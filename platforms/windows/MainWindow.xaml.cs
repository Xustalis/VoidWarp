using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using VoidWarp.Windows.ViewModels;

namespace VoidWarp.Windows
{
    /// <summary>
    /// MainWindow code-behind.
    /// Follows MVVM pattern - minimal logic here, all state in ViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // Create and bind ViewModel
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Auto-scroll logs to bottom when new items are added
            _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
        }

        /// <summary>
        /// Scroll log viewer to bottom when new log entries are added.
        /// </summary>
        private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Use BeginInvoke with low priority to ensure UI has updated
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        LogScrollViewer?.ScrollToEnd();
                    }
                    catch
                    {
                        // Ignore scroll errors
                    }
                }), DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Open file picker when user clicks the file selector area (matches Android tap-to-select).
        /// </summary>
        private void FileSelectArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel.SelectFileCommand.CanExecute(null))
                _viewModel.SelectFileCommand.Execute(null);
        }

        /// <summary>
        /// Cleanup when window is closed.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            _viewModel.Logs.CollectionChanged -= Logs_CollectionChanged;
            
            // Dispose ViewModel (stops all operations, cleans up native handles)
            _viewModel.Dispose();
            
            base.OnClosed(e);
        }
    }
}
