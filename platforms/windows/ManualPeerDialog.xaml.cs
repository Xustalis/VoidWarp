using System.Net;
using System.Windows;

namespace VoidWarp.Windows
{
    public partial class ManualPeerDialog : Window
    {
        public string IpAddress { get; private set; } = string.Empty;
        public ushort Port { get; private set; } = 42424;

        public ManualPeerDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var ipInput = IpBox.Text.Trim();
            var portInput = PortBox.Text.Trim();

            if (!IPAddress.TryParse(ipInput, out _))
            {
                MessageBox.Show("请输入有效的 IP 地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ushort.TryParse(portInput, out var port))
            {
                MessageBox.Show("请输入有效端口号", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IpAddress = ipInput;
            Port = port;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
