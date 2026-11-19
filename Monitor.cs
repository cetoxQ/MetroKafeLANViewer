using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MetroKafeLANViewer.Net;

namespace MetroKafeLANViewer;

/// <summary>
/// Tarama sonuçlarını cihaz kayıtlarıyla birleştirir; türü bulur, istemci aralığındaki istemci olmayan cihazları
/// (ör. elektrik kesintisinden sonra istemci IP'sini kapan kamera) çakışma olarak işaretler ve olayları kaydeder.
/// Yalnızca arayüz iş parçacığından çağrılır.
/// </summary>
public sealed partial class Monitor
{
    public ObservableCollection<Device> Devices { get; } = [];
    public ObservableCollection<EventEntry> Events { get; } = [];

    readonly Dictionary<string, Device> _byKey = new(StringComparer.OrdinalIgnoreCase);
    readonly DeviceFile _file;
    List<(uint From, uint To)> _clientRanges = [];
    Regex? _clientPattern;
    Regex? _clientLike;
    HashSet<uint> _gateways = [];
    HashSet<string> _gatewayMacs = [];
    HashSet<uint> _selfIps = [];
    HashSet<string> _conflicts = [];
    // İlk taramada kayıtlı cihazların hepsi "bağlandı" diye günlüğe yazılmasın
    bool _firstScanDone;
    // Kayıt boşken (ilk çalıştırma) her cihaz için "yeni cihaz" yazılmaz, tek bir özet yazılır
    bool _hadDevices;

    /// <summary>Yeni başlayan çakışmalar (tarama sonunda); bildirim ve ses için.</summary>
    public event Action<IReadOnlyList<Device>>? NewConflicts;

    public Monitor()
    {
        _file = Store.LoadDevices();
        foreach (var d in _file.Devices)
        {
            if (string.IsNullOrEmpty(d.Key) || _byKey.ContainsKey(d.Key)) continue;
            _byKey[d.Key] = d;
            Devices.Add(d);
        }
        _hadDevices = Devices.Count > 0;
    }

    public IEnumerable<Device> Conflicts => Devices.Where(d => d.Conflict);

    public void Configure(AppSettings s)
    {
        _clientRanges = IpRange.TryParse(s.ClientRanges, out var r, out _) ? r : [];
        _clientPattern = string.IsNullOrWhiteSpace(s.ClientNamePattern) ? null : TryRegex(s.ClientNamePattern);
        var adapters = IpRange.LocalAdapters();
        _gateways = [.. adapters.Where(a => a.Gateway != null).Select(a => IpRange.ToUInt(a.Gateway!))];
        _gatewayMacs = [.. adapters.Select(a => a.GatewayMac).OfType<string>()];
        _selfIps = [.. adapters.Select(a => IpRange.ToUInt(a.Ip))];
        // İlk geçiş aralıkları belirler, ad kalıbı ondan öğrenilir, ikinci geçiş türleri kalıpla bulur
        foreach (var d in Devices) Evaluate(d);
        LearnClientNames();
        foreach (var d in Devices) Evaluate(d);
        _conflicts = [.. Devices.Where(d => d.Conflict).Select(d => d.Key)];
    }

