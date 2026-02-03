using System.Windows;

namespace VoidWarp.Windows
{
    public partial class DeleteConfirmationDialog : Window
    {
        public bool ShouldDeleteFile { get; private set; }

        public DeleteConfirmationDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ShouldDeleteFile = DeleteFileCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
