using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VoidWarp.Windows.Core
{
    public sealed class NetworkInterfaceSelector
    {
        private static readonly string[] PreferredInterfaceHints =
        [
            "wlan",
            "wi-fi",
            "wifi",
            "eth",
            "ethernet",
            "lan"
        ];

        private static readonly string[] VirtualInterfaceHints =
        [
            "virtual",
            "vmware",
            "hyper-v",
            "vbox",
            "virtualbox",
            "docker",
            "loopback",
            "tunnel",
            "vpn",
            "teredo",
            "tailscale",
            "zerotier"
        ];

        public NetworkSelectionResult SelectBestInterface()
        {
            var candidates = new List<NetworkCandidate>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var name = $"{ni.Name} {ni.Description}".ToLowerInvariant();
                if (VirtualInterfaceHints.Any(hint => name.Contains(hint)))
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var addressInfo in ipProps.UnicastAddresses)
                {
                    if (addressInfo.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (IPAddress.IsLoopback(addressInfo.Address))
                    {
                        continue;
                    }

                    if (IsLinkLocal(addressInfo.Address))
                    {
                        continue;
                    }

                    var ip = addressInfo.Address.ToString();
                    var score = ScoreInterface(ni, ip, ipProps);
                    var reason = BuildReason(ni, ip);
                    candidates.Add(new NetworkCandidate(ni.Name, ip, score, reason));
                }
            }

            if (candidates.Count == 0)
            {
                return new NetworkSelectionResult(null, null, "未找到可用的 IPv4 接口", candidates);
            }

            var best = candidates
                .OrderByDescending(c => c.Score)
                .First();

            return new NetworkSelectionResult(best.IpAddress, best.InterfaceName, best.Reason, candidates);
        }

        private static int ScoreInterface(NetworkInterface ni, string ip, IPInterfaceProperties props)
        {
            var score = 0;

            score += ni.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => 120,
                NetworkInterfaceType.Ethernet => 100,
                NetworkInterfaceType.Ethernet3Megabit => 90,
                NetworkInterfaceType.FastEthernetFx => 90,
                NetworkInterfaceType.FastEthernetT => 90,
                NetworkInterfaceType.GigabitEthernet => 110,
                _ => 50
            };

            if (PreferredInterfaceHints.Any(hint => ni.Name.ToLowerInvariant().Contains(hint) ||
                                                   ni.Description.ToLowerInvariant().Contains(hint)))
            {
                score += 25;
            }

            if (IsPrivateIpv4(ip))
            {
                score += ip.StartsWith("192.168.", StringComparison.Ordinal) ? 30 : 20;
            }

            if (props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                score += 10;
            }

            return score;
        }

        private static string BuildReason(NetworkInterface ni, string ip)
        {
            var type = ni.NetworkInterfaceType.ToString();
            var networkHint = IsPrivateIpv4(ip) ? "私网" : "非私网";
            return $"{type} · {networkHint}";
        }

        private static bool IsLinkLocal(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length >= 2 && bytes[0] == 169 && bytes[1] == 254;
        }

        private static bool IsPrivateIpv4(string ip)
        {
            if (ip.StartsWith("10.", StringComparison.Ordinal))
            {
                return true;
            }

            if (ip.StartsWith("192.168.", StringComparison.Ordinal))
            {
                return true;
            }

            if (ip.StartsWith("172.", StringComparison.Ordinal))
            {
                var parts = ip.Split('.');
                if (parts.Length > 1 && int.TryParse(parts[1], out var secondOctet))
                {
                    return secondOctet is >= 16 and <= 31;
                }
            }

            return false;
        }
    }

    public sealed class NetworkSelectionResult
    {
        public string? IpAddress { get; }
        public string? InterfaceName { get; }
        public string Reason { get; }
        public IReadOnlyList<NetworkCandidate> Candidates { get; }

        public NetworkSelectionResult(
            string? ipAddress,
            string? interfaceName,
            string reason,
            IReadOnlyList<NetworkCandidate> candidates)
        {
            IpAddress = ipAddress;
            InterfaceName = interfaceName;
            Reason = reason;
            Candidates = candidates;
        }
    }

    public sealed class NetworkCandidate
    {
        public string InterfaceName { get; }
        public string IpAddress { get; }
        public int Score { get; }
        public string Reason { get; }

        public NetworkCandidate(string interfaceName, string ipAddress, int score, string reason)
        {
            InterfaceName = interfaceName;
            IpAddress = ipAddress;
            Score = score;
            Reason = reason;
        }
    }
}