    /// <summary>
    /// İstemci aralığındaki Windows adlarının çoğu "MASA-012" gibi aynı önekle başlıyorsa bu önekten bir kalıp çıkarılır;
    /// aralık dışında bir IP almış istemci (ör. kamera IP'sini kaptığı için) yine istemci olarak tanınır.
    /// </summary>
    void LearnClientNames()
    {
        var names = Devices.Where(d => d.WasClient && d.Hostname != null && d.InClientRange).Select(d => ClientName().Match(d.Hostname!)).ToList();
        var top = names.Where(m => m.Success).GroupBy(m => m.Groups[1].Value.ToUpperInvariant()).OrderByDescending(g => g.Count()).FirstOrDefault();
        _clientLike = top != null && top.Count() >= 5 && top.Count() * 2 >= names.Count
            ? new Regex($@"^{Regex.Escape(top.Key)}[-_ ]?\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : null;
    }

    [GeneratedRegex(@"^([A-Za-z]+)[-_ ]?\d+$")]
    private static partial Regex ClientName();

    public static Regex? TryRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        }
        catch
        {
            return null;
        }
    }

    public bool IsClientIp(uint ip) => IpRange.Contains(_clientRanges, ip);
    public IReadOnlyList<(uint From, uint To)> ClientRanges => _clientRanges;

    public Device? ClientOwnerOf(uint ip) =>
        _file.ClientIps.TryGetValue(IpRange.FromUInt(ip).ToString(), out var key) && _byKey.TryGetValue(key, out var d) ? d : null;

    // ---------------------------------------------------------------- tarama sonuçları

    public void Apply(ScanResult r, DateTime now)
    {
        if (!r.Alive)
        {
            // Bu IP boş: orada açık görünen cihaz kapanmış ya da başka IP'ye geçmiş
            foreach (var gone in Devices.Where(d => d.Online && d.IpNum == r.Ip).ToList()) GoOffline(gone, now);
            return;
        }

        string key = r.Mac ?? "IP:" + IpRange.FromUInt(r.Ip);
        if (!_byKey.TryGetValue(key, out var d))
        {
            d = new Device { Key = key, Mac = r.Mac, FirstSeen = now, IpNum = r.Ip };
            _byKey[key] = d;
            Devices.Add(d);
            d.Vendor = Oui.Lookup(r.Mac);
            Fill(d, r, now);
            if (_hadDevices) Log(EventLevel.Bilgi, $"Yeni cihaz: {d.Ip} {Describe(d)}");
        }
        else
        {
            uint oldIp = d.IpNum;
            bool wasOnline = d.Online;
            d.Vendor ??= Oui.Lookup(r.Mac);
            Fill(d, r, now);
            if (oldIp != 0 && oldIp != r.Ip)
                Log(d.Kind == DeviceKind.Istemci ? EventLevel.Bilgi : EventLevel.Uyari, $"IP değişti: {Describe(d)} {IpRange.FromUInt(oldIp)} → {d.Ip}");
            else if (!wasOnline && _firstScanDone && d.Kind != DeviceKind.Istemci)
                Log(EventLevel.Bilgi, $"Ağa bağlandı: {d.Ip} {Describe(d)}");
        }

        // Aynı IP'de başka bir cihaz açık görünüyorsa o artık orada değildir
        foreach (var other in Devices.Where(o => o != d && o.Online && o.IpNum == r.Ip).ToList()) GoOffline(other, now);
    }

    void Fill(Device d, ScanResult r, DateTime now)
    {
        d.IpNum = r.Ip;
        d.Online = true;
        d.LastSeen = now;
        d.PingMs = r.PingMs;
        d.OpenPorts = r.OpenPorts;
        d.Banner = r.HttpBanner;
        d.NetBios = r.NetBiosName != null;
        if (r.NetBiosName != null)
        {
            d.Hostname = r.NetBiosName;
            d.Workgroup = r.Workgroup;
        }
        else if (r.DnsName != null)
        {
            d.Hostname = r.DnsName;
        }
        Evaluate(d);
    }

    void GoOffline(Device d, DateTime now)
    {
        d.Online = false;
        d.PingMs = null;
        Evaluate(d);
        if (_firstScanDone && d.Kind != DeviceKind.Istemci) Log(EventLevel.Bilgi, $"Ağdan çıktı: {d.Ip} {Describe(d)}");
    }

    /// <summary>Türü ve çakışma durumunu yeniden hesaplar; istemcileri öğrenir.</summary>
    public void Evaluate(Device d)
    {
        d.IsGateway = _gateways.Contains(d.IpNum) || d.Mac != null && _gatewayMacs.Contains(d.Mac);
        d.IsSelf = _selfIps.Contains(d.IpNum) && d.Online;
        d.InClientRange = d.IpNum != 0 && IsClientIp(d.IpNum);
        var (kind, reason) = Classifier.Classify(d, _clientPattern, d.InClientRange, _clientLike);
        d.Kind = kind;
        d.KindReason = reason;

        // Windows adıyla istemci olarak tanınan cihaz hatırlanır; adı alınamasa da (güvenlik duvarı) istemci kalır
        if (kind == DeviceKind.Istemci && d.Online && d.ManualKind == null && d.NetBios) d.WasClient = true;
        if (kind == DeviceKind.Istemci && d.Online && d.InClientRange) _file.ClientIps[d.Ip] = d.Key;

        bool conflict = d.Online && d.InClientRange && kind != DeviceKind.Istemci && !d.IgnoreConflict;
        d.Conflict = conflict;
        if (!conflict)
        {
            d.ConflictText = null;
            return;
        }
        var owner = ClientOwnerOf(d.IpNum);
        string whose = owner != null && owner != d ? $" ({owner.DisplayName} bilgisayarının IP'si)" : "";
        d.ConflictText = $"{(kind == DeviceKind.Bilinmiyor ? "Tanınmayan cihaz" : Kinds.Text(kind))} istemci IP'sini aldı{whose}";
    }

    /// <summary>Tarama bitince: çakışma başlangıç/bitişlerini kaydeder, yenileri bildirir, kayıtları yazar.</summary>
    public void FinishScan()
    {
        var now = Devices.Where(d => d.Conflict).ToList();
        var fresh = now.Where(d => !_conflicts.Contains(d.Key)).ToList();
        foreach (var d in fresh)
            Log(EventLevel.Alarm, $"ÇAKIŞMA: {d.Ip} → {Describe(d)}. {d.ConflictText}.");
        var nowKeys = now.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _conflicts.Where(k => !nowKeys.Contains(k)))
        {
            if (!_byKey.TryGetValue(key, out var d)) continue;
            string why = !d.Online ? "cihaz ağdan çıktı" : !d.InClientRange ? $"cihaz artık {d.Ip} adresinde" : $"artık {Kinds.Text(d.Kind)} sayılıyor";
            Log(EventLevel.Duzeldi, $"Çakışma bitti: {Describe(d)} ({why}).");
        }
        _conflicts = nowKeys;
        if (!_hadDevices && Devices.Count > 0)
        {
            Log(EventLevel.Bilgi, $"İlk tarama: {Devices.Count} cihaz kaydedildi ({Devices.Count(d => d.Kind == DeviceKind.Istemci)} istemci). Bundan sonra yeni cihazlar burada yazılır.");
            _hadDevices = true;
        }
        _firstScanDone = true;
        LearnClientNames();
        Save();
        if (fresh.Count > 0) NewConflicts?.Invoke(fresh);
    }

    public void Save()
    {
        _file.Devices = [.. Devices];
        Store.SaveDevices(_file);
    }

    public void Remove(Device d)
    {
        Devices.Remove(d);
        _byKey.Remove(d.Key);
        foreach (var ip in _file.ClientIps.Where(kv => kv.Value == d.Key).Select(kv => kv.Key).ToList()) _file.ClientIps.Remove(ip);
        _conflicts.Remove(d.Key);
        Save();
    }

    public static string Describe(Device d)
    {
        var parts = new List<string> { Kinds.Text(d.Kind) };
        if (!string.IsNullOrEmpty(d.DisplayName)) parts.Add(d.DisplayName);
        else if (d.Vendor != null) parts.Add(d.Vendor);
        if (d.Mac != null) parts.Add(d.Mac);
        return string.Join(" · ", parts);
    }

    public void Log(EventLevel level, string text)
    {
        var e = new EventEntry(DateTime.Now, level, text);
        Events.Insert(0, e);
        while (Events.Count > 2000) Events.RemoveAt(Events.Count - 1);
        Store.AppendLog(e);
    }
}
