using System.Diagnostics;
using System.Runtime.InteropServices;
using VoidWarp.Windows.Native;

namespace VoidWarp.Windows.Core
{
    /// <summary>
    /// Receiver state enum matching Rust FFI
    /// </summary>
    public enum ReceiverState
    {
        Idle = 0,
        Listening = 1,
        AwaitingAccept = 2,
        Receiving = 3,
        Completed = 4,
        Error = 5
    }

    /// <summary>
    /// Pending transfer information
    /// </summary>
    public class PendingTransferInfo
    {
        public string SenderName { get; set; } = string.Empty;
        public string SenderAddress { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public ulong FileSize { get; set; }

        public string FormattedSize => FileSize switch
        {
            >= 1024 * 1024 * 1024 => $"{FileSize / 1024.0 / 1024.0 / 1024.0:F2} GB",
            >= 1024 * 1024 => $"{FileSize / 1024.0 / 1024.0:F1} MB",
            >= 1024 => $"{FileSize / 1024.0:F1} KB",
            _ => $"{FileSize} bytes"
        };
    }

    /// <summary>
    /// Event args for state changed event
    /// </summary>
    public class ReceiverStateChangedEventArgs : EventArgs
    {
        public ReceiverState OldState { get; }
        public ReceiverState NewState { get; }

