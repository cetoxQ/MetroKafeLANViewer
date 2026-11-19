using System.Net;

namespace MetroKafeLANViewer.Net;

/// <summary>Bir IP'nin tarama sonucu. <see cref="Mac"/> ve <see cref="PingMs"/> ikisi de yoksa IP boştur.</summary>
public sealed record ScanResult(
    uint Ip,
    string? Mac,
    int? PingMs,
    string? NetBiosName,
    string? Workgroup,
    string? DnsName,
    int[] OpenPorts,
    string? HttpBanner)
{
    public bool Alive => Mac != null || PingMs != null;
}

/// <summary>
/// IP aralığını paralel tarar: her adrese aynı anda ARP isteği ve ping gönderilir; yanıt veren cihazlardan
/// NetBIOS adı, DNS adı, açık portlar ve (Windows olmayanlarda) web başlığı alınır.
/// </summary>
public sealed class Scanner
{
    public int PingTimeoutMs { get; init; } = 1000;
    public int Parallel { get; init; } = 128;

    /// <summary>Bu bilgisayarın kendi adresleri: ARP kendine yanıt vermez, MAC bağdaştırıcıdan alınır.</summary>
    readonly Dictionary<uint, string> _self = IpRange.LocalAdapters().ToDictionary(a => IpRange.ToUInt(a.Ip), a => a.Mac);

    public async Task ScanAsync(IReadOnlyList<uint> ips, Action<ScanResult> onResult, Action<int> onProgress, CancellationToken ct)
    {
        // SendARP iş parçacığını bekletir; iş parçacığı havuzu yavaş büyümesin diye en baştan yeterince açılır
        ThreadPool.GetMinThreads(out int w, out int io);
        if (w < Parallel + 16) ThreadPool.SetMinThreads(Parallel + 16, io);

        using var gate = new SemaphoreSlim(Parallel);
        int done = 0;
        var tasks = ips.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                onResult(await ProbeAsync(ip, ct));
            }
            finally
            {
                gate.Release();
                onProgress(Interlocked.Increment(ref done));
            }
        }).ToList();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
    }

    public async Task<ScanResult> ProbeAsync(uint ipNum, CancellationToken ct = default)
    {
        var ip = IpRange.FromUInt(ipNum);
        var arp = _self.TryGetValue(ipNum, out var own) ? Task.FromResult<string?>(own) : Task.Run(() => Probes.Arp(ip), ct);
        var ping = Probes.PingAsync(ip, PingTimeoutMs);
        string? mac = await arp;
        int? ms = await ping;
        if (mac == null && ms == null) return new(ipNum, null, null, null, null, null, [], null);

        var nbTask = Probes.NetBiosAsync(ip, 1200);
        var portsTask = Probes.OpenPortsAsync(ip, 600);
        var nb = await nbTask;
        int[] ports = await portsTask;

        string? dns = nb == null ? await Probes.DnsNameAsync(ip, 1500) : null;
        string? banner = null;
        // Windows bilgisayarın web başlığına bakmaya gerek yok; kamera/modem/yazıcı web arayüzünden tanınır
        if (nb == null)
        {
            int web = ports.Contains(80) ? 80 : ports.Contains(8080) ? 8080 : 0;
            if (web != 0) banner = await Probes.HttpBannerAsync(ip, web, 2000);
        }
        return new(ipNum, mac, ms, nb?.Name, nb?.Group, dns, ports, banner);
    }
}
