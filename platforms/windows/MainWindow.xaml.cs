using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VoidWarp.Windows.Core;

namespace VoidWarp.Windows
{
    public partial class MainWindow : Window
    {
        private VoidWarpEngine? _engine;
        private TransferManager? _transferManager;
        private DispatcherTimer? _refreshTimer;
        private ObservableCollection<DiscoveredPeer> _peers = new();

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
                
                _transferManager.ProgressChanged += OnProgressChanged;
                _transferManager.TransferCompleted += OnTransferCompleted;
                
                DeviceIdText.Text = $"设备 ID: {_engine.DeviceId[..8]}...";
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
            if (_engine == null) return;

            if (_engine.StartDiscovery(42424))
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
            _engine?.Dispose();
            base.OnClosed(e);
        }
    }
}
