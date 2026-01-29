using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VoidWarp.Windows.Core;

namespace VoidWarp.Windows.ViewModels
{
    /// <summary>
    /// Main ViewModel for VoidWarp Windows client.
    /// Manages application state with ObservableCollections and proper UI thread dispatching.
    /// All UI updates are wrapped in Dispatcher.Invoke to avoid cross-thread exceptions.
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        private readonly VoidWarpEngine _engine;
        private bool _isReceiving;
        private bool _isDiscovering;
        private bool _isSending;
        private double _progressValue;
        private string _statusMessage = "Ready";
        private string _selectedFilePath = string.Empty;
        private PeerItem? _selectedPeer;
        private bool _disposed;
        private bool _hasPendingTransfer;
        private PendingTransferEventArgs? _pendingTransferInfo;

        #endregion

        #region Collections

        /// <summary>
        /// Discovered peers on the network.
        /// Thread-safe: all updates go through Dispatcher.
        /// </summary>
        public ObservableCollection<PeerItem> Peers { get; } = new();

        /// <summary>
        /// Application log messages.
        /// Thread-safe: all updates go through Dispatcher.
        /// </summary>
        public ObservableCollection<string> Logs { get; } = new();

        #endregion

        #region Properties

        /// <summary>
        /// Short device ID for display (first 8 characters, uppercase).
        /// </summary>
        public string DeviceId => _engine.ShortDeviceId;

        /// <summary>
        /// Machine name of this device.
        /// </summary>
        public string DeviceName => _engine.DeviceName;

        /// <summary>
        /// Port the receiver is listening on.
        /// </summary>
        public ushort ReceiverPort => _engine.ReceiverPort;

        /// <summary>
        /// True if the receiver is active.
        /// </summary>
        public bool IsReceiving
        {
            get => _isReceiving;
            set
            {
                if (SetProperty(ref _isReceiving, value))
                {
                    OnPropertyChanged(nameof(ReceiverStatusText));
                    OnPropertyChanged(nameof(ReceiverPort));
                    OnPropertyChanged(nameof(ReceiveStatusTitle));
                    OnPropertyChanged(nameof(ReceiveStatusSubtitle));
                }
            }
        }

        /// <summary>
        /// Display text for receiver status (matches Android).
        /// </summary>
        public string ReceiverStatusText => IsReceiving 
            ? $"Listening on port {ReceiverPort}" 
            : "Receiver disabled";

        /// <summary>
        /// Receive card title (matches Android: 接收准备就绪 / 接收模式已关闭).
        /// </summary>
        public string ReceiveStatusTitle => IsReceiving ? "接收准备就绪" : "接收模式已关闭";

        /// <summary>
        /// Receive card subtitle (matches Android: 端口 X 可见 / 点击开关启用).
        /// </summary>
        public string ReceiveStatusSubtitle => IsReceiving 
            ? $"端口 {ReceiverPort} 可见" 
            : "点击开关启用";

        /// <summary>
        /// True when there are no discovered peers (for empty state visibility).
        /// </summary>
        public bool HasNoPeers => Peers.Count == 0;

        /// <summary>
        /// True if discovery is running.
        /// </summary>
        public bool IsDiscovering
        {
            get => _isDiscovering;
            set
            {
                if (SetProperty(ref _isDiscovering, value))
                {
                    OnPropertyChanged(nameof(ScanButtonText));
                    OnPropertyChanged(nameof(DiscoveryStatusText));
                    OnPropertyChanged(nameof(DiscoveryStatusLabel));
                }
            }
        }

        /// <summary>
        /// Discovery status for diagnostic card (matches Android: 进行中 / 空闲).
        /// </summary>
        public string DiscoveryStatusLabel => IsDiscovering ? "进行中" : "空闲";

        /// <summary>
        /// Text for the scan button (matches Android).
        /// </summary>
        public string ScanButtonText => IsDiscovering ? "停止" : "扫描";

        /// <summary>
        /// Discovery status text for display.
        /// </summary>
        public string DiscoveryStatusText => IsDiscovering ? "Scanning..." : "Idle";

