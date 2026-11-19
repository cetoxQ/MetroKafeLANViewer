using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MetroKafeLANViewer.Net;

/// <summary>
/// "192.168.2.1-110", "192.168.2.1-192.168.2.254", "192.168.2.0/24" veya tek IP biçimindeki aralıklar.
/// Birden çok aralık virgülle ayrılır.
/// </summary>
public static class IpRange
{
    public static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    public static IPAddress FromUInt(uint v) => new([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);

    public static bool TryParse(string? text, out List<(uint From, uint To)> ranges, out string? error)
    {
        ranges = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Aralık boş.";
            return false;
        }
        foreach (string raw in text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseOne(raw, out var r))
            {
                error = $"Anlaşılamadı: {raw}";
                return false;
            }
            if (r.To - r.From > 65535)
            {
                error = $"Aralık çok büyük: {raw}";
                return false;
            }
            ranges.Add(r);
        }
        return true;
    }

    static bool TryParseOne(string s, out (uint From, uint To) r)
    {
        r = default;
        int slash = s.IndexOf('/');
        if (slash > 0)
        {
            if (!IPAddress.TryParse(s[..slash], out var net) || !int.TryParse(s[(slash + 1)..], out int bits) || bits < 16 || bits > 32) return false;
            uint mask = bits == 32 ? uint.MaxValue : ~(uint.MaxValue >> bits);
            uint start = ToUInt(net) & mask, end = start | ~mask;
            // Ağ ve yayın adresleri atlanır
            r = bits >= 31 ? (start, end) : (start + 1, end - 1);
            return true;
        }
        int dash = s.IndexOf('-');
        if (dash < 0)
        {
            if (!IPAddress.TryParse(s, out var one) || one.AddressFamily != AddressFamily.InterNetwork) return false;
            r = (ToUInt(one), ToUInt(one));
            return true;
        }
        if (!IPAddress.TryParse(s[..dash].Trim(), out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
        string right = s[(dash + 1)..].Trim();
        uint from = ToUInt(a), to;
        if (byte.TryParse(right, out byte last)) to = (from & 0xFFFFFF00) | last;
        else if (IPAddress.TryParse(right, out var b) && b.AddressFamily == AddressFamily.InterNetwork) to = ToUInt(b);
        else return false;
        if (to < from) return false;
        r = (from, to);
        return true;
    }

    public static IEnumerable<uint> Enumerate(IEnumerable<(uint From, uint To)> ranges) =>
        ranges.SelectMany(r => Enumerable.Range(0, (int)(r.To - r.From + 1)).Select(i => r.From + (uint)i)).Distinct().Order();

    public static bool Contains(IEnumerable<(uint From, uint To)> ranges, uint ip) => ranges.Any(r => ip >= r.From && ip <= r.To);

    /// <summary>Bu bilgisayarın çalışan IPv4 bağdaştırıcıları: IP, ağ maskesi, ağ geçidi ve MAC.</summary>
    public static List<LocalAdapter> LocalAdapters()
    {
        var list = new List<LocalAdapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            var gws = props.GatewayAddresses.Select(g => g.Address).ToList();
            var gw = gws.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            // Yalnızca IPv6 ağ geçidi varsa (fe80::...) modemin MAC'i adresin içinden çıkarılır (EUI-64)
            string? gwMac = gws.Where(a => a.IsIPv6LinkLocal).Select(MacFromEui64).FirstOrDefault(m => m != null);
            bool hasGateway = gw != null || gws.Count > 0;
            foreach (var u in props.UnicastAddresses)
            {
                if (u.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(u.Address)) continue;
                // 169.254 (DHCP alamamış) bağdaştırıcılar atlanır
                if (u.Address.GetAddressBytes()[0] == 169) continue;
                list.Add(new(u.Address, u.PrefixLength, gw, FormatMac(nic.GetPhysicalAddress().GetAddressBytes()), nic.Name, hasGateway, gwMac));
            }
        }
        // Ağ geçidi olan bağdaştırıcı önce (asıl ağ odur)
        return [.. list.OrderByDescending(a => a.HasGateway)];
    }

    /// <summary>İlk bağdaştırıcının /24 ağı, ör. "192.168.2.1-254".</summary>
    public static string DefaultScanRange()
    {
        var a = LocalAdapters().FirstOrDefault();
        if (a == null) return "192.168.2.1-254";
        var b = a.Ip.GetAddressBytes();
        return $"{b[0]}.{b[1]}.{b[2]}.1-254";
    }

    public static string DefaultClientRange()
    {
        var a = LocalAdapters().FirstOrDefault();
        if (a == null) return "192.168.2.1-110";
        var b = a.Ip.GetAddressBytes();
        return $"{b[0]}.{b[1]}.{b[2]}.1-110";
    }

    /// <summary>fe80::7e77:16ff:feba:5400 → 7C:77:16:BA:54:00 (adres MAC'ten türetilmişse).</summary>
    static string? MacFromEui64(IPAddress a)
    {
        var b = a.GetAddressBytes();
        if (b.Length != 16 || b[11] != 0xFF || b[12] != 0xFE) return null;
        return FormatMac([(byte)(b[8] ^ 0x02), b[9], b[10], b[13], b[14], b[15]]);
    }

    public static string FormatMac(byte[] mac) => mac.Length == 0 ? "" : string.Join(":", mac.Select(x => x.ToString("X2")));
}

public sealed record LocalAdapter(IPAddress Ip, int Prefix, IPAddress? Gateway, string Mac, string Name, bool HasGateway, string? GatewayMac);
