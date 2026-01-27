using System.Diagnostics;
using System.Runtime.InteropServices;
using VoidWarp.Windows.Native;

namespace VoidWarp.Windows.Core
{
    /// <summary>
    /// Discovery state enum - mirrors Android's pattern
    /// </summary>
    public enum DiscoveryState
    {
        Idle,
        Starting,
        Discovering,
        Error
    }

    /// <summary>
    /// Event args for peers changed event
    /// </summary>
    public class PeersChangedEventArgs : EventArgs
    {
        public List<DiscoveredPeer> Peers { get; }
        public int Count => Peers.Count;

        public PeersChangedEventArgs(List<DiscoveredPeer> peers)
        {
            Peers = peers;
        }
    }

    /// <summary>
    /// Event args for discovery state changed event
    /// </summary>
    public class DiscoveryStateChangedEventArgs : EventArgs
    {
        public DiscoveryState State { get; }
        public string? Message { get; }

        public DiscoveryStateChangedEventArgs(DiscoveryState state, string? message = null)
        {
            State = state;
            Message = message;
        }
    }

    public class DiscoveryDiagnosticsChangedEventArgs : EventArgs
    {
        public string LocalIpAddress { get; }
        public string InterfaceName { get; }
        public string Detail { get; }

        public DiscoveryDiagnosticsChangedEventArgs(string localIpAddress, string interfaceName, string detail)
        {
            LocalIpAddress = localIpAddress;
            InterfaceName = interfaceName;
            Detail = detail;
        }
    }

    /// <summary>
    /// High-level wrapper around the VoidWarp native library
    /// Implements event-driven architecture similar to Android's StateFlow pattern
    /// </summary>
    public sealed class VoidWarpEngine : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;
        private Timer? _refreshTimer;
        private List<DiscoveredPeer> _lastPeers = new();
        private readonly object _lock = new();
        private string? _discoveryMessage;

        public string DeviceId { get; private set; } = string.Empty;
        public string LocalIpAddress { get; private set; } = string.Empty;
        public string LocalInterfaceName { get; private set; } = string.Empty;
        public string DiscoveryDiagnosticsDetail { get; private set; } = string.Empty;
        
        private DiscoveryState _discoveryState = DiscoveryState.Idle;
        public DiscoveryState DiscoveryState
        {
            get => _discoveryState;
            private set => UpdateDiscoveryState(value, _discoveryMessage);
        }

        public bool IsDiscovering => DiscoveryState == DiscoveryState.Discovering;

        /// <summary>
        /// Event fired when the list of discovered peers changes
        /// </summary>
        public event EventHandler<PeersChangedEventArgs>? PeersChanged;

        /// <summary>
        /// Event fired when the discovery state changes
        /// </summary>
        public event EventHandler<DiscoveryStateChangedEventArgs>? DiscoveryStateChanged;

        /// <summary>
        /// Event fired when discovery diagnostics change
        /// </summary>
        public event EventHandler<DiscoveryDiagnosticsChangedEventArgs>? DiscoveryDiagnosticsChanged;

        public VoidWarpEngine(string deviceName = "Windows Device")
        {
            Debug.WriteLine($"[VoidWarpEngine] Initializing with device name: {deviceName}");
            
            _handle = NativeBindings.voidwarp_init(deviceName);
            if (_handle == IntPtr.Zero)
            {
                Debug.WriteLine("[VoidWarpEngine] ERROR: Failed to initialize native handle");
                throw new InvalidOperationException("Failed to initialize VoidWarp engine");
            }

            DeviceId = NativeBindings.GetStringAndFree(
                NativeBindings.voidwarp_get_device_id(_handle)
            ) ?? "unknown";
            
            LocalIpAddress = "unknown";
            LocalInterfaceName = "unknown";
            
            Debug.WriteLine($"[VoidWarpEngine] Initialized successfully. DeviceId: {DeviceId}, LocalIP: {LocalIpAddress}");
        }

        /// <summary>
        /// Generate a new 6-digit pairing code
        /// </summary>
        public static string GeneratePairingCode()
        {
            return NativeBindings.GetStringAndFree(
                NativeBindings.voidwarp_generate_pairing_code()
            ) ?? "000-000";
        }

