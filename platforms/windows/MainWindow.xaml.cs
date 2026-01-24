using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VoidWarp.Windows.Core;

namespace VoidWarp.Windows
{
    public partial class MainWindow : Window
    {
        private VoidWarpEngine? _engine;
        private TransferManager? _transferManager;
        private ReceiveManager? _receiveManager;
        private DispatcherTimer? _refreshTimer;
        private readonly ObservableCollection<DiscoveredPeer> _peers = [];
        private PendingTransferInfo? _pendingTransfer;

        public MainWindow()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _peers;
            
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            try
            {
                string deviceName = Environment.MachineName;
                _engine = new VoidWarpEngine(deviceName);
                _transferManager = new TransferManager();
                _receiveManager = new ReceiveManager();
                
                _transferManager.ProgressChanged += OnProgressChanged;
                _transferManager.TransferCompleted += OnTransferCompleted;
                
                _receiveManager.TransferRequested += OnIncomingTransfer;
                _receiveManager.ProgressChanged += OnReceiveProgressChanged;
                _receiveManager.TransferCompleted += OnReceiveCompleted;
                
                DeviceIdText.Text = $"设备 ID: {_engine.DeviceId[..8]}...";
                
                // Show Network Diagnostics
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var ips = host.AddressList
                    .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4
                    .Select(ip => ip.ToString());
                
                NetworkDebugText.Text = $"Diagnostics: My IPs: {string.Join(", ", ips)} | Port: {_receiveManager.Port}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}\n\n请确保 voidwarp_core.dll 在程序目录中。", 
                    "VoidWarp 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnProgressChanged(TransferProgressInfo progress)
        {
            Dispatcher.Invoke(() =>
            {
                TransferProgress.Value = progress.Percentage;
                TransferStatus.Text = $"{progress.FormattedProgress} ({progress.FormattedSpeed})";
            });
        }

        private void OnTransferCompleted(bool success, string? error)
        {
            Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    TransferStatus.Text = "传输完成！";
                    TransferProgress.Value = 100;
                    MessageBox.Show("文件传输成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TransferStatus.Text = $"传输失败: {error}";
                    MessageBox.Show($"传输失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            });
        }

        #region Receive Mode

        private void ReceiveModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_receiveManager == null) return;

            _receiveManager.StartReceiving();
            
            ReceiveStatusPanel.Visibility = Visibility.Visible;
            ReceiveStatusText.Text = "等待接收文件...";
            ReceivePortText.Text = $"监听端口: {_receiveManager.Port}";
            
            TransferStatus.Text = "接收模式已开启";
        }

        private void ReceiveModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_receiveManager == null) return;

            _receiveManager.StopReceiving();
            
            ReceiveStatusPanel.Visibility = Visibility.Collapsed;
            IncomingTransferPanel.Visibility = Visibility.Collapsed;
            DropZone.Visibility = Visibility.Visible;
            
            TransferStatus.Text = "等待传输...";
        }

        private void OnIncomingTransfer(PendingTransferInfo transfer)
        {
            Dispatcher.Invoke(() =>
            {
                _pendingTransfer = transfer;
                
                IncomingFileName.Text = $"文件: {transfer.FileName}";
                IncomingFileSize.Text = $"大小: {transfer.FormattedSize}";
                IncomingSender.Text = $"来自: {transfer.SenderName} ({transfer.SenderAddress})";
                
                IncomingTransferPanel.Visibility = Visibility.Visible;
                DropZone.Visibility = Visibility.Collapsed;
                
                ReceiveStatusText.Text = "收到传输请求！";

                // Also provide a modal prompt as requested (Offer -> user confirm).
                var result = MessageBox.Show(
                    $"来自: {transfer.SenderName} ({transfer.SenderAddress})\n文件: {transfer.FileName}\n大小: {transfer.FormattedSize}\n\n是否接收？",
                    "收到文件传输请求",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.No)
                {
                    _receiveManager?.RejectTransfer();
                    IncomingTransferPanel.Visibility = Visibility.Collapsed;
                    DropZone.Visibility = Visibility.Visible;
                    ReceiveStatusText.Text = "已拒绝，等待接收文件...";
                    _pendingTransfer = null;
                }
            });
        }

        private void OnReceiveProgressChanged(float progress)
        {
            Dispatcher.Invoke(() =>
            {
                TransferProgress.Value = progress;
                TransferStatus.Text = $"正在接收... {progress:F1}%";
            });
        }

