using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;

namespace VoidWarp.Windows.Core
{
    /// <summary>
    /// Helper class for firewall and network discovery diagnostics
    /// </summary>
    public static class FirewallHelper
    {
        /// <summary>
        /// Check if the machine likely has network discovery issues
        /// </summary>
        public static NetworkDiagnosticResult DiagnoseNetworkIssues()
        {
            var result = new NetworkDiagnosticResult();

            // Check if any network interface is up
            var hasActiveInterface = false;
            var hasPrivateIp = false;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    hasActiveInterface = true;

                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        var ip = addr.Address.ToString();
                        if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                        {
                            hasPrivateIp = true;
                            break;
                        }
                    }

                    if (hasPrivateIp)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirewallHelper] Error checking network interfaces: {ex.Message}");
            }

            if (!hasActiveInterface)
            {
                result.Issues.Add("未检测到活动的网络接口");
                result.Suggestions.Add("请检查网络连接是否正常");
            }
            else if (!hasPrivateIp)
            {
                result.Issues.Add("未检测到局域网 IP 地址");
                result.Suggestions.Add("请确保设备连接到 Wi-Fi 或有线局域网");
            }

            // Add general suggestions for mDNS issues
            result.Suggestions.Add("确保 Windows 防火墙允许 VoidWarp 通过");
            result.Suggestions.Add("确保\"网络发现\"已在网络设置中启用");
            result.Suggestions.Add("如果使用 VPN，可能需要断开 VPN 连接");

            return result;
        }

        /// <summary>
        /// Show a dialog with network troubleshooting suggestions
        /// </summary>
        public static void ShowNetworkTroubleshootingDialog()
        {
            var diag = DiagnoseNetworkIssues();

            var issuesText = diag.Issues.Count > 0
                ? "检测到的问题:\n• " + string.Join("\n• ", diag.Issues) + "\n\n"
                : "";

            var suggestionsText = "建议操作:\n• " + string.Join("\n• ", diag.Suggestions);

            var message = $"设备发现功能可能受到网络配置影响。\n\n{issuesText}{suggestionsText}\n\n是否打开 Windows 防火墙设置？";

            var result = MessageBox.Show(
                message,
                "网络诊断",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (result == MessageBoxResult.Yes)
            {
                OpenFirewallSettings();
            }
        }

        /// <summary>
        /// Open Windows Firewall settings
        /// </summary>
        public static void OpenFirewallSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "control.exe",
                    Arguments = "firewall.cpl",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirewallHelper] Failed to open firewall settings: {ex.Message}");
                MessageBox.Show("无法打开防火墙设置", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Open Network and Sharing Center (for network discovery settings)
        /// </summary>
        public static void OpenNetworkSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirewallHelper] Failed to open network settings: {ex.Message}");
                MessageBox.Show("无法打开网络设置", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public sealed class NetworkDiagnosticResult
    {
        public List<string> Issues { get; } = [];
        public List<string> Suggestions { get; } = [];
        public bool HasCriticalIssues => Issues.Count > 0;
    }
}
