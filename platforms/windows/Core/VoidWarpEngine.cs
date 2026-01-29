using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoidWarp.Windows.Native;

namespace VoidWarp.Windows.Core
{
    #region Event Args

    public class LogEventArgs : EventArgs
    {
        public string Message { get; }
        public LogLevel Level { get; }
        public DateTime Timestamp { get; }

        public LogEventArgs(string message, LogLevel level = LogLevel.Info)
        {
            Message = message;
            Level = level;
            Timestamp = DateTime.Now;
        }
    }

    public enum LogLevel { Debug, Info, Warning, Error }

    public class PeerDiscoveredEventArgs : EventArgs
    {
        public List<PeerItem> Peers { get; }
        public int Count => Peers.Count;

        public PeerDiscoveredEventArgs(List<PeerItem> peers)
        {
            Peers = peers ?? new List<PeerItem>();
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public double Percentage { get; }
        public string FormattedProgress { get; }
        public string FormattedSpeed { get; }
        public ulong BytesTransferred { get; }
        public ulong TotalBytes { get; }

        public ProgressEventArgs(double percentage, ulong bytesTransferred = 0, ulong totalBytes = 0, string speed = "")
        {
            Percentage = Math.Min(100, Math.Max(0, percentage));
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            FormattedProgress = $"{Percentage:F1}%";
            FormattedSpeed = speed;
        }
    }

    public class TransferCompleteEventArgs : EventArgs
    {
        public bool Success { get; }
        public string? ErrorMessage { get; }
        public string? FilePath { get; }

        public TransferCompleteEventArgs(bool success, string? errorMessage = null, string? filePath = null)
        {
            Success = success;
            ErrorMessage = errorMessage;
            FilePath = filePath;
        }
    }

    public class PendingTransferEventArgs : EventArgs
    {
        public string FileName { get; }
        public long FileSize { get; }
        public string SenderName { get; }
        public string SenderAddress { get; }
        public string FormattedSize { get; }

        public PendingTransferEventArgs(string fileName, long fileSize, string senderName, string senderAddress)
        {
            FileName = fileName ?? "unknown";
            FileSize = fileSize;
            SenderName = senderName ?? "unknown";
            SenderAddress = senderAddress ?? "unknown";
            FormattedSize = FormatFileSize(fileSize);
        }

        private static string FormatFileSize(long bytes)
        {
            return bytes switch
            {
                >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
                >= 1024L * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
                >= 1024 => $"{bytes / 1024.0:F1} KB",
                _ => $"{bytes} B"
            };
        }
    }

    #endregion

    #region Data Models

    public class PeerItem
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public ushort Port { get; set; }

        /// <summary>
        /// Get the best (first) IP address for connection attempts.
        /// </summary>
        public string BestIp => IpAddress.Split(',').FirstOrDefault()?.Trim() ?? "";

        /// <summary>
        /// Get all IP addresses as a list (for multi-IP peers).
        /// </summary>
        public List<string> AllIps => IpAddress
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(ip => ip.Trim())
            .Where(ip => !string.IsNullOrEmpty(ip))
            .ToList();

        /// <summary>
        /// Display-friendly name showing IP:Port.
        /// </summary>
        public string DisplayName => $"{BestIp}:{Port}";

        /// <summary>
        /// Short device ID for display.
        /// </summary>
        public string ShortId => DeviceId.Length >= 8 ? DeviceId[..8].ToUpperInvariant() : DeviceId.ToUpperInvariant();

        public override string ToString() => $"{DeviceName} ({DisplayName})";

        public override bool Equals(object? obj) =>
            obj is PeerItem other && DeviceId == other.DeviceId;

        public override int GetHashCode() => DeviceId.GetHashCode();
    }

    #endregion

    /// <summary>
    /// Singleton async wrapper for VoidWarp native library.
    /// Mimics Android's TransferManager pattern with C# events.
    /// Thread-safe with proper exception handling.
    /// </summary>
    public sealed class VoidWarpEngine : IDisposable
    {
        #region Singleton