        /// <summary>
        /// True if currently sending a file.
        /// </summary>
        public bool IsSending
        {
            get => _isSending;
            set
            {
                if (SetProperty(ref _isSending, value))
                {
                    OnPropertyChanged(nameof(CanSend));
                    OnPropertyChanged(nameof(IsTransferring));
                }
            }
        }

        /// <summary>
        /// True if any transfer (send or receive) is in progress.
        /// </summary>
        public bool IsTransferring => IsSending || _hasPendingTransfer;

        /// <summary>
        /// Transfer progress (0-100).
        /// </summary>
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, Math.Min(100, Math.Max(0, value)));
        }

        /// <summary>
        /// Current status message for display.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value ?? "");
        }

        /// <summary>
        /// Path to the selected file for sending.
        /// </summary>
        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set
            {
                if (SetProperty(ref _selectedFilePath, value ?? ""))
                {
                    OnPropertyChanged(nameof(HasFileSelected));
                    OnPropertyChanged(nameof(CanSend));
                    OnPropertyChanged(nameof(SelectedFileName));
                    OnPropertyChanged(nameof(SelectedFileInfo));
                }
            }
        }

        /// <summary>
        /// Just the filename of the selected file (matches Android placeholder).
        /// </summary>
        public string SelectedFileName => string.IsNullOrEmpty(SelectedFilePath)
            ? "选择要发送的文件"
            : Path.GetFileName(SelectedFilePath);

        /// <summary>
        /// File info with size for display.
        /// </summary>
        public string SelectedFileInfo
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
                    return "选择要发送的文件";
                
                try
                {
                    var info = new FileInfo(SelectedFilePath);
                    return $"{info.Name} ({FormatSize(info.Length)})";
                }
                catch
                {
                    return Path.GetFileName(SelectedFilePath);
                }
            }
        }

        /// <summary>
        /// True if a valid file is selected.
        /// </summary>
        public bool HasFileSelected => !string.IsNullOrEmpty(SelectedFilePath) && File.Exists(SelectedFilePath);

        /// <summary>
        /// Currently selected peer for sending.
        /// </summary>
        public PeerItem? SelectedPeer
        {
            get => _selectedPeer;
            set
            {
                if (SetProperty(ref _selectedPeer, value))
                {
                    OnPropertyChanged(nameof(CanSend));
                    OnPropertyChanged(nameof(HasPeerSelected));
                    
                    if (value != null)
                    {
                        AddLog($"Selected: {value.DeviceName} ({value.DisplayName})");
                    }
                }
            }
        }

        /// <summary>
        /// True if a peer is selected.
        /// </summary>
        public bool HasPeerSelected => SelectedPeer != null;

        /// <summary>
        /// True if send operation can proceed.
        /// </summary>
        public bool CanSend => HasFileSelected && HasPeerSelected && !IsSending;

        /// <summary>
        /// Local IP addresses info for display (matches Android: 本机IP).
        /// </summary>
        public string LocalIpInfo
        {
            get
            {
                var ips = VoidWarpEngine.GetAllLocalIpAddresses();
                return ips.Count > 0 ? string.Join(" | ", ips) : "无";
            }
        }

        /// <summary>
        /// True if native library loaded successfully.
        /// </summary>
        public bool IsNativeLoaded => _engine.NativeLoaded || _engine.IsInitialized;

        /// <summary>
        /// Warning message if native library failed to load.
        /// </summary>
        public string? NativeLoadWarning => !IsNativeLoaded ? _engine.NativeLoadError : null;

        #endregion

        #region Commands

        public ICommand ScanCommand { get; }
        public ICommand SendFileCommand { get; }
        public ICommand ToggleReceiverCommand { get; }
        public ICommand SelectFileCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand OpenDownloadsCommand { get; }
        public ICommand AddManualPeerCommand { get; }
        public ICommand CancelTransferCommand { get; }

        #endregion

        #region Events

        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Constructor

        public MainViewModel()
        {
            _engine = VoidWarpEngine.Instance;

            // Subscribe to engine events
            _engine.OnLog += Engine_OnLog;
            _engine.OnPeerDiscovered += Engine_OnPeerDiscovered;
            _engine.OnProgress += Engine_OnProgress;
            _engine.OnTransferComplete += Engine_OnTransferComplete;
            _engine.OnPendingTransfer += Engine_OnPendingTransfer;

            // Initialize commands
            ScanCommand = new RelayCommand(_ => ToggleScan());
            SendFileCommand = new RelayCommand(async _ => await SendFileAsync(), _ => CanSend);
            ToggleReceiverCommand = new RelayCommand(_ => ToggleReceiver());
            SelectFileCommand = new RelayCommand(_ => SelectFile());
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            TestConnectionCommand = new RelayCommand(peer => TestConnection(peer as PeerItem));
            OpenDownloadsCommand = new RelayCommand(_ => OpenDownloadsFolder());
            AddManualPeerCommand = new RelayCommand(_ => ShowAddPeerDialog());
            CancelTransferCommand = new RelayCommand(_ => CancelTransfer());

            // Initial log
            AddLog("VoidWarp Windows Client started");
            AddLog($"Device: {DeviceName} | ID: {DeviceId}");

            // Check native library status
            if (!_engine.IsInitialized)
            {
                AddLog($"WARNING: Engine not initialized - {_engine.NativeLoadError ?? "unknown error"}");
                AddLog("请从项目根目录运行 build_windows.bat 以先构建 Rust 核心，或将 voidwarp_core.dll 复制到本程序所在目录。");
                StatusMessage = "voidwarp_core.dll 未找到，请先构建或复制 DLL";
            }
            else
            {
                AddLog("Native library loaded successfully");
                // Auto-start receiver and discovery
                _ = InitializeAsync();
            }
        }

        #endregion

        #region Initialization

        private async Task InitializeAsync()
        {
            try
            {
                AddLog("Auto-starting receiver...");
                
                // Start receiver first to get the port
                _engine.StartReceiver();
                
                // Wait for port assignment
                await Task.Delay(500);
                
                InvokeOnUI(() =>
                {
                    IsReceiving = true;
                    OnPropertyChanged(nameof(ReceiverPort));
                    AddLog($"Receiver active on port {ReceiverPort}");
                });

                // Start discovery with receiver port
                var port = _engine.ReceiverPort > 0 ? _engine.ReceiverPort : (ushort)42424;
                _engine.StartDiscovery(port);
                
                InvokeOnUI(() =>
                {
                    IsDiscovering = true;
                    StatusMessage = "Scanning for devices...";
                });

                AddLog("Auto-discovery started");
            }
            catch (Exception ex)
            {
                AddLog($"Initialization error: {ex.Message}");
                StatusMessage = "Init failed";
            }
        }

        #endregion

        #region Command Handlers

        private void ToggleScan()
        {
            if (IsDiscovering)
            {
                _engine.StopDiscovery();
                InvokeOnUI(() =>
                {
                    IsDiscovering = false;
                    StatusMessage = "Scan stopped";
                });
            }
            else
            {
                var port = _engine.ReceiverPort > 0 ? _engine.ReceiverPort : (ushort)42424;
                _engine.StartDiscovery(port);
                InvokeOnUI(() =>
                {
                    IsDiscovering = true;
                    StatusMessage = "Scanning...";
                });
            }
        }

        private async Task SendFileAsync()
        {
            if (SelectedPeer == null || !HasFileSelected)
            {
                AddLog("Please select a file and target device");
                return;
            }

            InvokeOnUI(() =>
            {
                IsSending = true;
                ProgressValue = 0;
                StatusMessage = $"Sending to {SelectedPeer.DeviceName}...";
            });

            try
            {
                await _engine.SendFileAsync(SelectedFilePath, SelectedPeer);
            }
            catch (Exception ex)
            {
                AddLog($"Send error: {ex.Message}");
                InvokeOnUI(() => StatusMessage = "Send failed");
            }
            finally
            {
                InvokeOnUI(() => IsSending = false);
            }
        }

        private void ToggleReceiver()
        {
            if (IsReceiving)
            {
                _engine.StopReceiver();
                InvokeOnUI(() =>
                {
                    IsReceiving = false;
                    StatusMessage = "Receiver stopped";
                });
            }
            else
            {
                _engine.StartReceiver();
                InvokeOnUI(() =>
                {
                    IsReceiving = true;
                    StatusMessage = "Receiver active";
                    OnPropertyChanged(nameof(ReceiverPort));
                });
            }
        }

        private void SelectFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select file to send",
                Filter = "All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedFilePath = dialog.FileName;
                try
                {
                    var fileInfo = new FileInfo(dialog.FileName);
                    AddLog($"Selected: {fileInfo.Name} ({FormatSize(fileInfo.Length)})");
                }
                catch
                {
                    AddLog($"Selected: {Path.GetFileName(dialog.FileName)}");
                }
            }
        }

        private void ClearLogs()
        {
            InvokeOnUI(() => Logs.Clear());
        }

        private void TestConnection(PeerItem? peer)
        {
            if (peer == null) return;

            AddLog($"Testing connection to {peer.DeviceName}...");
            StatusMessage = "Testing connection...";

            Task.Run(() =>
            {
                bool result = _engine.TestConnection(peer);
                InvokeOnUI(() =>
                {
                    if (result)
                    {
                        AddLog($"✓ {peer.DeviceName} is online and reachable");
                        StatusMessage = "Connection OK";
                        MessageBox.Show(
                            $"Device is online!\n\n{peer.DeviceName}\n{peer.DisplayName}",
                            "Connection Test",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        AddLog($"✗ Cannot reach {peer.DeviceName}");
                        StatusMessage = "Connection failed";
                        MessageBox.Show(
                            $"Cannot connect to {peer.DeviceName}\n\nMake sure:\n• Device is on the same network\n• Receiver is enabled on the device\n• Firewall allows connections",
                            "Connection Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                });
            });
        }

        private void OpenDownloadsFolder()
        {
            var path = GetDownloadsPath();
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            
            try
            {
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                AddLog($"Failed to open folder: {ex.Message}");
            }
        }

        private void ShowAddPeerDialog()
        {
            // Create a simple input dialog for manual peer addition
            var dialog = new ManualPeerDialog();
            dialog.Owner = Application.Current.MainWindow;
            
            if (dialog.ShowDialog() == true)
            {
                var ip = dialog.IpAddress;
                var port = dialog.Port;
                
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    var id = $"manual-{ip.Replace(".", "-")}";
                    var name = $"Manual ({ip})";
                    
                    if (_engine.AddManualPeer(id, name, ip, port))
                    {
                        AddLog($"Added manual peer: {ip}:{port}");
                    }
                    else
                    {
                        AddLog($"Failed to add peer: {ip}:{port}");
                    }
                }
            }
        }

        private void CancelTransfer()
        {
            _engine.CancelSend();
            InvokeOnUI(() =>
            {
                IsSending = false;
                ProgressValue = 0;
                StatusMessage = "Transfer cancelled";
            });
        }

        #endregion

        #region Engine Event Handlers

        private void Engine_OnLog(object? sender, LogEventArgs e)
        {
            AddLog($"[{e.Level}] {e.Message}");
        }

        private void Engine_OnPeerDiscovered(object? sender, PeerDiscoveredEventArgs e)
        {
            InvokeOnUI(() =>
            {
                // Preserve selection if possible
                var selectedId = SelectedPeer?.DeviceId;
                
                Peers.Clear();
                foreach (var peer in e.Peers)
                {
                    Peers.Add(peer);
                }

                // Restore selection
                if (selectedId != null)
                {
                    SelectedPeer = e.Peers.Find(p => p.DeviceId == selectedId);
                }

                OnPropertyChanged(nameof(HasNoPeers));

                if (e.Count > 0)
                {
                    StatusMessage = $"发现 {e.Count} 台设备";
                }
                else if (IsDiscovering)
                {
                    StatusMessage = "正在扫描...";
                }
            });
        }

        private void Engine_OnProgress(object? sender, ProgressEventArgs e)
        {
            InvokeOnUI(() =>
            {
                ProgressValue = e.Percentage;
                StatusMessage = $"{e.Percentage:F1}% {e.FormattedSpeed}".Trim();
            });
        }

        private void Engine_OnTransferComplete(object? sender, TransferCompleteEventArgs e)
        {
            InvokeOnUI(() =>
            {
                ProgressValue = e.Success ? 100 : 0;
                IsSending = false;
                _hasPendingTransfer = false;

                if (e.Success)
                {
                    StatusMessage = "Transfer complete!";
                    AddLog("✓ Transfer completed successfully");
                    
                    MessageBox.Show(
                        "File transfer completed successfully!",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = $"Failed: {e.ErrorMessage}";
                    AddLog($"✗ Transfer failed: {e.ErrorMessage}");
                    
                    MessageBox.Show(
                        $"Transfer failed:\n{e.ErrorMessage}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }

        private void Engine_OnPendingTransfer(object? sender, PendingTransferEventArgs e)
        {
            InvokeOnUI(() =>
            {
                _hasPendingTransfer = true;
                _pendingTransferInfo = e;
                OnPropertyChanged(nameof(IsTransferring));
                
                AddLog($"Incoming: {e.FileName} ({e.FormattedSize}) from {e.SenderName}");

                var result = MessageBox.Show(
                    $"Incoming file transfer\n\n" +
                    $"From: {e.SenderName}\n" +
                    $"Address: {e.SenderAddress}\n" +
                    $"File: {e.FileName}\n" +
                    $"Size: {e.FormattedSize}\n\n" +
                    "Accept this transfer?",
                    "File Transfer Request",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Sanitize filename
                    var safeName = SanitizeFileName(e.FileName);
                    var savePath = Path.Combine(GetDownloadsPath(), safeName);
                    
                    if (_engine.AcceptTransfer(savePath))
                    {
                        AddLog($"Accepted, saving to: {savePath}");
                        StatusMessage = "Receiving file...";
                    }
                    else
                    {
                        AddLog("Failed to accept transfer");
                        StatusMessage = "Accept failed";
                        _hasPendingTransfer = false;
                    }
                }
                else
                {
                    _engine.RejectTransfer();
                    AddLog("Transfer rejected");
                    StatusMessage = "Transfer rejected";
                    _hasPendingTransfer = false;
                }
                
                OnPropertyChanged(nameof(IsTransferring));
            });
        }

        #endregion

        #region Helpers

        /// <summary>
        /// CRITICAL: Execute action on UI thread to avoid cross-thread exceptions.
        /// All ObservableCollection and property updates must go through this.
        /// </summary>
        private void InvokeOnUI(Action action)
        {
            if (Application.Current?.Dispatcher == null)
            {
                action();
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// Add a timestamped log message (thread-safe).
        /// </summary>
        private void AddLog(string message)
        {
            InvokeOnUI(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                Logs.Add($"[{timestamp}] {message}");

                // Keep log size manageable
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(0);
                }
            });
        }

        /// <summary>
        /// Get the downloads folder path.
        /// </summary>
        private static string GetDownloadsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "VoidWarp"
            );
        }

        /// <summary>
        /// Format file size for display.
        /// </summary>
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

        /// <summary>
        /// Sanitize a filename by removing invalid characters.
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeName = new string(fileName
                .Where(c => !invalid.Contains(c))
                .ToArray());
            
            return string.IsNullOrWhiteSpace(safeName) ? "received_file" : safeName;
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            // Unsubscribe from events
            _engine.OnLog -= Engine_OnLog;
            _engine.OnPeerDiscovered -= Engine_OnPeerDiscovered;
            _engine.OnProgress -= Engine_OnProgress;
            _engine.OnTransferComplete -= Engine_OnTransferComplete;
            _engine.OnPendingTransfer -= Engine_OnPendingTransfer;

            // Dispose engine
            _engine.Dispose();
            
            _disposed = true;
        }

        #endregion
    }

    #region RelayCommand

    /// <summary>
    /// Simple ICommand implementation for MVVM binding.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    #endregion
}