        private void OnReceiveCompleted(bool success, string? error)
        {
            Dispatcher.Invoke(() =>
            {
                IncomingTransferPanel.Visibility = Visibility.Collapsed;
                
                if (success)
                {
                    TransferStatus.Text = "接收完成！";
                    TransferProgress.Value = 100;
                    MessageBox.Show("文件接收成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TransferStatus.Text = $"接收失败: {error}";
                    MessageBox.Show($"接收失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                ReceiveStatusText.Text = "等待接收文件...";
            });
        }

        private async void AcceptBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_receiveManager == null || _pendingTransfer == null) return;

            var dialog = new SaveFileDialog
            {
                FileName = _pendingTransfer.FileName,
                Title = "保存文件"
            };

            if (dialog.ShowDialog() == true)
            {
                AcceptBtn.IsEnabled = false;
                RejectBtn.IsEnabled = false;
                
                TransferStatus.Text = "正在接收...";
                await _receiveManager.AcceptTransferAsync(dialog.FileName);
                
                AcceptBtn.IsEnabled = true;
                RejectBtn.IsEnabled = true;
            }
        }

        private void RejectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_receiveManager == null) return;

            _receiveManager.RejectTransfer();
            
            IncomingTransferPanel.Visibility = Visibility.Collapsed;
            ReceiveStatusText.Text = "等待接收文件...";
            _pendingTransfer = null;
        }

        #endregion

        private void DiscoverBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            if (_engine.IsDiscovering)
            {
                StopDiscovery();
            }
            else
            {
                StartDiscovery();
            }
        }

        private void StartDiscovery()
        {
            if (_engine == null || _receiveManager == null) return;

            // Use the receiver's actual port for discovery registration
            // This ensures peers connect to the correct port for file transfers
            if (_engine.StartDiscovery(_receiveManager.Port))
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x6c, 0x63, 0xff));
                StatusText.Text = "正在发现设备...";
                DiscoverBtn.Content = "停止发现";

                _refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _refreshTimer.Tick += RefreshPeers;
                _refreshTimer.Start();
            }
            else
            {
                MessageBox.Show("启动设备发现失败", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StopDiscovery()
        {
            _engine?.StopDiscovery();
            _refreshTimer?.Stop();
            _refreshTimer = null;
            
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            StatusText.Text = "发现已停止";
            DiscoverBtn.Content = "开始发现设备";
        }

        private void ManualAddBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            string ipInput = ManualIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ipInput))
            {
                MessageBox.Show("请输入有效的 IP 地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Default port
            ushort port = 42424;

            // Add the manual peer
            string peerId = $"manual-{ipInput.Replace(".", "-")}";
            string peerName = $"手动添加 ({ipInput})";
            
            _engine.AddManualPeer(peerId, peerName, ipInput, port);
            
            // Refresh the peer list
            RefreshPeers(null, EventArgs.Empty);
            
            MessageBox.Show($"已添加设备: {ipInput}:{port}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenDownloadsBtn_Click(object sender, RoutedEventArgs e)
        {
            string downloadPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                "Downloads", 
                "VoidWarp"
            );
            
            if (!System.IO.Directory.Exists(downloadPath))
            {
                System.IO.Directory.CreateDirectory(downloadPath);
            }
            
            System.Diagnostics.Process.Start("explorer.exe", downloadPath);
        }

        private void RefreshPeers(object? sender, EventArgs e)
        {
            if (_engine == null) return;

            var peers = _engine.GetPeers();
            _peers.Clear();
            foreach (var peer in peers)
            {
                _peers.Add(peer);
            }

            StatusText.Text = $"已发现 {_peers.Count} 个设备";
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (_transferManager == null) return;
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                
                if (DeviceList.SelectedItem == null)
                {
                    MessageBox.Show("请先选择目标设备", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_transferManager.IsTransferring)
                {
                    MessageBox.Show("正在传输中，请等待完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var target = (DiscoveredPeer)DeviceList.SelectedItem;
                
                foreach (var file in files)
                {
                    TransferStatus.Text = $"正在发送: {System.IO.Path.GetFileName(file)}";
                    TransferProgress.Value = 0;
                    
                    try
                    {
                        await _transferManager.SendFileAsync(file, target);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"传输错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        break;
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            _transferManager?.Dispose();
            _receiveManager?.Dispose();
            _engine?.Dispose();
            base.OnClosed(e);
        }
    }
}
