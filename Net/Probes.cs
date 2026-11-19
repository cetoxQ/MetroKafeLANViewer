using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MetroKafeLANViewer.Net;

/// <summary>Tek bir IP'ye yapılan yoklamalar: ARP, ping, NetBIOS adı, DNS adı, açık portlar ve web başlığı.</summary>
public static partial class Probes
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref int physAddrLen);

    /// <summary>
    /// ARP isteği gönderip MAC adresini alır. Ping'e yanıt vermeyen (güvenlik duvarlı) cihazlar da ARP'ye yanıt verir.
    /// Yalnızca aynı ağdaki adreslerde çalışır; birkaç saniye bekletebilir, bu yüzden ayrı iş parçacığında çağrılır.
    /// </summary>
    public static string? Arp(IPAddress ip)
    {
        var mac = new byte[6];
        int len = mac.Length;
        // SendARP adresi ağ bayt sırasında (bellekte a.b.c.d) ister
        uint dest = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
        if (SendARP(dest, 0, mac, ref len) != 0 || len < 6) return null;
        if (mac.All(b => b == 0)) return null;
        return IpRange.FormatMac(mac[..6]);
    }

    public static async Task<int?> PingAsync(IPAddress ip, int timeoutMs)
    {
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(ip, timeoutMs);
            return r.Status == IPStatus.Success ? (int)r.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- NetBIOS

    /// <summary>
    /// NetBIOS durum sorgusu (UDP 137, "NBSTAT"). Windows bilgisayarlar bilgisayar adını ve çalışma grubunu döner;
    /// kameralar ve telefonlar genelde yanıt vermez. İstemcileri tanımanın en güvenilir yolu budur.
    /// </summary>
    public static async Task<(string Name, string? Group)?> NetBiosAsync(IPAddress ip, int timeoutMs)
    {
        byte[] query =
        [
            0x4D, 0x4B, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x20, 0x43, 0x4B, .. Enumerable.Repeat((byte)0x41, 30), 0x00,
            0x00, 0x21, 0x00, 0x01,
        ];
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            using var cts = new CancellationTokenSource(timeoutMs);
            await udp.SendAsync(query, new IPEndPoint(ip, 137), cts.Token);
            var res = await udp.ReceiveAsync(cts.Token);
            return ParseNbstat(res.Buffer);
        }
        catch
        {
            return null;
        }
    }

    static (string Name, string? Group)? ParseNbstat(byte[] d)
    {
        // 12 bayt başlık + 34 bayt ad + tür/sınıf/TTL/uzunluk (10) = 56: ad sayısı
        const int Count = 56;
        if (d.Length < Count + 1) return null;
        int n = d[Count];
        string? name = null, group = null;
        for (int i = 0; i < n; i++)
        {
            int o = Count + 1 + i * 18;
            if (o + 18 > d.Length) break;
            string entry = Encoding.ASCII.GetString(d, o, 15).Trim();
            byte suffix = d[o + 15];
            bool isGroup = (d[o + 16] & 0x80) != 0;
            if (suffix != 0x00) continue;
            if (!isGroup) name ??= entry;
            else group ??= entry;
        }
        return name == null ? null : (name, group);
    }

    public static async Task<string?> DnsNameAsync(IPAddress ip, int timeoutMs)
    {
        try
        {
            var t = Dns.GetHostEntryAsync(ip);
            if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) return null;
            string host = (await t).HostName;
            // Ad bulunamayınca bazı DNS'ler IP'nin kendisini döner
            return string.IsNullOrEmpty(host) || IPAddress.TryParse(host, out _) ? null : host;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- portlar

    /// <summary>Cihaz türünü anlamaya yarayan portlar.</summary>
    public static readonly int[] KnownPorts = [80, 443, 554, 8000, 8080, 37777, 34567, 9100, 445, 3389, 5900, 53];

    public static string PortName(int port) => port switch
    {
        80 => "HTTP",
        443 => "HTTPS",
        554 => "RTSP",
        8000 => "Hikvision",
        8080 => "HTTP-8080",
        37777 => "Dahua",
        34567 => "XMEye",
        9100 => "Yazıcı",
        445 => "SMB",
        3389 => "RDP",
        5900 => "VNC",
        53 => "DNS",
        _ => port.ToString(),
    };

    public static async Task<int[]> OpenPortsAsync(IPAddress ip, int timeoutMs)
    {
        var tasks = KnownPorts.Select(async port => await IsOpenAsync(ip, port, timeoutMs) ? port : 0);
        return [.. (await Task.WhenAll(tasks)).Where(p => p != 0)];
    }

    static async Task<bool> IsOpenAsync(IPAddress ip, int port, int timeoutMs)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await s.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- web başlığı

    /// <summary>Web arayüzünün "Server" başlığı ve sayfa başlığı; kameralar ve modemler çoğu zaman markasını burada söyler.</summary>
    public static async Task<string?> HttpBannerAsync(IPAddress ip, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(ip, port, cts.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET / HTTP/1.0\r\nHost: {ip}\r\nUser-Agent: MetroKafeLANViewer\r\n\r\n"), cts.Token);
            var buf = new byte[8192];
            int total = 0;
            while (total < buf.Length)
            {
                int read = await stream.ReadAsync(buf.AsMemory(total), cts.Token);
                if (read == 0) break;
                total += read;
            }
            string text = Encoding.UTF8.GetString(buf, 0, total);
            var parts = new List<string>();
            var server = ServerHeader().Match(text);
            if (server.Success) parts.Add(server.Groups[1].Value.Trim());
            var title = TitleTag().Match(text);
            if (title.Success) parts.Add(WebUtility.HtmlDecode(title.Groups[1].Value).Trim());
            var realm = RealmHeader().Match(text);
            if (realm.Success) parts.Add(realm.Groups[1].Value.Trim());
            var location = LocationHeader().Match(text);
            if (location.Success) parts.Add("→ " + location.Groups[1].Value.Trim());
            string result = string.Join(" | ", parts.Where(p => p.Length > 0).Distinct());
            return result.Length == 0 ? null : result.Length > 160 ? result[..160] : result;
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"^Server:\s*(.+?)\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ServerHeader();

    [GeneratedRegex(@"<title[^>]*>\s*([^<]{1,120})", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"realm=""([^""]{1,80})""", RegexOptions.IgnoreCase)]
    private static partial Regex RealmHeader();

    [GeneratedRegex(@"^Location:\s*(.+?)\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LocationHeader();

    // ---------------------------------------------------------------- Wake-on-LAN

    public static async Task WakeAsync(string mac)
    {
        byte[] m = [.. mac.Split(':', '-').Select(x => Convert.ToByte(x, 16))];
        if (m.Length != 6) return;
        byte[] packet = [.. Enumerable.Repeat((byte)0xFF, 6), .. Enumerable.Range(0, 16).SelectMany(_ => m)];
        using var udp = new UdpClient { EnableBroadcast = true };
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, 9));
        foreach (var a in IpRange.LocalAdapters())
        {
            uint ip = IpRange.ToUInt(a.Ip), mask = a.Prefix == 0 ? 0 : ~(uint.MaxValue >> a.Prefix);
            await udp.SendAsync(packet, new IPEndPoint(IpRange.FromUInt(ip | ~mask), 9));
        }
    }
}