        /// <summary>
        /// Start mDNS discovery, advertising the specified receiver port
        /// Automatically starts background peer refresh
        /// </summary>
        /// <param name="receiverPort">The port that the file receiver is listening on</param>
        public bool StartDiscovery(ushort receiverPort)
        {
            Debug.WriteLine($"[VoidWarpEngine] Starting discovery on port {receiverPort}...");
            UpdateDiscoveryState(DiscoveryState.Starting, "正在选择本机网络接口...");
            
            try
            {
                var selector = new NetworkInterfaceSelector();
                var selection = selector.SelectBestInterface();
                var localIp = selection.IpAddress;
                LocalIpAddress = localIp ?? "auto-detect";
                LocalInterfaceName = selection.InterfaceName ?? "auto";
                DiscoveryDiagnosticsDetail = selection.Reason;

                Debug.WriteLine($"[VoidWarpEngine] Local IP: {LocalIpAddress} (if={LocalInterfaceName})");
                foreach (var candidate in selection.Candidates)
                {
                    Debug.WriteLine($"[VoidWarpEngine] Candidate: {candidate.InterfaceName} {candidate.IpAddress} score={candidate.Score} ({candidate.Reason})");
                }

                DiscoveryDiagnosticsChanged?.Invoke(
                    this,
                    new DiscoveryDiagnosticsChangedEventArgs(LocalIpAddress, LocalInterfaceName, DiscoveryDiagnosticsDetail)
                );
                
                int result = localIp == null
                    ? NativeBindings.voidwarp_start_discovery(_handle, receiverPort)
                    : NativeBindings.voidwarp_start_discovery_with_ip(_handle, receiverPort, localIp);
                
                Debug.WriteLine($"[VoidWarpEngine] Native discovery returned: {result}");
                
                // Auto-add localhost for USB/ADB forwarding scenarios
                if (result == 0)
                {
                    AddManualPeer("usb-android", "USB/Localhost", "127.0.0.1", receiverPort);
                }
                
                // Start background refresh timer (every 1 second, like Android)
                StartBackgroundRefresh();
                
                var modeHint = localIp == null
                    ? "未找到优先接口，使用系统自动选择 (可能降级)"
                    : $"使用接口 {LocalInterfaceName} / {LocalIpAddress}";
                UpdateDiscoveryState(DiscoveryState.Discovering, modeHint);
                Debug.WriteLine("[VoidWarpEngine] Discovery started successfully");
                
                // Trigger immediate refresh
                RefreshPeersInternal();
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoidWarpEngine] ERROR starting discovery: {ex.Message}");
                UpdateDiscoveryState(DiscoveryState.Error, $"启动失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Manually add a peer (e.g. for USB connections)
        /// </summary>
        public bool AddManualPeer(string id, string name, string ip, ushort port)
        {
            Debug.WriteLine($"[VoidWarpEngine] Adding manual peer: {name} at {ip}:{port}");
            var result = NativeBindings.voidwarp_add_manual_peer(_handle, id, name, ip, port);
            
            if (result == 0)
            {
                Debug.WriteLine("[VoidWarpEngine] Manual peer added successfully");
                // Trigger immediate refresh to show the new peer
                RefreshPeersInternal();
            }
            else
            {
                Debug.WriteLine($"[VoidWarpEngine] Failed to add manual peer, result: {result}");
            }
            
            return result == 0;
        }

        /// <summary>
        /// Stop mDNS discovery
        /// </summary>
        public void StopDiscovery()
        {
            Debug.WriteLine("[VoidWarpEngine] Stopping discovery...");
            
            StopBackgroundRefresh();
            NativeBindings.voidwarp_stop_discovery(_handle);
            
            lock (_lock)
            {
                _lastPeers.Clear();
            }
            
            UpdateDiscoveryState(DiscoveryState.Idle, "发现已停止");
            PeersChanged?.Invoke(this, new PeersChangedEventArgs(new List<DiscoveredPeer>()));
            
            Debug.WriteLine("[VoidWarpEngine] Discovery stopped");
        }

        /// <summary>
        /// Force a manual refresh of the peer list
        /// </summary>
        public void RefreshPeers()
        {
            RefreshPeersInternal();
        }

        /// <summary>
        /// Get list of discovered peers (current snapshot)
        /// </summary>
        public List<DiscoveredPeer> GetPeers()
        {
            lock (_lock)
            {
                return new List<DiscoveredPeer>(_lastPeers);
            }
        }

        private void StartBackgroundRefresh()
        {
            StopBackgroundRefresh();
            
            Debug.WriteLine("[VoidWarpEngine] Starting background refresh timer (1s interval)");
            _refreshTimer = new Timer(
                _ => RefreshPeersInternal(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1)
            );
        }

        private void StopBackgroundRefresh()
        {
            if (_refreshTimer != null)
            {
                Debug.WriteLine("[VoidWarpEngine] Stopping background refresh timer");
                _refreshTimer.Dispose();
                _refreshTimer = null;
            }
        }

        private void RefreshPeersInternal()
        {
            if (_disposed || _handle == IntPtr.Zero) return;
            
            try
            {
                var peers = FetchPeersFromNative();
                
                bool changed = false;
                lock (_lock)
                {
                    // Check if peers list has changed
                    if (peers.Count != _lastPeers.Count)
                    {
                        changed = true;
                    }
                    else
                    {
                        for (int i = 0; i < peers.Count; i++)
                        {
                            if (peers[i].DeviceId != _lastPeers[i].DeviceId ||
                                peers[i].IpAddress != _lastPeers[i].IpAddress ||
                                peers[i].Port != _lastPeers[i].Port)
                            {
                                changed = true;
                                break;
                            }
                        }
                    }
                    
                    if (changed)
                    {
                        _lastPeers = peers;
                    }
                }
                
                if (changed)
                {
                    Debug.WriteLine($"[VoidWarpEngine] Peers changed, count: {peers.Count}");
                    foreach (var peer in peers)
                    {
                        Debug.WriteLine($"  - {peer.DeviceName} ({peer.IpAddress}:{peer.Port})");
                    }
                    
                    // Fire event (UI should handle thread marshalling)
                    PeersChanged?.Invoke(this, new PeersChangedEventArgs(peers));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoidWarpEngine] ERROR refreshing peers: {ex.Message}");
            }
        }

        private List<DiscoveredPeer> FetchPeersFromNative()
        {
            var result = new List<DiscoveredPeer>();
            var list = NativeBindings.voidwarp_get_peers(_handle);

            if (list.Peers != IntPtr.Zero && list.Count > 0)
            {
                int structSize = Marshal.SizeOf<NativeBindings.FfiPeer>();
                for (nuint i = 0; i < list.Count; i++)
                {
                    IntPtr peerPtr = list.Peers + (int)(i * (nuint)structSize);
                    var ffiPeer = Marshal.PtrToStructure<NativeBindings.FfiPeer>(peerPtr);

                    result.Add(new DiscoveredPeer
                    {
                        DeviceId = Marshal.PtrToStringUTF8(ffiPeer.DeviceId) ?? "",
                        DeviceName = Marshal.PtrToStringUTF8(ffiPeer.DeviceName) ?? "",
                        IpAddress = Marshal.PtrToStringUTF8(ffiPeer.IpAddress) ?? "",
                        Port = ffiPeer.Port
                    });
                }

                NativeBindings.voidwarp_free_peer_list(list);
            }

            return result;
        }

        public static bool TestConnection(DiscoveredPeer peer)
        {
            Debug.WriteLine($"[VoidWarpEngine] Testing connection to {peer.DeviceName}...");
            
            var ips = (peer.IpAddress ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var ip in ips)
            {
                Debug.WriteLine($"[VoidWarpEngine] Trying {ip.Trim()}:{peer.Port}...");
                if (NativeBindings.voidwarp_transport_ping(ip.Trim(), peer.Port))
                {
                    Debug.WriteLine($"[VoidWarpEngine] Connection test PASSED for {ip.Trim()}");
                    return true;
                }
            }
            
            Debug.WriteLine($"[VoidWarpEngine] Connection test FAILED for all IPs");
            return false;
        }

        private void UpdateDiscoveryState(DiscoveryState newState, string? message)
        {
            if (_discoveryState != newState || _discoveryMessage != message)
            {
                _discoveryState = newState;
                _discoveryMessage = message;
                Debug.WriteLine($"[VoidWarpEngine] Discovery state changed: {newState} ({message})");
                DiscoveryStateChanged?.Invoke(this, new DiscoveryStateChangedEventArgs(newState, message));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Debug.WriteLine("[VoidWarpEngine] Disposing...");
                
                StopBackgroundRefresh();
                
                if (_handle != IntPtr.Zero)
                {
                    NativeBindings.voidwarp_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
                
                Debug.WriteLine("[VoidWarpEngine] Disposed");
            }
        }
    }

    /// <summary>
    /// Represents a discovered peer on the network
    /// </summary>
    public class DiscoveredPeer
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;  // Can be comma-separated list
        public ushort Port { get; set; }

        /// <summary>
        /// Get the best (first) IP address from the list
        /// </summary>
        public string BestIp => IpAddress.Split(',').FirstOrDefault()?.Trim() ?? "";

        /// <summary>
        /// Get all IP addresses as a list
        /// </summary>
        public List<string> AllIps => IpAddress
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(ip => ip.Trim())
            .ToList();

        /// <summary>
        /// Display name for UI (shows first IP if multiple)
        /// </summary>
        public string DisplayName
        {
            get
            {
                var ips = AllIps;
                if (ips.Count > 1)
                {
                    return $"{DeviceName} ({ips[0]}...)";
                }
                return $"{DeviceName} ({IpAddress})";
            }
        }

        public override string ToString() => $"{DeviceName} ({IpAddress}:{Port})";
    }
}