        private static readonly Lazy<VoidWarpEngine> _instance = new(() => new VoidWarpEngine());
        public static VoidWarpEngine Instance => _instance.Value;

        #endregion

        #region Fields

        private IntPtr _handle;
        private IntPtr _receiverHandle;
        private IntPtr _senderHandle;
        private bool _disposed;
        private readonly object _lock = new();

        private CancellationTokenSource? _discoveryCts;
        private CancellationTokenSource? _receiverCts;
        private CancellationTokenSource? _senderCts;

        private Task? _discoveryTask;
        private Task? _receiverTask;
        private Task? _senderTask;

        private List<PeerItem> _cachedPeers = new();
        private string _currentDeviceId = string.Empty;

        #endregion

        #region Properties

        /// <summary>
        /// The unique device ID for this instance.
        /// </summary>
        public string DeviceId
        {
            get => _currentDeviceId;
            private set => _currentDeviceId = value;
        }

        /// <summary>
        /// The device name (machine name).
        /// </summary>
        public string DeviceName { get; } = Environment.MachineName;

        /// <summary>
        /// Short device ID for UI display.
        /// </summary>
        public string ShortDeviceId => DeviceId.Length >= 8 
            ? DeviceId[..8].ToUpperInvariant() 
            : DeviceId.ToUpperInvariant();

        /// <summary>
        /// True if discovery is currently running.
        /// </summary>
        public bool IsDiscovering => _discoveryCts != null && !_discoveryCts.IsCancellationRequested;

        /// <summary>
        /// True if the receiver is currently running.
        /// </summary>
        public bool IsReceiving => _receiverHandle != IntPtr.Zero && _receiverCts != null && !_receiverCts.IsCancellationRequested;

        /// <summary>
        /// True if currently sending a file.
        /// </summary>
        public bool IsSending => _senderCts != null && !_senderCts.IsCancellationRequested;

        /// <summary>
        /// The port the receiver is listening on (0 if not active).
        /// </summary>
        public ushort ReceiverPort { get; private set; }

        /// <summary>
        /// True if the native library was loaded successfully.
        /// </summary>
        public bool NativeLoaded => NativeBindings.IsLoaded;

        /// <summary>
        /// Any error message from native library loading.
        /// </summary>
        public string? NativeLoadError => NativeBindings.LoadError;

        /// <summary>
        /// True if the engine is properly initialized.
        /// </summary>
        public bool IsInitialized => _handle != IntPtr.Zero;

        #endregion

        #region Events

        /// <summary>
        /// Fired when a log message is generated.
        /// </summary>
        public event EventHandler<LogEventArgs>? OnLog;

        /// <summary>
        /// Fired when peers are discovered or updated.
        /// </summary>
        public event EventHandler<PeerDiscoveredEventArgs>? OnPeerDiscovered;

        /// <summary>
        /// Fired when transfer progress updates.
        /// </summary>
        public event EventHandler<ProgressEventArgs>? OnProgress;

        /// <summary>
        /// Fired when a transfer completes (success or failure).
        /// </summary>
        public event EventHandler<TransferCompleteEventArgs>? OnTransferComplete;

        /// <summary>
        /// Fired when an incoming transfer is pending user acceptance.
        /// </summary>
        public event EventHandler<PendingTransferEventArgs>? OnPendingTransfer;

        #endregion

        #region Constructor & Initialization

        private VoidWarpEngine()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                Log($"Initializing VoidWarpEngine for '{DeviceName}'...", LogLevel.Info);

                // Check if native library is available
                if (!string.IsNullOrEmpty(NativeBindings.LoadError))
                {
                    Log($"Native library warning: {NativeBindings.LoadError}", LogLevel.Warning);
                }

                // Initialize native handle
                _handle = NativeBindings.voidwarp_init(DeviceName);
                
