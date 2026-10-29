using System.Net.NetworkInformation;

namespace Dockbox.Services;

internal enum NetworkKind { None, WiFi, Ethernet, Other }

/// <summary>Conexão de rede em uso: tipo, nome da rede, sinal e bytes transferidos (para calcular a velocidade).</summary>
internal static class NetworkStatus
{
    public sealed record State(NetworkKind Kind, string Name, int SignalBars, long BytesReceived, long BytesSent);

    public static State Current()
    {
        var nic = ActiveInterface();
        if (nic == null) return new State(NetworkKind.None, "", 0, 0, 0);

        long rx = 0, tx = 0;
        try
        {
            var stats = nic.GetIPStatistics();
            rx = stats.BytesReceived;
            tx = stats.BytesSent;
        }
        catch { }

        var kind = nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => NetworkKind.WiFi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => NetworkKind.Ethernet,
            _ => NetworkKind.Other,
        };

        string name = "";
        int bars = 0;
        try
        {
            // Nome do perfil (no Wi-Fi é o nome da rede) sem pedir permissão de localização.
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile != null)
            {
                name = profile.ProfileName ?? "";
                if (profile.IsWlanConnectionProfile)
                {
                    kind = NetworkKind.WiFi;
                    bars = profile.GetSignalBars() ?? 0;
                }
            }
        }
        catch { }
        if (name.Length == 0) name = nic.Name;
        return new State(kind, name, bars, rx, tx);
    }

    /// <summary>A interface ligada com gateway (a que sai para a internet); com várias, a com mais tráfego.</summary>
    private static NetworkInterface? ActiveInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    && HasGateway(n))
                .OrderByDescending(Traffic)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static bool HasGateway(NetworkInterface n)
    {
        try { return n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)); }
        catch { return false; }
    }

    private static long Traffic(NetworkInterface n)
    {
        try
        {
            var s = n.GetIPStatistics();
            return s.BytesReceived + s.BytesSent;
        }
        catch { return 0; }
    }
}
