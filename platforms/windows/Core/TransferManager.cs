using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoidWarp.Windows.Native;

namespace VoidWarp.Windows.Core
{
    /// <summary>
    /// Manages file transfer operations using the native core
    /// </summary>
    public class TransferManager : IDisposable
    {
        private IntPtr _senderHandle = IntPtr.Zero;
        private CancellationTokenSource? _cts;
        private bool _disposed = false;

        public string? FileName { get; private set; }
        public ulong FileSize { get; private set; }
        public bool IsTransferring { get; private set; }

        public event Action<TransferProgressInfo>? ProgressChanged;
        public event Action<bool, string?>? TransferCompleted;

        /// <summary>
        /// Start sending a file to the target peer
        /// </summary>
        public async Task SendFileAsync(string filePath, DiscoveredPeer target, CancellationToken cancellationToken = default)
        {
            if (IsTransferring)
                throw new InvalidOperationException("A transfer is already in progress");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            _senderHandle = NativeBindings.voidwarp_create_sender(filePath);
            if (_senderHandle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create file sender");

            FileName = NativeBindings.GetStringAndFree(NativeBindings.voidwarp_sender_get_name(_senderHandle));
            FileSize = NativeBindings.voidwarp_sender_get_size(_senderHandle);
            IsTransferring = true;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Task.Run(() => TransferLoop(target), _cts.Token);
                TransferCompleted?.Invoke(true, null);
            }
            catch (OperationCanceledException)
            {
                NativeBindings.voidwarp_sender_cancel(_senderHandle);
                TransferCompleted?.Invoke(false, "Transfer cancelled");
            }
            catch (Exception ex)
            {
                TransferCompleted?.Invoke(false, ex.Message);
            }
            finally
            {
                CleanupSender();
            }
        }

        private void TransferLoop(DiscoveredPeer target)
        {
            DateTime startTime = DateTime.Now;
            ulong lastBytes = 0;
            DateTime lastSpeedCheck = DateTime.Now;

            while (!_cts!.Token.IsCancellationRequested)
            {
                var chunk = NativeBindings.voidwarp_sender_read_chunk(_senderHandle);
                
                if (chunk.Data == IntPtr.Zero || chunk.Len == 0)
                {
                    // No more data
                    break;
                }

                // In a real implementation, we would send this chunk over the network
                // For now, we just simulate the transfer with a small delay
                Thread.Sleep(1); // Simulate network latency

                NativeBindings.voidwarp_free_chunk(chunk);

                // Report progress
                var progress = NativeBindings.voidwarp_sender_get_progress(_senderHandle);
                
                // Calculate speed
                double elapsed = (DateTime.Now - lastSpeedCheck).TotalSeconds;
                if (elapsed >= 0.5)
                {
                    ulong bytesDelta = progress.BytesTransferred - lastBytes;
                    float speedMbps = (float)(bytesDelta / elapsed / 1024 / 1024);
                    lastBytes = progress.BytesTransferred;
                    lastSpeedCheck = DateTime.Now;

                    ProgressChanged?.Invoke(new TransferProgressInfo
                    {
                        BytesTransferred = progress.BytesTransferred,
                        TotalBytes = progress.TotalBytes,
                        Percentage = progress.Percentage,
                        SpeedMbps = speedMbps,
                        State = (TransferState)progress.State
                    });
                }

                if (chunk.IsLast)
                    break;
            }
        }

        public void CancelTransfer()
        {
            _cts?.Cancel();
        }

        private void CleanupSender()
        {
            if (_senderHandle != IntPtr.Zero)
            {
                NativeBindings.voidwarp_destroy_sender(_senderHandle);
                _senderHandle = IntPtr.Zero;
            }
            IsTransferring = false;
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CleanupSender();
                _disposed = true;
            }
        }
    }

    public class TransferProgressInfo
    {
        public ulong BytesTransferred { get; set; }
        public ulong TotalBytes { get; set; }
        public float Percentage { get; set; }
        public float SpeedMbps { get; set; }
        public TransferState State { get; set; }

        public string FormattedProgress => $"{BytesTransferred / 1024 / 1024:F1} MB / {TotalBytes / 1024 / 1024:F1} MB";
        public string FormattedSpeed => $"{SpeedMbps:F1} MB/s";
    }

    public enum TransferState
    {
        Pending = 0,
        Transferring = 1,
        Paused = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }
}