        public ReceiverStateChangedEventArgs(ReceiverState oldState, ReceiverState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// Manages file receiving operations using the native core
    /// Implements event-driven architecture similar to Android's StateFlow pattern
    /// </summary>
    public class ReceiveManager : IDisposable
    {
        private IntPtr _receiverHandle = IntPtr.Zero;
        private CancellationTokenSource? _pollCts;
        private bool _disposed = false;
        private ReceiverState _lastState = ReceiverState.Idle;
        private PendingTransferInfo? _lastPendingTransfer;
        private bool _pendingTransferHandled = false;

        /// <summary>
        /// The port the receiver is listening on
        /// </summary>
        public ushort Port { get; private set; }

        /// <summary>
        /// Whether the receiver is initialized and ready
        /// </summary>
        public bool IsInitialized => _receiverHandle != IntPtr.Zero && Port > 0;

        /// <summary>
        /// Whether actively receiving or listening
        /// </summary>
        public bool IsReceiving => State == ReceiverState.Listening || State == ReceiverState.Receiving;

        /// <summary>
        /// Current receiver state
        /// </summary>
        public ReceiverState State
        {
            get
            {
                if (_receiverHandle == IntPtr.Zero) return ReceiverState.Idle;
                return (ReceiverState)NativeBindings.voidwarp_receiver_get_state(_receiverHandle);
            }
        }

        /// <summary>
        /// Event fired when an incoming transfer is detected
        /// </summary>
        public event Action<PendingTransferInfo>? TransferRequested;

        /// <summary>
        /// Event fired when transfer progress changes
        /// </summary>
        public event Action<float>? ProgressChanged;

        /// <summary>
        /// Event fired when a transfer completes (success or failure)
        /// </summary>
        public event Action<bool, string?>? TransferCompleted;

        /// <summary>
        /// Event fired when receiver state changes
        /// </summary>
        public event EventHandler<ReceiverStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Event fired when initialization is complete
        /// </summary>
        public event EventHandler<ushort>? Initialized;

        public ReceiveManager()
        {
            Debug.WriteLine("[ReceiveManager] Creating receiver...");
            
            _receiverHandle = NativeBindings.voidwarp_create_receiver();
            if (_receiverHandle == IntPtr.Zero)
            {
                Debug.WriteLine("[ReceiveManager] ERROR: Failed to create native receiver");
                throw new InvalidOperationException("Failed to create file receiver");
            }

            Port = NativeBindings.voidwarp_receiver_get_port(_receiverHandle);
            
            if (Port <= 0)
            {
                Debug.WriteLine("[ReceiveManager] ERROR: Invalid port returned");
                NativeBindings.voidwarp_destroy_receiver(_receiverHandle);
                _receiverHandle = IntPtr.Zero;
                throw new InvalidOperationException("Failed to bind receiver to a valid port");
            }
            
            Debug.WriteLine($"[ReceiveManager] Created successfully on port {Port}");
            
            // Fire initialized event
            Initialized?.Invoke(this, Port);
        }

        /// <summary>
        /// Start listening for incoming transfers
        /// </summary>
        public void StartReceiving()
        {
            if (_receiverHandle == IntPtr.Zero)
            {
                Debug.WriteLine("[ReceiveManager] Cannot start: handle is null");
                return;
            }

            Debug.WriteLine($"[ReceiveManager] Starting receiver on port {Port}...");
            
            NativeBindings.voidwarp_receiver_start(_receiverHandle);
            
            // Reset pending transfer state
            _lastPendingTransfer = null;
            _pendingTransferHandled = false;
            
            StartPolling();
            
            Debug.WriteLine("[ReceiveManager] Receiver started, polling active");
        }

        /// <summary>
        /// Stop listening
        /// </summary>
        public void StopReceiving()
        {
            Debug.WriteLine("[ReceiveManager] Stopping receiver...");
            
            _pollCts?.Cancel();
            _pollCts = null;

            if (_receiverHandle != IntPtr.Zero)
            {
                NativeBindings.voidwarp_receiver_stop(_receiverHandle);
            }
            
            UpdateState(ReceiverState.Idle);
            
            Debug.WriteLine("[ReceiveManager] Receiver stopped");
        }

        /// <summary>
        /// Accept the pending transfer
        /// </summary>
        public async Task<bool> AcceptTransferAsync(string savePath)
        {
            if (_receiverHandle == IntPtr.Zero)
            {
                Debug.WriteLine("[ReceiveManager] Cannot accept: handle is null");
                return false;
            }

            Debug.WriteLine($"[ReceiveManager] Accepting transfer, saving to: {savePath}");
            _pendingTransferHandled = true;
            
            return await Task.Run(() =>
            {
                int result = NativeBindings.voidwarp_receiver_accept(_receiverHandle, savePath);
                if (result == 0)
                {
                    Debug.WriteLine("[ReceiveManager] Transfer accepted and completed successfully");
                    TransferCompleted?.Invoke(true, null);
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ReceiveManager] Transfer failed with result: {result}");
                    TransferCompleted?.Invoke(false, "Transfer failed");
                    return false;
                }
            });
        }

        /// <summary>
        /// Reject the pending transfer
        /// </summary>
        public void RejectTransfer()
        {
            if (_receiverHandle == IntPtr.Zero)
            {
                Debug.WriteLine("[ReceiveManager] Cannot reject: handle is null");
                return;
            }

            Debug.WriteLine("[ReceiveManager] Rejecting transfer...");
            _pendingTransferHandled = true;
            
            var result = NativeBindings.voidwarp_receiver_reject(_receiverHandle);
            if (result != 0)
            {
                Debug.WriteLine($"[ReceiveManager] Reject failed with result: {result}");
                TransferCompleted?.Invoke(false, "Reject failed");
            }
            else
            {
                Debug.WriteLine("[ReceiveManager] Transfer rejected successfully");
            }
        }

        /// <summary>
        /// Get current pending transfer info
        /// </summary>
        public PendingTransferInfo? GetPendingTransfer()
        {
            if (_receiverHandle == IntPtr.Zero) return null;

            var pending = NativeBindings.voidwarp_receiver_get_pending(_receiverHandle);
            if (!pending.IsValid)
            {
                return null;
            }

            var info = new PendingTransferInfo
            {
                SenderName = Marshal.PtrToStringUTF8(pending.SenderName) ?? "Unknown",
                SenderAddress = Marshal.PtrToStringUTF8(pending.SenderAddr) ?? "",
                FileName = Marshal.PtrToStringUTF8(pending.FileName) ?? "unknown",
                FileSize = pending.FileSize
            };

            NativeBindings.voidwarp_free_pending_transfer(pending);
            return info;
        }

        /// <summary>
        /// Get current progress percentage
        /// </summary>
        public float GetProgress()
        {
            if (_receiverHandle == IntPtr.Zero) return 0;
            return NativeBindings.voidwarp_receiver_get_progress(_receiverHandle);
        }

        /// <summary>
        /// Get bytes received so far
        /// </summary>
        public ulong GetBytesReceived()
        {
            if (_receiverHandle == IntPtr.Zero) return 0;
            return NativeBindings.voidwarp_receiver_get_bytes_received(_receiverHandle);
        }

        private void UpdateState(ReceiverState newState)
        {
            if (_lastState != newState)
            {
                var oldState = _lastState;
                _lastState = newState;
                
                Debug.WriteLine($"[ReceiveManager] State changed: {oldState} -> {newState}");
                StateChanged?.Invoke(this, new ReceiverStateChangedEventArgs(oldState, newState));
            }
        }

        private void StartPolling()
        {
            // Cancel any existing polling
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            Task.Run(async () =>
            {
                Debug.WriteLine("[ReceiveManager] Polling loop started");
                
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var state = State;
                        UpdateState(state);

                        if (state == ReceiverState.AwaitingAccept)
                        {
                            // Only fire event once per pending transfer
                            if (!_pendingTransferHandled)
                            {
                                var pending = GetPendingTransfer();
                                if (pending != null && 
                                    (_lastPendingTransfer == null || 
                                     _lastPendingTransfer.FileName != pending.FileName))
                                {
                                    _lastPendingTransfer = pending;
                                    Debug.WriteLine($"[ReceiveManager] Incoming transfer: {pending.FileName} ({pending.FormattedSize}) from {pending.SenderName}");
                                    TransferRequested?.Invoke(pending);
                                }
                            }
                        }
                        else if (state == ReceiverState.Receiving)
                        {
                            var progress = GetProgress();
                            ProgressChanged?.Invoke(progress);
                        }
                        else if (state == ReceiverState.Completed)
                        {
                            Debug.WriteLine("[ReceiveManager] Transfer completed, restarting listener...");
                            // Reset state for next transfer
                            _lastPendingTransfer = null;
                            _pendingTransferHandled = false;
                            // Restart listening
                            StartReceiving();
                            break; // Exit this polling loop, new one will be started
                        }
                        else if (state == ReceiverState.Idle || state == ReceiverState.Error)
                        {
                            // Reset pending transfer state
                            if (state == ReceiverState.Error)
                            {
                                Debug.WriteLine("[ReceiveManager] Error state detected");
                            }
                            _lastPendingTransfer = null;
                            _pendingTransferHandled = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ReceiveManager] Polling error: {ex.Message}");
                    }

                    await Task.Delay(200, token);
                }
                
                Debug.WriteLine("[ReceiveManager] Polling loop ended");
            }, token);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Debug.WriteLine("[ReceiveManager] Disposing...");
                
                StopReceiving();
                
                if (_receiverHandle != IntPtr.Zero)
                {
                    NativeBindings.voidwarp_destroy_receiver(_receiverHandle);
                    _receiverHandle = IntPtr.Zero;
                }
                _disposed = true;
                GC.SuppressFinalize(this);
                
                Debug.WriteLine("[ReceiveManager] Disposed");
            }
        }
    }
}
