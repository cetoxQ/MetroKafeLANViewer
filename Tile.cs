using System.ComponentModel;
using System.Windows.Media;
using MetroKafeLANViewer.Net;

namespace MetroKafeLANViewer;

/// <summary>Haritadaki bir IP kutusu. İstemci aralığında her IP için bir kutu vardır; boş IP'ler soluk görünür.</summary>
public sealed class Tile : INotifyPropertyChanged
{
    public uint Ip { get; init; }
    public Device? Device { get; private set; }

    public string Number => (Ip & 0xFF).ToString();
    public string Label { get; private set; } = "";
    public Brush Fill { get; private set; } = Empty;
    public Brush Edge { get; private set; } = EmptyEdge;
    public Brush NumberBrush { get; private set; } = Muted;
    public string Tip { get; private set; } = "";
    /// <summary>Tür simgesi; boş IP'de yok.</summary>
    public Geometry? Icon { get; private set; }
    public Brush IconBrush { get; private set; } = Muted;
    /// <summary>Çoklu seçimde seçili mi (beyaz kenar).</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }
    bool _selected;

    public System.Windows.Visibility IconVisibility => Icon == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static readonly Brush Empty = Frozen("#0F1522"), EmptyEdge = Frozen("#1C2433"), Muted = Frozen("#4B5568"), Text = Frozen("#E5E7EB");
    static readonly Brush ClientFill = Frozen("#0F2E1E"), ClientEdge = Frozen("#22C55E");
    static readonly Brush ConflictFill = Frozen("#5B1219"), ConflictEdge = Frozen("#EF4444");
    static readonly Brush CameraFill = Frozen("#1E2A4A"), CameraEdge = Frozen("#3B82F6");
    static readonly Brush OtherFill = Frozen("#2A2238"), OtherEdge = Frozen("#A78BFA");
    static readonly Brush OfflineFill = Frozen("#141A26"), OfflineEdge = Frozen("#2B3445");

    public void Set(Device? d, Device? owner)
    {
        Device = d;
        string ip = IpRange.FromUInt(Ip).ToString();
        if (d == null || !d.Online)
        {
            // Boş IP: burada en son görülen istemcinin adı soluk yazılır
            Label = owner?.DisplayName ?? (d != null ? ShortName(d) : "");
            Fill = d == null ? Empty : OfflineFill;
            Edge = d == null ? EmptyEdge : OfflineEdge;
            NumberBrush = Muted;
            // Bölmedeki kapalı cihaz: simgesi soluk görünür
            Icon = d != null ? Icons.Of(d.Kind) : null;
            IconBrush = Muted;
            Tip = d != null ? $"{ip} — kapalı\n{Monitor.Describe(d)}\nSon görülme: {d.LastSeen:dd.MM HH:mm}"
                : owner != null ? $"{ip} — boş\nEn son: {owner.DisplayName} ({owner.LastSeen:dd.MM HH:mm})" : $"{ip} — boş";
        }
        else
        {
            Label = d.Conflict ? Kinds.Text(d.Kind).ToUpper(Tr) : ShortName(d);
            (Fill, Edge) = d.Conflict ? (ConflictFill, ConflictEdge)
                : d.Kind == DeviceKind.Istemci ? (ClientFill, ClientEdge)
                : d.Kind == DeviceKind.Kamera ? (CameraFill, CameraEdge)
                : (OtherFill, OtherEdge);
            NumberBrush = Text;
            Icon = Icons.Of(d.Kind);
            IconBrush = d.Conflict ? ConflictEdge : Icons.BrushOf(d.Kind);
            var lines = new List<string> { $"{ip} — {Kinds.Text(d.Kind)}" };
            if (d.Conflict) lines.Add("⚠ " + d.ConflictText);
            if (!string.IsNullOrEmpty(d.DisplayName)) lines.Add(d.DisplayName);
            if (d.Mac != null) lines.Add($"MAC: {d.Mac}");
            if (d.Vendor != null) lines.Add($"Üretici: {d.Vendor}");
            if (d.PortsText.Length > 0) lines.Add($"Portlar: {d.PortsText}");
            if (d.KindReason != null) lines.Add($"Neden: {d.KindReason}");
            if (d.Note != null) lines.Add($"Not: {d.Note}");
            Tip = string.Join("\n", lines);
        }
        PropertyChanged?.Invoke(this, new(string.Empty));
    }

    static string ShortName(Device d)
    {
        if (!string.IsNullOrEmpty(d.Name)) return d.Name;
        if (!string.IsNullOrEmpty(d.Hostname)) return d.Hostname.Split('.')[0];
        return Kinds.Text(d.Kind);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
