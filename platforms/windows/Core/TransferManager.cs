using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoidWarp.Windows.Native;

namespace VoidWarp.Windows.Core
{
    /// <summary>
    /// Manages file transfer operations using TCP sender
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
        /// Start sending a file to the target peer using TCP
        /// </summary>
        public async Task SendFileAsync(string filePath, PeerItem target, CancellationToken cancellationToken = default)
        {
            if (IsTransferring)
                throw new InvalidOperationException("A transfer is already in progress");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            // Use TCP Sender instead of chunk-based sender
            _senderHandle = NativeBindings.voidwarp_tcp_sender_create(filePath);
            if (_senderHandle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create TCP file sender");

            FileName = Path.GetFileName(filePath);
            FileSize = NativeBindings.voidwarp_tcp_sender_get_file_size(_senderHandle);
            IsTransferring = true;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // Start progress monitoring task
                var progressTask = MonitorProgressAsync(_cts.Token);

                var ipCandidates = (target.IpAddress ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ipCandidates.Length == 0 && !string.IsNullOrWhiteSpace(target.IpAddress))
                {
                    ipCandidates = new[] { target.IpAddress };
                }

                int result = 3;
                foreach (var ip in ipCandidates)
                {
                    result = await NativeAsync.StartTcpSenderAsync(
                        _senderHandle,
                        ip,
                        target.Port,
                        Environment.MachineName,
                        _cts.Token
                    );

                    if (result == 0)
                    {
                        break;
                    }

                    if (result == 3)
                    {
                        continue;
                    }

                    break;
                }

                // Stop progress monitoring
                _cts.Cancel();
                try { await progressTask; } catch (OperationCanceledException) { }

                // Handle result
                switch (result)
                {
                    case 0: // Success
                        ProgressChanged?.Invoke(new TransferProgressInfo
                        {
                            BytesTransferred = FileSize,
                            TotalBytes = FileSize,
                            Percentage = 100,
                            SpeedMbps = 0,
                            State = TransferState.Completed
                        });
                        TransferCompleted?.Invoke(true, null);
                        break;
                    case 1: // Rejected
                        TransferCompleted?.Invoke(false, "对方拒绝了传输");
                        break;
                    case 2: // ChecksumMismatch
                        TransferCompleted?.Invoke(false, "校验和不匹配");
                        break;
                    case 3: // ConnectionFailed
                        if (ipCandidates.Length > 0)
                        {
                            TransferCompleted?.Invoke(false, $"连接失败：无法连接到设备（已尝试 {ipCandidates.Length} 个IP）");
                        }
                        else
                        {
                            TransferCompleted?.Invoke(false, "连接失败");
                        }
                        break;
                    case 4: // Timeout
                        TransferCompleted?.Invoke(false, "传输超时");
                        break;
                    case 5: // Cancelled
                        TransferCompleted?.Invoke(false, "已取消");
                        break;
                    default:
                        TransferCompleted?.Invoke(false, $"未知错误: {result}");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                NativeBindings.voidwarp_tcp_sender_cancel(_senderHandle);
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

        private async Task MonitorProgressAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && IsTransferring)
                {
                    float progress = NativeBindings.voidwarp_tcp_sender_get_progress(_senderHandle);
                    ProgressChanged?.Invoke(new TransferProgressInfo
                    {
                        BytesTransferred = (ulong)(progress / 100.0 * FileSize),
                        TotalBytes = FileSize,
                        Percentage = progress,
                        SpeedMbps = 0, // TCP sender calculates internally
                        State = TransferState.Transferring
                    });

                    if (progress >= 100f) break;
                    await Task.Delay(200, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when transfer completes
            }
        }

        public void CancelTransfer()
        {
            if (_senderHandle != IntPtr.Zero)
            {
                NativeBindings.voidwarp_tcp_sender_cancel(_senderHandle);
            }
            _cts?.Cancel();
        }

        private void CleanupSender()
        {
            if (_senderHandle != IntPtr.Zero)
            {
                NativeBindings.voidwarp_tcp_sender_destroy(_senderHandle);
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
                GC.SuppressFinalize(this);
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
