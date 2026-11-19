using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MetroKafeLANViewer.Net;

namespace MetroKafeLANViewer;

// Kayıtta adıyla tutulur; sıra değişebilir
public enum DeviceKind { Bilinmiyor, Istemci, Bilgisayar, Kamera, Modem, Router, AgCihazi, Yazici, Mobil }

public static class Kinds
{
    public static string Text(DeviceKind k) => k switch
    {
        DeviceKind.Istemci => "İstemci",
        DeviceKind.Bilgisayar => "Bilgisayar",
        DeviceKind.Kamera => "Kamera",
        DeviceKind.Modem => "Modem",
        DeviceKind.Router => "Wi-Fi router",
        DeviceKind.AgCihazi => "Ağ cihazı (switch / AP)",
        DeviceKind.Yazici => "Yazıcı",
        DeviceKind.Mobil => "Telefon / tablet",
        _ => "Bilinmiyor",
    };

    public static readonly DeviceKind[] All = Enum.GetValues<DeviceKind>();
}

/// <summary>
/// Ağda görülmüş bir cihaz. Anahtar MAC adresidir (MAC alınamayan, başka ağdaki cihazlarda "IP:a.b.c.d").
/// Kullanıcının verdiği ad, tür ve not ile en son görülme bilgileri veri\cihazlar.json'a kaydedilir.
/// </summary>
public sealed class Device : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public string? Mac { get; set; }

    // ------------------------------------------------ kalıcı bilgiler
    public string? Name { get => _name; set => Set(ref _name, value, nameof(DisplayName)); }
    public string? Note { get => _note; set => Set(ref _note, value); }
    /// <summary>Kullanıcının elle seçtiği tür; null ise tür otomatik bulunur.</summary>
    public DeviceKind? ManualKind { get => _manualKind; set => Set(ref _manualKind, value); }
    /// <summary>Bir kez istemci olarak tanındıysa (NetBIOS adıyla) sonra adı alınamasa da istemci sayılır.</summary>
    public bool WasClient { get; set; }
    /// <summary>Bilerek istemci aralığında duran cihaz (sabit IP'li sunucu vb.): çakışma sayılmaz.</summary>
    public bool IgnoreConflict { get; set; }
    /// <summary>Haritada durduğu bölmenin kimliği (<see cref="GroupDef.Id"/>); null ise "Diğer cihazlar".</summary>
    public string? Group { get; set; }
    public string? Hostname { get => _hostname; set => Set(ref _hostname, value, nameof(DisplayName)); }
    public string? Workgroup { get; set; }
    public string? Vendor { get => _vendor; set => Set(ref _vendor, value, nameof(DisplayName)); }
    public uint IpNum { get => _ipNum; set => Set(ref _ipNum, value, nameof(Ip)); }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get => _lastSeen; set => Set(ref _lastSeen, value); }

    // ------------------------------------------------ son taramadan
    [JsonIgnore] public bool Online { get => _online; set => Set(ref _online, value, nameof(StatusText)); }
    [JsonIgnore] public int? PingMs { get => _ping; set => Set(ref _ping, value, nameof(PingText)); }
    [JsonIgnore] public int[] OpenPorts { get => _ports; set => Set(ref _ports, value, nameof(PortsText)); }
    [JsonIgnore] public string? Banner { get => _banner; set => Set(ref _banner, value, nameof(PortsText)); }
    [JsonIgnore]
    public DeviceKind Kind
    {
        get => _kind;
        set
        {
            Set(ref _kind, value, nameof(KindText));
            PropertyChanged?.Invoke(this, new(nameof(KindIcon)));
            PropertyChanged?.Invoke(this, new(nameof(KindBrush)));
        }
    }
    [JsonIgnore] public string? KindReason { get => _kindReason; set => Set(ref _kindReason, value); }
    [JsonIgnore] public bool InClientRange { get => _inClient; set => Set(ref _inClient, value); }
    [JsonIgnore] public bool Conflict { get => _conflict; set => Set(ref _conflict, value, nameof(StatusText)); }
    [JsonIgnore] public string? ConflictText { get => _conflictText; set => Set(ref _conflictText, value); }
    [JsonIgnore] public bool IsGateway { get; set; }
    /// <summary>Son taramada Windows adı (NetBIOS) yanıtı geldi.</summary>
    [JsonIgnore] public bool NetBios { get; set; }
    [JsonIgnore] public bool IsSelf { get; set; }

    // ------------------------------------------------ görünüm
    [JsonIgnore] public string Ip => IpNum == 0 ? "" : IpRange.FromUInt(IpNum).ToString();
    [JsonIgnore] public string DisplayName => Name ?? Hostname ?? "";
    [JsonIgnore] public string KindText => Kinds.Text(Kind);
    [JsonIgnore] public System.Windows.Media.Geometry KindIcon => Icons.Of(Kind);
    [JsonIgnore] public System.Windows.Media.Brush KindBrush => Icons.BrushOf(Kind);
    [JsonIgnore] public string PingText => PingMs is { } ms ? (ms == 0 ? "<1" : ms.ToString()) : Online ? "—" : "";
    [JsonIgnore] public string StatusText => Conflict ? "ÇAKIŞMA" : Online ? "Açık" : "Kapalı";
    [JsonIgnore]
    public string PortsText
    {
        get
        {
            string ports = string.Join(", ", OpenPorts.Select(Probes.PortName));
            return Banner == null ? ports : ports.Length == 0 ? Banner : $"{ports} — {Banner}";
        }
    }

    string? _name, _note, _hostname, _vendor, _banner, _kindReason, _conflictText;
    DeviceKind? _manualKind;
    uint _ipNum;
    DateTime _lastSeen;
    bool _online, _inClient, _conflict;
    int? _ping;
    int[] _ports = [];
    DeviceKind _kind;

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, string? also = null, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
        if (also != null) PropertyChanged?.Invoke(this, new(also));
    }

    /// <summary>Görünümü yenilemek için (ör. tür değişince satır rengi).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new(string.Empty));
}