                if (_handle == IntPtr.Zero)
                {
                    Log("Failed to initialize native handle - DLL may not be loaded correctly", LogLevel.Error);
                    return;
                }

                // Get device ID
                var idPtr = NativeBindings.voidwarp_get_device_id(_handle);
                DeviceId = NativeBindings.GetStringAndFree(idPtr) ?? Guid.NewGuid().ToString("N");

                Log($"Engine initialized successfully. Device ID: {ShortDeviceId}", LogLevel.Info);
            }
            catch (DllNotFoundException ex)
            {
                Log($"Native library not found: {ex.Message}", LogLevel.Error);
            }
            catch (Exception ex)
            {
                Log($"Initialization error: {ex.Message}", LogLevel.Error);
            }
        }

        #endregion

        #region Discovery

        /// <summary>
        /// Start device discovery on the network.
        /// Runs in a background Task.Run loop, polling peers periodically.
        /// </summary>
        public void StartDiscovery(ushort port)
        {
            if (IsDiscovering)
            {
                Log("Discovery already running", LogLevel.Warning);
                return;
            }

            if (_handle == IntPtr.Zero)
            {
                Log("Cannot start discovery - engine not initialized", LogLevel.Error);
                return;
            }

            _discoveryCts = new CancellationTokenSource();
            var token = _discoveryCts.Token;

            _discoveryTask = Task.Run(async () =>
            {
                try
                {
                    Log($"Starting discovery on port {port}...", LogLevel.Info);

                    // Get local IP for mDNS binding
                    var localIp = GetPrimaryLocalIp();
                    Log($"Using local IP: {localIp}", LogLevel.Debug);

                    // Start native discovery
                    int result;
                    if (!string.IsNullOrEmpty(localIp) && localIp != "127.0.0.1")
                    {
                        result = NativeBindings.voidwarp_start_discovery_with_ip(_handle, port, localIp);
                    }
                    else
                    {
                        result = NativeBindings.voidwarp_start_discovery(_handle, port);
                    }

                    if (result != 0)
                    {
                        Log($"Native discovery returned code {result} (may be running in fallback mode)", LogLevel.Warning);
                    }

                    Log("Discovery started - polling for peers...", LogLevel.Info);

                    // Polling loop - refresh peers periodically
                    while (!token.IsCancellationRequested)
                    {
                        RefreshPeers();
                        await Task.Delay(1000, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("Discovery stopped by user", LogLevel.Info);
                }
                catch (DllNotFoundException ex)
                {
                    Log($"DLL error during discovery: {ex.Message}", LogLevel.Error);
                }
                catch (Exception ex)
                {
                    Log($"Discovery error: {ex.Message}", LogLevel.Error);
                }
            }, token);
        }

        /// <summary>
        /// Stop device discovery.
        /// </summary>
        public void StopDiscovery()
        {
            if (_discoveryCts == null) return;

            try
            {
                Log("Stopping discovery...", LogLevel.Info);
                _discoveryCts.Cancel();
                
                if (_handle != IntPtr.Zero)
                {
                    NativeBindings.voidwarp_stop_discovery(_handle);
                }
            }
            catch (Exception ex)
            {
                Log($"Error stopping discovery: {ex.Message}", LogLevel.Warning);
            }
            finally
            {
                _discoveryCts?.Dispose();
                _discoveryCts = null;
            }
        }

        /// <summary>
        /// Manually refresh the peer list from native code.
        /// </summary>
        public void RefreshPeers()
        {
            if (_handle == IntPtr.Zero) return;

            try
            {
                var peers = FetchPeersFromNative();
                
                // Filter out self
                peers = peers.Where(p => p.DeviceId != DeviceId).ToList();

                // Check if peers changed
                bool changed = HasPeersChanged(peers);

                if (changed)
                {
                    lock (_lock)
                    {
                        _cachedPeers = peers;
                    }
                    OnPeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(peers));
                    
                    if (peers.Count > 0)
                    {
                        Log($"Discovered {peers.Count} peer(s)", LogLevel.Debug);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error refreshing peers: {ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// Add a manual peer for direct connection (USB/localhost).
        /// </summary>
        public bool AddManualPeer(string id, string name, string ip, ushort port)
        {
            if (_handle == IntPtr.Zero)
            {
                Log("Cannot add peer - engine not initialized", LogLevel.Error);
                return false;
            }

            try
            {
                int result = NativeBindings.voidwarp_add_manual_peer(_handle, id, name, ip, port);
                if (result == 0)
                {
                    Log($"Added manual peer: {name} ({ip}:{port})", LogLevel.Info);
                    RefreshPeers();
                    return true;
                }
                else
                {
                    Log($"Failed to add manual peer (code: {result})", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"Error adding manual peer: {ex.Message}", LogLevel.Error);
            }
            return false;
        }

        private List<PeerItem> FetchPeersFromNative()
        {
            var result = new List<PeerItem>();
            var list = NativeBindings.voidwarp_get_peers(_handle);

            if (list.Peers == IntPtr.Zero || list.Count == 0)
            {
                return result;
            }

            try
            {
                int structSize = Marshal.SizeOf<NativeBindings.FfiPeer>();
                for (nuint i = 0; i < list.Count; i++)
                {
                    IntPtr peerPtr = list.Peers + (int)(i * (nuint)structSize);
                    var ffiPeer = Marshal.PtrToStructure<NativeBindings.FfiPeer>(peerPtr);

                    var peer = new PeerItem
                    {
                        DeviceId = NativeBindings.GetString(ffiPeer.DeviceId) ?? "",
                        DeviceName = NativeBindings.GetString(ffiPeer.DeviceName) ?? "",
                        IpAddress = NativeBindings.GetString(ffiPeer.IpAddress) ?? "",
                        Port = ffiPeer.Port
                    };

                    if (!string.IsNullOrEmpty(peer.DeviceId))
                    {
                        result.Add(peer);
                    }
                }
            }
            finally
            {
                NativeBindings.voidwarp_free_peer_list(list);
            }

            return result;
        }

        private bool HasPeersChanged(List<PeerItem> newPeers)
        {
            lock (_lock)
            {
                if (newPeers.Count != _cachedPeers.Count) return true;
                
                for (int i = 0; i < newPeers.Count; i++)
                {
                    var newPeer = newPeers[i];
                    var oldPeer = _cachedPeers.FirstOrDefault(p => p.DeviceId == newPeer.DeviceId);
                    
                    if (oldPeer == null ||
                        oldPeer.IpAddress != newPeer.IpAddress ||
                        oldPeer.Port != newPeer.Port)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        #endregion

        #region File Sending

        /// <summary>
        /// Send a file to a peer asynchronously.
        /// Tries all available IPs until one succeeds.
        /// </summary>
        public async Task SendFileAsync(string filePath, PeerItem peer)
        {
            if (IsSending)
            {
                Log("Already sending a file", LogLevel.Warning);
                return;
            }

            if (!File.Exists(filePath))
            {
                Log($"File not found: {filePath}", LogLevel.Error);
                OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(false, "File not found"));
                return;
            }

            _senderCts = new CancellationTokenSource();
            var token = _senderCts.Token;

            _senderTask = Task.Run(async () =>
            {
                IntPtr senderHandle = IntPtr.Zero;
                
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var fileSize = new FileInfo(filePath).Length;
                    Log($"Sending: {fileName} ({FormatSize(fileSize)}) -> {peer.DeviceName}", LogLevel.Info);

                    // Create sender
                    senderHandle = NativeBindings.voidwarp_tcp_sender_create(filePath);
                    if (senderHandle == IntPtr.Zero)
                    {
                        Log("Failed to create sender handle", LogLevel.Error);
                        OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(false, "Failed to create sender"));
                        return;
                    }

                    _senderHandle = senderHandle;

                    // Start progress monitoring
                    var progressTask = MonitorSendProgressAsync(senderHandle, (ulong)fileSize, token);

                    // Try each IP address until success
                    int result = -1;
                    string? successIp = null;

                    foreach (var ip in peer.AllIps)
                    {
                        if (token.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(ip)) continue;

                        Log($"Trying {ip}:{peer.Port}...", LogLevel.Debug);
                        
                        result = NativeBindings.voidwarp_tcp_sender_start(senderHandle, ip, peer.Port, DeviceName);

                        if (result == 0)
                        {
                            successIp = ip;
                            Log($"Transfer completed via {ip}", LogLevel.Info);
                            break;
                        }
                        else if (result == 3) // Connection failed, try next IP
                        {
                            Log($"Connection failed to {ip}, trying next...", LogLevel.Debug);
                            continue;
                        }
                        else
                        {
                            // Fatal error, don't try other IPs
                            break;
                        }
                    }

                    // Wait for progress monitoring to finish
                    try { await progressTask; } catch { }

                    // Interpret result
                    var (success, error) = InterpretSendResult(result);
                    
                    OnProgress?.Invoke(this, new ProgressEventArgs(success ? 100 : 0));
                    OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(success, error, filePath));
                    
                    Log(success ? "Transfer completed successfully" : $"Transfer failed: {error}", 
                        success ? LogLevel.Info : LogLevel.Error);
                }
                catch (OperationCanceledException)
                {
                    Log("Transfer cancelled by user", LogLevel.Info);
                    OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(false, "Cancelled"));
                }
                catch (DllNotFoundException ex)
                {
                    Log($"DLL error: {ex.Message}", LogLevel.Error);
                    OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(false, "Native library error"));
                }
                catch (Exception ex)
                {
                    Log($"Send error: {ex.Message}", LogLevel.Error);
                    OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(false, ex.Message));
                }
                finally
                {
                    // Cleanup sender
                    if (senderHandle != IntPtr.Zero)
                    {
                        NativeBindings.voidwarp_tcp_sender_destroy(senderHandle);
                    }
                    _senderHandle = IntPtr.Zero;
                    _senderCts?.Dispose();
                    _senderCts = null;
                }
            }, token);

            await _senderTask;
        }

        private async Task MonitorSendProgressAsync(IntPtr sender, ulong totalBytes, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && sender != IntPtr.Zero)
                {
                    float progress = NativeBindings.voidwarp_tcp_sender_get_progress(sender);
                    OnProgress?.Invoke(this, new ProgressEventArgs(progress, (ulong)(progress / 100.0 * totalBytes), totalBytes));
                    
                    if (progress >= 100) break;
                    await Task.Delay(200, token);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private (bool success, string? error) InterpretSendResult(int result)
        {
            return result switch
            {
                0 => (true, null),
                1 => (false, "Transfer rejected by receiver"),
                2 => (false, "Checksum verification failed"),
                3 => (false, "Connection failed - check if receiver is online"),
                4 => (false, "Transfer timed out"),
                5 => (false, "Transfer was cancelled"),
                6 => (false, "IO error during transfer"),
                -1 => (false, "Invalid sender handle"),
                _ => (false, $"Unknown error (code: {result})")
            };
        }

        /// <summary>
        /// Cancel the current send operation.
        /// </summary>
        public void CancelSend()
        {
            if (_senderHandle != IntPtr.Zero)
            {
                NativeBindings.voidwarp_tcp_sender_cancel(_senderHandle);
            }
            _senderCts?.Cancel();
            Log("Send cancelled", LogLevel.Info);
        }

        #endregion

        #region File Receiving

        /// <summary>
        /// Start the receiver to listen for incoming transfers.
        /// </summary>
        public void StartReceiver()
        {
            if (IsReceiving)
            {
                Log("Receiver already running", LogLevel.Warning);
                return;
            }

            _receiverCts = new CancellationTokenSource();
            var token = _receiverCts.Token;

            _receiverTask = Task.Run(async () =>
            {
                try
                {
                    // Create receiver
                    _receiverHandle = NativeBindings.voidwarp_create_receiver();
                    if (_receiverHandle == IntPtr.Zero)
                    {
                        Log("Failed to create receiver", LogLevel.Error);
                        return;
                    }

                    // Get assigned port
                    ReceiverPort = NativeBindings.voidwarp_receiver_get_port(_receiverHandle);
                    Log($"Receiver created on port {ReceiverPort}", LogLevel.Info);

                    // Start listening
                    NativeBindings.voidwarp_receiver_start(_receiverHandle);
                    Log("Receiver listening for connections...", LogLevel.Info);

                    // Polling loop to check receiver state
                    while (!token.IsCancellationRequested && _receiverHandle != IntPtr.Zero)
                    {
                        int state = NativeBindings.voidwarp_receiver_get_state(_receiverHandle);

                        await HandleReceiverState((ReceiverState)state, token);
                        await Task.Delay(200, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("Receiver stopped by user", LogLevel.Info);
                }
                catch (DllNotFoundException ex)
                {
                    Log($"DLL error: {ex.Message}", LogLevel.Error);
                }
                catch (Exception ex)
                {
                    Log($"Receiver error: {ex.Message}", LogLevel.Error);
                }
                finally
                {
                    CleanupReceiver();
                }
            }, token);
        }

        private async Task HandleReceiverState(ReceiverState state, CancellationToken token)
        {
            switch (state)
            {
                case ReceiverState.AwaitingAccept:
                    // Get pending transfer info
                    var pending = NativeBindings.voidwarp_receiver_get_pending(_receiverHandle);
                    if (pending.IsValid)
                    {
                        var fileName = NativeBindings.GetString(pending.FileName) ?? "unknown";
                        var senderName = NativeBindings.GetString(pending.SenderName) ?? "unknown";
                        var senderAddr = NativeBindings.GetString(pending.SenderAddr) ?? "unknown";
                        
                        Log($"Incoming transfer: {fileName} from {senderName}", LogLevel.Info);
                        OnPendingTransfer?.Invoke(this, new PendingTransferEventArgs(
                            fileName, (long)pending.FileSize, senderName, senderAddr));
                        
                        NativeBindings.voidwarp_free_pending_transfer(pending);
                        
                        // Wait a bit before polling again to avoid spamming the event
                        await Task.Delay(1000, token);
                    }
                    break;

                case ReceiverState.Receiving:
                    float progress = NativeBindings.voidwarp_receiver_get_progress(_receiverHandle);
                    ulong received = NativeBindings.voidwarp_receiver_get_bytes_received(_receiverHandle);
                    OnProgress?.Invoke(this, new ProgressEventArgs(progress, received, 0));
                    break;

                case ReceiverState.Completed:
                    Log("File received successfully", LogLevel.Info);
                    OnProgress?.Invoke(this, new ProgressEventArgs(100));
                    OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(true));
                    
                    // Reset receiver to listen for more transfers
                    NativeBindings.voidwarp_receiver_start(_receiverHandle);
                    break;

                case ReceiverState.Error:
                    Log("Receive error occurred", LogLevel.Error);
                    OnTransferComplete?.Invoke(this, new TransferCompleteEventArgs(false, "Receive error"));
                    
                    // Reset receiver
                    NativeBindings.voidwarp_receiver_start(_receiverHandle);
                    break;
            }
        }

        /// <summary>
        /// Accept a pending incoming transfer.
        /// </summary>
        public bool AcceptTransfer(string savePath)
        {
            if (_receiverHandle == IntPtr.Zero)
            {
                Log("Cannot accept - receiver not active", LogLevel.Error);
                return false;
            }

            try
            {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                int result = NativeBindings.voidwarp_receiver_accept(_receiverHandle, savePath);
                if (result == 0)
                {
                    Log($"Transfer accepted, saving to: {savePath}", LogLevel.Info);
                    return true;
                }
                else
                {
                    Log($"Failed to accept transfer (code: {result})", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Accept error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Reject a pending incoming transfer.
        /// </summary>
        public void RejectTransfer()
        {
            if (_receiverHandle == IntPtr.Zero) return;

            try
            {
                NativeBindings.voidwarp_receiver_reject(_receiverHandle);
                Log("Transfer rejected", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"Reject error: {ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// Stop the receiver.
        /// </summary>
        public void StopReceiver()
        {
            _receiverCts?.Cancel();
            
            if (_receiverHandle != IntPtr.Zero)
            {
                try { NativeBindings.voidwarp_receiver_stop(_receiverHandle); }
                catch { }
            }
            
            Log("Receiver stopping...", LogLevel.Info);
        }

        private void CleanupReceiver()
        {
            if (_receiverHandle != IntPtr.Zero)
            {
                try { NativeBindings.voidwarp_destroy_receiver(_receiverHandle); }
                catch { }
                finally { _receiverHandle = IntPtr.Zero; }
            }
            
            ReceiverPort = 0;
            _receiverCts?.Dispose();
            _receiverCts = null;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Test if a peer is reachable.
        /// </summary>
        public bool TestConnection(PeerItem peer)
        {
            try
            {
                foreach (var ip in peer.AllIps)
                {
                    if (string.IsNullOrWhiteSpace(ip)) continue;
                    
                    if (NativeBindings.voidwarp_transport_ping(ip, peer.Port))
                    {
                        Log($"Connection test passed: {ip}:{peer.Port}", LogLevel.Info);
                        return true;
                    }
                }
                Log($"Connection test failed for {peer.DeviceName}", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Log($"Connection test error: {ex.Message}", LogLevel.Warning);
            }
            return false;
        }

        /// <summary>
        /// Get the primary local IP address for network operations.
        /// </summary>
        public static string GetPrimaryLocalIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    // Prioritize WiFi and Ethernet
                    var name = ni.Name.ToLowerInvariant();
                    bool isPriority = name.Contains("wi-fi") || name.Contains("wifi") || 
                                     name.Contains("ethernet") || name.Contains("eth");

                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ip = addr.Address.ToString();
                            if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                            {
                                if (isPriority) return ip;
                            }
                        }
                    }
                }

                // Fallback: return first non-loopback IPv4
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !addr.Address.ToString().StartsWith("127."))
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch { }
            
            return "127.0.0.1";
        }

        /// <summary>
        /// Get all local IP addresses with interface names.
        /// </summary>
        public static List<string> GetAllLocalIpAddresses()
        {
            var ips = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ips.Add($"{ni.Name}: {addr.Address}");
                        }
                    }
                }
            }
            catch { }
            return ips;
        }

        private static string FormatSize(long bytes)
        {
            return bytes switch
            {
                >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
                >= 1024L * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
                >= 1024 => $"{bytes / 1024.0:F1} KB",
                _ => $"{bytes} B"
            };
        }

        private void Log(string message, LogLevel level)
        {
            Debug.WriteLine($"[VoidWarpEngine] [{level}] {message}");
            OnLog?.Invoke(this, new LogEventArgs(message, level));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            Log("Disposing VoidWarpEngine...", LogLevel.Info);

            // Stop all operations
            StopDiscovery();
            StopReceiver();
            CancelSend();

            // Destroy main handle
            if (_handle != IntPtr.Zero)
            {
                try { NativeBindings.voidwarp_destroy(_handle); }
                catch { }
                finally { _handle = IntPtr.Zero; }
            }

            _disposed = true;
            Log("Engine disposed", LogLevel.Info);
        }

        #endregion
    }
}
