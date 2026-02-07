using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        private ReceivedFileInfo? _ongoingTransfer;
        private readonly HashSet<string> _acceptedPeers = new();
        private bool _showLogs = false;
        private DispatcherTimer? _historySyncTimer;

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

        /// <summary>
        /// Files pending to be sent (queue for multi-file sending).
        /// Thread-safe: all updates go through Dispatcher.
        /// </summary>
        public ObservableCollection<PendingFileInfo> PendingFiles { get; } = new();
        public ObservableCollection<ReceivedFileInfo> ReceivedFiles { get; } = new();

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
        /// True when there are no received files in history.
        /// </summary>
        public bool HasNoReceivedFiles => ReceivedFiles.Count == 0;


        public bool ShowLogs
        {
            get => _showLogs;
            set => SetProperty(ref _showLogs, value);
        }

        public ICommand ToggleLogsCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand CopyLogsCommand { get; }
        public ICommand ConfigureFirewallCommand { get; }


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
        public bool IsTransferring => IsSending || _hasPendingTransfer || OngoingTransfer != null;

        /// <summary>
        /// Currently active transfer (only for incoming/outgoing that hasn't reached terminal state).
        /// </summary>
        public ReceivedFileInfo? OngoingTransfer
        {
            get => _ongoingTransfer;
            set
            {
                if (SetProperty(ref _ongoingTransfer, value))
                {
                    OnPropertyChanged(nameof(IsTransferring));
                    OnPropertyChanged(nameof(HasOngoingTransfer));
                }
            }
        }

        public bool HasOngoingTransfer => OngoingTransfer != null;


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
        /// True if send operation can proceed (device + files ready).
        /// </summary>
        public bool CanSend => HasPendingFiles && HasPeerSelected && !IsSending;

        /// <summary>
        /// True if there are files in the pending queue.
        /// </summary>
        public bool HasPendingFiles => PendingFiles.Count > 0;

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
        public ICommand SendFileCommand { get; } // Legacy - now sends all
        public ICommand SendAllFilesCommand { get; } // Send all pending files
        public ICommand ToggleReceiverCommand { get; }
        public ICommand SelectFileCommand { get; } // Legacy - adds to queue
        public ICommand PickFileCommand { get; } // Add file(s) to queue
        public ICommand AddFileCommand { get; } // Add file(s) to queue
        public ICommand AddFolderCommand { get; } // Add folder(s) to queue
        public ICommand RemoveFileCommand { get; } // Remove file from queue
        public ICommand TestConnectionCommand { get; }
        public ICommand OpenDownloadsCommand { get; }
        public ICommand AddManualPeerCommand { get; }
        public ICommand DeleteReceivedFileCommand { get; }
        public ICommand CancelTransferCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand ClearAllHistoryCommand { get; }
        public ICommand RemoveHistoryItemCommand { get; }



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
            SendFileCommand = new RelayCommand(async _ => await SendAllFilesAsync(), _ => CanSend);
            SendAllFilesCommand = new RelayCommand(async _ => await SendAllFilesAsync(), _ => CanSend);
            ToggleReceiverCommand = new RelayCommand(_ => ToggleReceiver());
            SelectFileCommand = new RelayCommand(_ => AddFileToPendingQueue());
            PickFileCommand = new RelayCommand(_ => AddFileToPendingQueue());
            AddFileCommand = new RelayCommand(_ => AddFileToPendingQueue());
            AddFolderCommand = new RelayCommand(_ => AddFolderToPendingQueue());
            RemoveFileCommand = new RelayCommand(file => RemoveFileFromQueue(file as PendingFileInfo));
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            TestConnectionCommand = new RelayCommand(peer => TestConnection(peer as PeerItem));
            OpenDownloadsCommand = new RelayCommand(_ => OpenDownloadsFolder());
            AddManualPeerCommand = new RelayCommand(_ => ShowAddPeerDialog());
            DeleteReceivedFileCommand = new RelayCommand(file => DeleteReceivedFile(file as ReceivedFileInfo));
            CancelTransferCommand = new RelayCommand(_ => CancelTransfer());
            ConfigureFirewallCommand = new RelayCommand(_ => Core.FirewallHelper.RunFirewallSetupScript());
            ToggleLogsCommand = new RelayCommand(_ => ShowLogs = !ShowLogs);
            ClearLogsCommand = new RelayCommand(_ => InvokeOnUI(() => Logs.Clear()));
            CopyLogsCommand = new RelayCommand(_ => CopyLogsToClipboard());
            OpenFileCommand = new RelayCommand(file => OpenReceivedFile(file as ReceivedFileInfo));
            ClearAllHistoryCommand = new RelayCommand(_ => ClearAllHistory(), _ => !IsTransferring);
            RemoveHistoryItemCommand = new RelayCommand(file => RemoveHistoryItem(file as ReceivedFileInfo));
            CopyLogsCommand = new RelayCommand(_ => CopyLogsToClipboard());

            // Start history sync timer
            StartHistorySyncTimer();


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
                _ = LoadHistoryAsync();
                
                ReceivedFiles.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoReceivedFiles));
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

        /// <summary>
        /// Add a file to the pending files queue (new multi-file workflow).
        /// </summary>
        private void AddFileToPendingQueue()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要发送的文件",
                Filter = "All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = true // Allow multi-selection
            };

            if (dialog.ShowDialog() == true)
            {
                InvokeOnUI(() =>
                {
                    foreach (var filePath in dialog.FileNames)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            
                            // Check if file is already in queue
                            if (PendingFiles.Any(f => f.FilePath == filePath))
                            {
                                AddLog($"文件已在队列中: {fileInfo.Name}");
                                continue;
                            }

                            var pendingFile = new PendingFileInfo
                            {
                                FilePath = filePath,
                                FileSize = fileInfo.Length
                            };

                            PendingFiles.Add(pendingFile);
                            AddLog($"已添加: {fileInfo.Name} ({FormatSize(fileInfo.Length)})");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"无法添加文件: {Path.GetFileName(filePath)} - {ex.Message}");
                        }
                    }

                    OnPropertyChanged(nameof(HasPendingFiles));
                    OnPropertyChanged(nameof(CanSend));
                    
                    if (dialog.FileNames.Length > 0)
                    {
                        StatusMessage = $"{PendingFiles.Count} 个文件待发送";
                    }
                });
            }
        }

        /// <summary>
        /// Add a folder to the pending files queue.
        /// </summary>
        private void AddFolderToPendingQueue()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Folder to Send",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                // Process in background to avoid freezing UI on large folders
                Task.Run(() =>
                {
                    foreach (var folderPath in dialog.FolderNames)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(folderPath);
                            
                            bool exists = false;
                            InvokeOnUI(() => { exists = PendingFiles.Any(f => f.FilePath == folderPath); });
                            
                            if (exists)
                            {
                                InvokeOnUI(() => AddLog($"Folder already in queue: {dirInfo.Name}"));
                                continue;
                            }

                            // Calculate total size (heavy operation)
                            long totalSize = 0;
                            try
                            {
                                totalSize = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                            }
                            catch (Exception ex)
                            {
                                InvokeOnUI(() => AddLog($"Error calculating size: {ex.Message}"));
                                continue;
                            }

                            InvokeOnUI(() =>
                            {
                                if (PendingFiles.Any(f => f.FilePath == folderPath)) return;

                                var pendingFile = new PendingFileInfo
                                {
                                    FilePath = folderPath,
                                    FileSize = totalSize,
                                    IsFolder = true
                                };

                                PendingFiles.Add(pendingFile);
                                AddLog($"Added folder: {dirInfo.Name} ({FormatSize(totalSize)})");
                                
                                OnPropertyChanged(nameof(HasPendingFiles));
                                OnPropertyChanged(nameof(CanSend));
                                StatusMessage = $"{PendingFiles.Count} items queued";
                            });
                        }
                        catch (Exception ex)
                        {
                            InvokeOnUI(() => AddLog($"Cannot add folder: {Path.GetFileName(folderPath)} - {ex.Message}"));
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Remove a file from the pending files queue.
        /// </summary>
        private void RemoveFileFromQueue(PendingFileInfo? file)
        {
            if (file == null) return;

            InvokeOnUI(() =>
            {
                PendingFiles.Remove(file);
                AddLog($"已移除: {file.FileName}");
                OnPropertyChanged(nameof(HasPendingFiles));
                OnPropertyChanged(nameof(CanSend));
                StatusMessage = PendingFiles.Count > 0 
                    ? $"{PendingFiles.Count} 个文件待发送" 
                    : "Ready";
            });
        }

        /// <summary>
        /// Send all pending files sequentially (matches Android behavior).
        /// </summary>
        private async Task SendAllFilesAsync()
        {
            if (SelectedPeer == null || !HasPendingFiles)
            {
                AddLog("请先选择设备和文件");
                return;
            }

            var filesToSend = PendingFiles.ToList();
            var peer = SelectedPeer;
            var successCount = 0;

            InvokeOnUI(() =>
            {
                IsSending = true;
                ProgressValue = 0;
            });

            try
            {
                for (int i = 0; i < filesToSend.Count; i++)
                {
                    var file = filesToSend[i];
                    
                    InvokeOnUI(() =>
                    {
                        StatusMessage = $"正在发送 ({i + 1}/{filesToSend.Count}): {file.FileName}";
                        AddLog($"[{i + 1}/{filesToSend.Count}] 发送: {file.FileName}");
                        
                        OngoingTransfer = new ReceivedFileInfo
                        {
                            FilePath = file.FilePath,
                            FileName = file.FileName,
                            FileSize = FormatSize(file.FileSize),
                            SenderName = peer.DeviceName,
                            ReceivedTime = DateTime.Now,
                            Status = TransferStatus.InProgress
                        };
                    });

                    try
                    {
                        await _engine.SendFileAsync(file.FilePath, peer);
                        successCount++;
                        
                        InvokeOnUI(() =>
                        {
                            AddLog($"✓ 成功: {file.FileName}");
                            if (OngoingTransfer != null)
                            {
                                OngoingTransfer.Status = TransferStatus.Success;
                                ReceivedFiles.Insert(0, OngoingTransfer);
                                OngoingTransfer = null;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        InvokeOnUI(() =>
                        {
                            AddLog($"✗ 失败: {file.FileName} - {ex.Message}");
                            if (OngoingTransfer != null)
                            {
                                OngoingTransfer.Status = TransferStatus.Failed;
                                ReceivedFiles.Insert(0, OngoingTransfer);
                                OngoingTransfer = null;
                            }
                        });
                    }

                    // Small delay between files
                    await Task.Delay(500);
                }

                // Final summary
                InvokeOnUI(() =>
                {
                    StatusMessage = $"传输完成: {successCount}/{filesToSend.Count} 个文件成功";
                    AddLog($"传输完成: 成功 {successCount}/{filesToSend.Count}");
                    
                    // Clear pending files if all succeeded
                    if (successCount == filesToSend.Count)
                    {
                        PendingFiles.Clear();
                        OnPropertyChanged(nameof(HasPendingFiles));
                        OnPropertyChanged(nameof(CanSend));
                    }

                    MessageBox.Show(
                        $"传输完成！\n\n成功: {successCount}\n失败: {filesToSend.Count - successCount}",
                        "虚空传送",
                        MessageBoxButton.OK,
                        successCount == filesToSend.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    
                    SaveHistory();
                });
            }
            finally
            {
                InvokeOnUI(() => IsSending = false);
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

                if (OngoingTransfer != null)
                {
                    // Move to history with terminal state
                    OngoingTransfer.Status = e.Success ? TransferStatus.Success : TransferStatus.Failed;
                    ReceivedFiles.Insert(0, OngoingTransfer);
                    OngoingTransfer = null;
                    SaveHistory();
                }

                if (e.Success)
                {
                    StatusMessage = "传输完成！";
                    AddLog("✓ 传输任务成功完成");
                }
                else
                {
                    StatusMessage = $"传输失败: {e.ErrorMessage}";
                    AddLog($"✗ 传输异常: {e.ErrorMessage}");
                    
                    MessageBox.Show(
                        $"传输失败：\n{e.ErrorMessage}",
                        "错误",
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
                
                string typeLabel = e.IsFolder ? "文件夹" : "文件";
                AddLog($"收到请求: {e.FileName} ({e.FormattedSize}) [{typeLabel}] 来自 {e.SenderName}");

                // Auto-accept if this peer was already approved in this session
                if (_acceptedPeers.Contains(e.SenderAddress) || _acceptedPeers.Contains(e.SenderName))
                {
                    AcceptIncomingTransfer(e);
                    return;
                }

                string typeStr = e.IsFolder ? "文件夹" : "文件";
                string sizeLabel = e.IsFolder ? "总大小" : "大小";

                var result = MessageBox.Show(
                    $"收到{typeStr}传输请求\n\n" +
                    $"发送者: {e.SenderName}\n" +
                    $"地址: {e.SenderAddress}\n" +
                    $"{typeStr}: {e.FileName}\n" +
                    $"{sizeLabel}: {e.FormattedSize}\n\n" +
                    "极力推荐仅接收信任设备的文件。是否接收？",
                    $"{typeStr}传输请求",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Remember this peer for the rest of the session to avoid repeated prompts
                    _acceptedPeers.Add(e.SenderAddress);
                    _acceptedPeers.Add(e.SenderName);
                    
                    AcceptIncomingTransfer(e);
                }
                else
                {
                    _engine.RejectTransfer();
                    AddLog("已拒绝传输");
                    StatusMessage = "已拒绝传输";
                    _hasPendingTransfer = false;
                }
                
                OnPropertyChanged(nameof(IsTransferring));
            });
        }

        private void AcceptIncomingTransfer(PendingTransferEventArgs e)
        {
            // Sanitize filename
            var safeName = SanitizeFileName(e.FileName);
            var downloadsPath = GetDownloadsPath();
            if (!Directory.Exists(downloadsPath))
            {
                Directory.CreateDirectory(downloadsPath);
            }
            var savePath = Path.Combine(downloadsPath, safeName);
            
            if (_engine.AcceptTransfer(savePath))
            {
                AddLog($"已接受，正在保存至: {savePath}");
                StatusMessage = "正在接收文件...";
                
                OngoingTransfer = new ReceivedFileInfo
                {
                    FilePath = savePath,
                    FileName = e.FileName,
                    FileSize = e.FormattedSize,
                    SenderName = e.SenderName,
                    ReceivedTime = DateTime.Now,
                    Status = TransferStatus.InProgress
                };
            }
            else
            {
                AddLog("接受传输失败");
                StatusMessage = "接收失败";
                _hasPendingTransfer = false;
            }
        }

        #endregion


        private async Task LoadHistoryAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidWarp", "history.json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var history = JsonSerializer.Deserialize<List<ReceivedFileInfo>>(json);
                        if (history != null)
                        {
                            InvokeOnUI(() =>
                            {
                                foreach (var item in history)
                                {
                                    // item.FileExists is computed, no need to set
                                    ReceivedFiles.Add(item);
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"Failed to load history: {ex.Message}");
                }
            });
        }

        private void SaveHistory()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidWarp");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                
                var filePath = Path.Combine(path, "history.json");
                var json = JsonSerializer.Serialize(ReceivedFiles.ToList());
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                AddLog($"Failed to save history: {ex.Message}");
            }
        }

        private void DeleteReceivedFile(ReceivedFileInfo? file)
        {
            if (file == null) return;

            var dialog = new DeleteConfirmationDialog();
            dialog.Owner = Application.Current.MainWindow;
            
            if (dialog.ShowDialog() == true)
            {
                // Remove from list
                ReceivedFiles.Remove(file);
                SaveHistory();

                // Delete physical file if requested
                if (dialog.ShouldDeleteFile && File.Exists(file.FilePath))
                {
                    try
                    {
                        File.Delete(file.FilePath);
                        AddLog($"Deleted file: {file.FileName}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        #region Helpers

        /// <summary>
        /// CRITICAL: Execute action on UI thread to avoid cross-thread exceptions.
        /// All ObservableCollection and property updates must go through this.
        /// </summary>
        private void InvokeOnUI(Action action)
        {
            if (_disposed) return;

            try
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
            catch (ObjectDisposedException)
            {
                // App shutting down
            }
            catch (InvalidOperationException)
            {
                // Dispatcher shutting down or similar
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] InvokeOnUI error: {ex.Message}");
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

            _engine.OnLog -= Engine_OnLog;
            _engine.OnPeerDiscovered -= Engine_OnPeerDiscovered;
            _engine.OnProgress -= Engine_OnProgress;
            _engine.OnTransferComplete -= Engine_OnTransferComplete;
            _engine.OnPendingTransfer -= Engine_OnPendingTransfer;

            try { _engine.Dispose(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Engine.Dispose error: {ex.Message}");
            }

            _disposed = true;
        }

        #endregion

        private void ClearAllHistory()
        {
            if (IsTransferring) return;
            
            var result = MessageBox.Show("确定要清空所有传输记录吗？\n(这不会删除实际文件)", "虚空传送", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                InvokeOnUI(() =>
                {
                    ReceivedFiles.Clear();
                    SaveHistory();
                });
            }
        }

        private void RemoveHistoryItem(ReceivedFileInfo? file)
        {
            if (file == null) return;
            InvokeOnUI(() =>
            {
                ReceivedFiles.Remove(file);
                SaveHistory();
            });
        }

        private void StartHistorySyncTimer()
        {
            _historySyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _historySyncTimer.Tick += (s, e) => SyncHistoryWithDisk();
            _historySyncTimer.Start();
        }

        private void SyncHistoryWithDisk()
        {
            bool changed = false;
            foreach (var item in ReceivedFiles.ToList())
            {
                if (item.Status == TransferStatus.Success && !item.FileExists)
                {
                    item.Status = TransferStatus.Deleted;
                    changed = true;
                }
            }
            if (changed)
            {
                // Force UI update for the whole collection to refresh badges
                // (Alternatively, implement INPC on ReceivedFileInfo, but this is simpler for now)
                OnPropertyChanged(nameof(ReceivedFiles));
                SaveHistory();
            }
        }

        private void OpenReceivedFile(ReceivedFileInfo? file)
        {
            if (file == null || file.Status == TransferStatus.Deleted || !File.Exists(file.FilePath)) return;

            try
            {
                Process.Start(new ProcessStartInfo(file.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog($"无法打开文件: {ex.Message}");
            }
        }

        private void CopyLogsToClipboard()
        {
            try
            {
                var logContent = string.Join(Environment.NewLine, Logs);
                if (!string.IsNullOrEmpty(logContent))
                {
                    Clipboard.SetText(logContent);
                    MessageBox.Show("日志已复制到剪贴板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog($"复制日志失败: {ex.Message}");
            }
        }
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

        public void Execute(object? parameter)
        {
            try
            {
                _execute(parameter);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RelayCommand] Execute error: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"操作失败：{ex.Message}",
                    "VoidWarp",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    #endregion
}
