using System.Configuration;
using System.Data;
using System.Windows;
using System.Diagnostics;
using System.IO;

namespace VoidWarp.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Best-effort firewall guidance. We don't auto-elevate; we give the user an actionable script.
        try
        {
            string publishDir = AppContext.BaseDirectory;
            string script = Path.Combine(publishDir, "firewall_rules.bat");

            if (File.Exists(script))
            {
                var result = MessageBox.Show(
                    "VoidWarp 需要局域网 UDP mDNS(5353) 才能发现设备。\n\n若你发现“无法互相发现”，请以管理员身份运行发布目录下的 `firewall_rules.bat` 添加防火墙规则。\n\n现在打开脚本所在目录？",
                    "防火墙提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = publishDir,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch
        {
            // ignore any startup issues
        }
    }
}

