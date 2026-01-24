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

        public string FormattedSize => FileSize > 1024 * 1024
            ? $"{FileSize / 1024.0 / 1024.0:F1} MB"
            : $"{FileSize / 1024.0:F1} KB";
    }

    /// <summary>
    /// Manages file receiving operations using the native core
    /// </summary>
    public class ReceiveManager : IDisposable
    {
        private IntPtr _receiverHandle = IntPtr.Zero;
        private CancellationTokenSource? _pollCts;
        private bool _disposed = false;

        public ushort Port { get; private set; }
        public bool IsReceiving => State == ReceiverState.Listening || State == ReceiverState.Receiving;

        public ReceiverState State
        {
            get
            {
                if (_receiverHandle == IntPtr.Zero) return ReceiverState.Idle;
                return (ReceiverState)NativeBindings.voidwarp_receiver_get_state(_receiverHandle);
            }
        }

        public event Action<PendingTransferInfo>? TransferRequested;
        public event Action<float>? ProgressChanged;
        public event Action<bool, string?>? TransferCompleted;

        public ReceiveManager()
        {
            _receiverHandle = NativeBindings.voidwarp_create_receiver();
            if (_receiverHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create file receiver");
            }

            Port = NativeBindings.voidwarp_receiver_get_port(_receiverHandle);
        }

        /// <summary>
        /// Start listening for incoming transfers
        /// </summary>
        public void StartReceiving()
        {
            if (_receiverHandle == IntPtr.Zero) return;

            NativeBindings.voidwarp_receiver_start(_receiverHandle);
            NativeBindings.voidwarp_transport_start_server(Port);
            StartPolling();
        }

        /// <summary>
        /// Stop listening
        /// </summary>
        public void StopReceiving()
        {
            _pollCts?.Cancel();
            _pollCts = null;

            if (_receiverHandle != IntPtr.Zero)
            {
                NativeBindings.voidwarp_receiver_stop(_receiverHandle);
            }
        }

        /// <summary>
        /// Accept the pending transfer
        /// </summary>
        public async Task<bool> AcceptTransferAsync(string savePath)
        {
            if (_receiverHandle == IntPtr.Zero) return false;

            return await Task.Run(() =>
            {
                int result = NativeBindings.voidwarp_receiver_accept(_receiverHandle, savePath);
                if (result == 0)
                {
                    TransferCompleted?.Invoke(true, null);
                    return true;
                }
                else
                {
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
            if (_receiverHandle == IntPtr.Zero) return;
            var result = NativeBindings.voidwarp_receiver_reject(_receiverHandle);
            if (result != 0)
            {
                TransferCompleted?.Invoke(false, "Reject failed");
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

        private void StartPolling()
        {
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var state = State;

                    if (state == ReceiverState.AwaitingAccept)
                    {
                        var pending = GetPendingTransfer();
                        if (pending != null)
                        {
                            TransferRequested?.Invoke(pending);
                        }
                    }
                    else if (state == ReceiverState.Receiving)
                    {
                        var progress = GetProgress();
                        ProgressChanged?.Invoke(progress);
                    }
                    else if (state == ReceiverState.Completed)
                    {
                        // Reset to listening
                        StartReceiving();
                    }

                    await Task.Delay(200, token);
                }
            }, token);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopReceiving();
                if (_receiverHandle != IntPtr.Zero)
                {
                    NativeBindings.voidwarp_destroy_receiver(_receiverHandle);
                    _receiverHandle = IntPtr.Zero;
                }
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
