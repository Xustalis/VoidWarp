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

            // CRITICAL: Ensure this window is recognized as the MainWindow for dialog ownership
            Application.Current.MainWindow = this;
            // _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
        }

        /// <summary>
        /// Open file picker when user clicks the file selector area (matches Android tap-to-select).
        /// </summary>
        private void FileSelectArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel.SelectFileCommand.CanExecute(null))
                _viewModel.SelectFileCommand.Execute(null);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Cleanup when window is closed. Safe Dispose so shutdown never throws.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // _viewModel.Logs.CollectionChanged -= Logs_CollectionChanged;

            try
            {
                _viewModel.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Dispose error: {ex.Message}");
            }

            base.OnClosed(e);
        }
    }
}
