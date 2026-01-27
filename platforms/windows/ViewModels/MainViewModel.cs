using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VoidWarp.Windows;
using VoidWarp.Windows.Core;

namespace VoidWarp.Windows.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly VoidWarpEngine _engine;
        private readonly TransferManager _transferManager;
        private readonly ReceiveManager _receiveManager;
        private PendingTransferInfo? _pendingTransfer;
        private bool _isReceiveModeEnabled;
        private string _deviceIdText = "设备 ID: -";
        private string _localNetworkText = "本机网络: -";
        private string _discoveryStatusText = "未开始发现";
        private string _discoveryDetailText = string.Empty;
        private string _discoveryButtonText = "开始发现设备";
        private Brush _discoveryStatusColor = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        private string _receiveStatusText = "等待接收文件...";
        private string _receivePortText = "监听端口: -";
        private bool _isIncomingTransferVisible;
        private bool _isDropZoneVisible = true;
        private string _transferStatusText = "等待传输...";
        private double _transferProgress;
        private DiscoveredPeer? _selectedPeer;
        private string _incomingFileName = "文件: -";
        private string _incomingFileSize = "大小: -";
        private string _incomingSender = "来自: -";

        public ObservableCollection<DiscoveredPeer> Peers { get; } = [];

        public ICommand ToggleDiscoveryCommand { get; }
        public ICommand ManualAddCommand { get; }
        public ICommand OpenDownloadsCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand AcceptTransferCommand { get; }
        public ICommand RejectTransferCommand { get; }
        public ICommand ShowNetworkDiagnosticsCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _dispatcher = Application.Current.Dispatcher;

            _engine = new VoidWarpEngine(Environment.MachineName);
            _transferManager = new TransferManager();
            _receiveManager = new ReceiveManager();

            _engine.PeersChanged += OnPeersChanged;
            _engine.DiscoveryStateChanged += OnDiscoveryStateChanged;
            _engine.DiscoveryDiagnosticsChanged += OnDiscoveryDiagnosticsChanged;

            _transferManager.ProgressChanged += OnTransferProgressChanged;
            _transferManager.TransferCompleted += OnTransferCompleted;

            _receiveManager.TransferRequested += OnIncomingTransfer;
            _receiveManager.ProgressChanged += OnReceiveProgressChanged;
            _receiveManager.TransferCompleted += OnReceiveCompleted;
            _receiveManager.StateChanged += OnReceiverStateChanged;

            ToggleDiscoveryCommand = new RelayCommand(_ => ToggleDiscovery());
            ManualAddCommand = new RelayCommand(_ => AddManualPeer());
            OpenDownloadsCommand = new RelayCommand(_ => OpenDownloads());
            TestConnectionCommand = new RelayCommand(param => TestConnection(param as DiscoveredPeer));
            AcceptTransferCommand = new RelayCommand(_ => AcceptPendingTransferAsync());
            RejectTransferCommand = new RelayCommand(_ => RejectPendingTransfer());
            ShowNetworkDiagnosticsCommand = new RelayCommand(_ => FirewallHelper.ShowNetworkTroubleshootingDialog());

            DeviceIdText = $"设备 ID: {_engine.DeviceId[..8]}...";
            UpdateNetworkDiagnostics();

            InitializeReceiving();
            StartDiscovery(_receiveManager.Port);
        }

        public string DeviceIdText
        {
            get => _deviceIdText;
            private set => SetProperty(ref _deviceIdText, value);
        }

        public string LocalNetworkText
        {
            get => _localNetworkText;
            private set => SetProperty(ref _localNetworkText, value);
        }

        public string DiscoveryStatusText
        {
            get => _discoveryStatusText;
            private set => SetProperty(ref _discoveryStatusText, value);
        }

        public string DiscoveryDetailText
        {
            get => _discoveryDetailText;
            private set => SetProperty(ref _discoveryDetailText, value);
        }

        public string DiscoveryButtonText
        {
            get => _discoveryButtonText;
            private set => SetProperty(ref _discoveryButtonText, value);
        }

        public Brush DiscoveryStatusColor
        {
            get => _discoveryStatusColor;
            private set => SetProperty(ref _discoveryStatusColor, value);
        }

        public bool IsReceiveModeEnabled
        {
            get => _isReceiveModeEnabled;
            set
            {
                if (SetProperty(ref _isReceiveModeEnabled, value))
                {
                    if (value)
                    {
                        _receiveManager.StartReceiving();
                        ReceiveStatusText = "等待接收文件...";
                        ReceivePortText = $"监听端口: {_receiveManager.Port}";
                        TransferStatusText = "接收模式已开启";
                    }
                    else
                    {
                        _receiveManager.StopReceiving();
                        IncomingTransferVisible = false;
                        DropZoneVisible = true;
                        TransferStatusText = "等待传输...";
                    }
                }
            }
        }

        public string ReceiveStatusText
        {
            get => _receiveStatusText;
            private set => SetProperty(ref _receiveStatusText, value);
        }

        public string ReceivePortText
        {
            get => _receivePortText;
            private set => SetProperty(ref _receivePortText, value);
        }

        public bool IncomingTransferVisible
        {
            get => _isIncomingTransferVisible;
            private set => SetProperty(ref _isIncomingTransferVisible, value);
        }

        public bool DropZoneVisible
        {
            get => _isDropZoneVisible;
            private set => SetProperty(ref _isDropZoneVisible, value);
        }

        public string TransferStatusText
        {
            get => _transferStatusText;
            private set => SetProperty(ref _transferStatusText, value);
        }

        public double TransferProgress
        {
            get => _transferProgress;
            private set => SetProperty(ref _transferProgress, value);
        }

        public DiscoveredPeer? SelectedPeer
        {
            get => _selectedPeer;
            set => SetProperty(ref _selectedPeer, value);
        }

        public string IncomingFileName
        {
            get => _incomingFileName;
            private set => SetProperty(ref _incomingFileName, value);
        }

        public string IncomingFileSize
        {
            get => _incomingFileSize;
            private set => SetProperty(ref _incomingFileSize, value);
        }

        public string IncomingSender
        {
            get => _incomingSender;
            private set => SetProperty(ref _incomingSender, value);
        }

        public async Task SendFilesAsync(string[] files)
        {
            if (files.Length == 0)
            {
                return;
            }

            if (SelectedPeer == null)
            {
                MessageBox.Show("请先选择目标设备", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_transferManager.IsTransferring)
            {
                MessageBox.Show("正在传输中，请等待完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var file in files)
            {
                TransferStatusText = $"正在发送: {System.IO.Path.GetFileName(file)}";
                TransferProgress = 0;

                try
                {
                    await _transferManager.SendFileAsync(file, SelectedPeer);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"传输错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                }
            }
        }

        public void Dispose()
        {
            _engine.PeersChanged -= OnPeersChanged;
            _engine.DiscoveryStateChanged -= OnDiscoveryStateChanged;
            _engine.DiscoveryDiagnosticsChanged -= OnDiscoveryDiagnosticsChanged;

            _receiveManager.TransferRequested -= OnIncomingTransfer;
            _receiveManager.ProgressChanged -= OnReceiveProgressChanged;
            _receiveManager.TransferCompleted -= OnReceiveCompleted;
            _receiveManager.StateChanged -= OnReceiverStateChanged;

            _transferManager.ProgressChanged -= OnTransferProgressChanged;
            _transferManager.TransferCompleted -= OnTransferCompleted;

            _transferManager.Dispose();
            _receiveManager.Dispose();
            _engine.Dispose();
        }

        private void InitializeReceiving()
        {
            _receiveManager.StartReceiving();
            _isReceiveModeEnabled = true;
            OnPropertyChanged(nameof(IsReceiveModeEnabled));
            ReceivePortText = $"监听端口: {_receiveManager.Port}";
            TransferStatusText = "接收模式已开启";
        }

        private void ToggleDiscovery()
        {
            if (_engine.IsDiscovering)
            {
                _engine.StopDiscovery();
            }
            else
            {
                StartDiscovery(_receiveManager.Port);
            }
        }

        private void StartDiscovery(ushort port)
        {
            if (!_engine.StartDiscovery(port))
            {
                var diag = FirewallHelper.DiagnoseNetworkIssues();
                var issuesSummary = diag.Issues.Count > 0
                    ? "\n\n问题: " + string.Join("; ", diag.Issues)
                    : "";

                var result = MessageBox.Show(
                    $"启动设备发现失败。{issuesSummary}\n\n是否查看网络诊断建议？",
                    "发现失败",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    FirewallHelper.ShowNetworkTroubleshootingDialog();
                }
            }
        }

        private void AddManualPeer()
        {
            if (!_engine.IsDiscovering)
            {
                StartDiscovery(_receiveManager.Port);
            }

            var dialog = new ManualPeerDialog();
            if (dialog.ShowDialog() == true)
            {
                var ipInput = dialog.IpAddress;
                var port = dialog.Port;
                var peerId = $"manual-{ipInput.Replace(".", "-")}";
                var peerName = $"手动添加 ({ipInput})";

                if (_engine.AddManualPeer(peerId, peerName, ipInput, port))
                {
                    MessageBox.Show($"已添加设备: {ipInput}:{port}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"添加设备失败: {ipInput}:{port}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void OpenDownloads()
        {
            var downloadPath = System.IO.Path.Combine(
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

        private void TestConnection(DiscoveredPeer? peer)
        {
            if (peer == null)
            {
                MessageBox.Show("请先选择目标设备", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = VoidWarpEngine.TestConnection(peer);
            if (result)
            {
                MessageBox.Show($"设备在线！\n{peer.DeviceName} ({peer.BestIp}:{peer.Port})", 
                    "连接测试", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var diagResult = MessageBox.Show(
                    $"无法连接到设备: {peer.DeviceName}\nIP: {peer.IpAddress}\n端口: {peer.Port}\n\n" +
                    "可能原因:\n• 设备不在同一局域网\n• 防火墙阻止了连接\n• 目标设备未开启接收模式\n\n是否查看网络诊断？",
                    "连接失败",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (diagResult == MessageBoxResult.Yes)
                {
                    FirewallHelper.ShowNetworkTroubleshootingDialog();
                }
            }
        }

        private async void AcceptPendingTransferAsync()
        {
            if (_pendingTransfer == null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = _pendingTransfer.FileName,
                Title = "保存文件"
            };

            if (dialog.ShowDialog() == true)
            {
                TransferStatusText = "正在接收...";
                await _receiveManager.AcceptTransferAsync(dialog.FileName);
            }
        }

        private void RejectPendingTransfer()
        {
            _receiveManager.RejectTransfer();
            IncomingTransferVisible = false;
            DropZoneVisible = true;
            ReceiveStatusText = "等待接收文件...";
            _pendingTransfer = null;
            IncomingFileName = "文件: -";
            IncomingFileSize = "大小: -";
            IncomingSender = "来自: -";
        }

        private void OnPeersChanged(object? sender, PeersChangedEventArgs e)
        {
            _dispatcher.BeginInvoke(() =>
            {
                Debug.WriteLine($"[MainViewModel] Peers changed, count: {e.Count}");
                Peers.Clear();
                foreach (var peer in e.Peers)
                {
                    Peers.Add(peer);
                }

                if (Peers.Count == 0)
                {
                    DiscoveryStatusText = _engine.IsDiscovering ? "正在发现设备..." : "未发现设备";
                }
                else
                {
                    DiscoveryStatusText = $"已发现 {Peers.Count} 个设备";
                }
            });
        }

        private void OnDiscoveryStateChanged(object? sender, DiscoveryStateChangedEventArgs e)
        {
            _dispatcher.BeginInvoke(() =>
            {
                Debug.WriteLine($"[MainViewModel] Discovery state changed: {e.State}");
                DiscoveryDetailText = e.Message ?? string.Empty;

                switch (e.State)
                {
                    case DiscoveryState.Discovering:
                        DiscoveryStatusColor = new SolidColorBrush(Color.FromRgb(0x6c, 0x63, 0xff));
                        DiscoveryStatusText = "正在发现设备...";
                        DiscoveryButtonText = "停止发现";
                        break;
                    case DiscoveryState.Idle:
                        DiscoveryStatusColor = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                        DiscoveryStatusText = "发现已停止";
                        DiscoveryButtonText = "开始发现设备";
                        break;
                    case DiscoveryState.Starting:
                        DiscoveryStatusColor = new SolidColorBrush(Color.FromRgb(0xff, 0x98, 0x00));
                        DiscoveryStatusText = "正在启动发现...";
                        DiscoveryButtonText = "正在启动...";
                        break;
                    case DiscoveryState.Error:
                        DiscoveryStatusColor = new SolidColorBrush(Color.FromRgb(0xff, 0x52, 0x52));
                        DiscoveryStatusText = $"发现错误: {e.Message ?? "未知错误"}";
                        DiscoveryButtonText = "重试发现";
                        // Optionally show diagnostic hint for errors
                        DiscoveryDetailText = "点击右下角\"诊断\"按钮获取帮助";
                        break;
                }
            });
        }

        private void OnDiscoveryDiagnosticsChanged(object? sender, DiscoveryDiagnosticsChangedEventArgs e)
        {
            _dispatcher.BeginInvoke(() =>
            {
                LocalNetworkText = $"本机网络: {e.InterfaceName} / {e.LocalIpAddress}";
                UpdateNetworkDiagnostics();
            });
        }

        private void OnReceiverStateChanged(object? sender, ReceiverStateChangedEventArgs e)
        {
            _dispatcher.BeginInvoke(() =>
            {
                switch (e.NewState)
                {
                    case ReceiverState.Listening:
                        ReceiveStatusText = "等待接收文件...";
                        break;
                    case ReceiverState.AwaitingAccept:
                        ReceiveStatusText = "收到传输请求！";
                        break;
                    case ReceiverState.Receiving:
                        ReceiveStatusText = "正在接收...";
                        break;
                    case ReceiverState.Completed:
                        ReceiveStatusText = "接收完成！";
                        break;
                    case ReceiverState.Error:
                        ReceiveStatusText = "接收错误";
                        break;
                }
            });
        }

        private void OnTransferProgressChanged(TransferProgressInfo progress)
        {
            _dispatcher.Invoke(() =>
            {
                TransferProgress = progress.Percentage;
                TransferStatusText = $"{progress.FormattedProgress} ({progress.FormattedSpeed})";
            });
        }

        private void OnTransferCompleted(bool success, string? error)
        {
            _dispatcher.Invoke(() =>
            {
                if (success)
                {
                    TransferStatusText = "传输完成！";
                    TransferProgress = 100;
                    MessageBox.Show("文件传输成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TransferStatusText = $"传输失败: {error}";
                    MessageBox.Show($"传输失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            });
        }

        private void OnIncomingTransfer(PendingTransferInfo transfer)
        {
            _dispatcher.Invoke(() =>
            {
                _pendingTransfer = transfer;
                IncomingTransferVisible = true;
                DropZoneVisible = false;
                ReceiveStatusText = "收到传输请求！";
                IncomingFileName = $"文件: {transfer.FileName}";
                IncomingFileSize = $"大小: {transfer.FormattedSize}";
                IncomingSender = $"来自: {transfer.SenderName} ({transfer.SenderAddress})";

                var result = MessageBox.Show(
                    $"来自: {transfer.SenderName} ({transfer.SenderAddress})\n文件: {transfer.FileName}\n大小: {transfer.FormattedSize}\n\n是否接收？",
                    "收到文件传输请求",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.No)
                {
                    RejectPendingTransfer();
                }
            });
        }

        private void OnReceiveProgressChanged(float progress)
        {
            _dispatcher.Invoke(() =>
            {
                TransferProgress = progress;
                TransferStatusText = $"正在接收... {progress:F1}%";
            });
        }

        private void OnReceiveCompleted(bool success, string? error)
        {
            _dispatcher.Invoke(() =>
            {
                IncomingTransferVisible = false;
                DropZoneVisible = true;
                IncomingFileName = "文件: -";
                IncomingFileSize = "大小: -";
                IncomingSender = "来自: -";

                if (success)
                {
                    TransferStatusText = "接收完成！";
                    TransferProgress = 100;
                    MessageBox.Show("文件接收成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TransferStatusText = $"接收失败: {error}";
                    MessageBox.Show($"接收失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                ReceiveStatusText = "等待接收文件...";
            });
        }

        private void UpdateNetworkDiagnostics()
        {
            var ipSummary = string.Join(", ", NetworkDiagnosticsHelper.GetLocalIpSummaries());
            LocalNetworkText = $"本机网络: {_engine.LocalInterfaceName} / {_engine.LocalIpAddress}";
            var detail = string.IsNullOrWhiteSpace(_engine.DiscoveryDiagnosticsDetail)
                ? "网络诊断"
                : _engine.DiscoveryDiagnosticsDetail;
            DiscoveryDetailText = $"{detail} | IP 列表: {ipSummary}";
            ReceivePortText = $"监听端口: {_receiveManager.Port}";
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static class NetworkDiagnosticsHelper
    {
        public static IReadOnlyList<string> GetLocalIpSummaries()
        {
            var results = new List<string>();
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        var ip = addr.Address.ToString();
                        results.Add($"{ni.Name}: {ip}");
                    }
                }
            }
            catch
            {
                return [];
            }

            return results;
        }
    }
}
