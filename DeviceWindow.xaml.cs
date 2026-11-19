using System.Windows;

namespace MetroKafeLANViewer;

public sealed record KindOption(System.Windows.Media.Geometry? Icon, System.Windows.Media.Brush Brush, string Text)
{
    public Visibility IconVisibility => Icon == null ? Visibility.Collapsed : Visibility.Visible;
}

public partial class DeviceWindow : Window
{
    readonly Device _device;

    public DeviceWindow(Device d)
    {
        InitializeComponent();
        _device = d;
        Header.Text = string.IsNullOrEmpty(d.DisplayName) ? d.Ip : $"{d.DisplayName}  ·  {d.Ip}";
        var lines = new List<string>
        {
            $"MAC: {d.Mac ?? "—"}    Üretici: {d.Vendor ?? "bilinmiyor"}",
            $"Windows adı / DNS: {d.Hostname ?? "—"}" + (d.Workgroup != null ? $"  ({d.Workgroup})" : ""),
            $"Portlar: {(d.PortsText.Length > 0 ? d.PortsText : "—")}",
            $"İlk görülme: {d.FirstSeen:dd.MM.yyyy HH:mm}    Son görülme: {d.LastSeen:dd.MM.yyyy HH:mm}    {(d.Online ? "Şu an açık" : "Şu an kapalı")}",
        };
        Info.Text = string.Join("\n", lines);
        NameBox.Text = d.Name ?? "";
        NoteBox.Text = d.Note ?? "";

        KindBox.Items.Add(new KindOption(null, Icons.BrushOf(DeviceKind.Bilinmiyor),
            d.ManualKind == null ? $"Otomatik (şu an: {Kinds.Text(d.Kind)})" : "Otomatik bulsun"));
        foreach (var k in Kinds.All) KindBox.Items.Add(new KindOption(Icons.Of(k), Icons.BrushOf(k), Kinds.Text(k)));
        KindBox.SelectedIndex = d.ManualKind is { } m ? Array.IndexOf(Kinds.All, m) + 1 : 0;
        KindHint.Text = d.ManualKind == null ? $"Otomatik bulundu: {d.KindReason}" : "Elle seçildi.";
        Loaded += (_, _) => NameBox.Focus();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _device.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
        _device.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        var kind = KindBox.SelectedIndex <= 0 ? (DeviceKind?)null : Kinds.All[KindBox.SelectedIndex - 1];
        if (kind != _device.ManualKind)
        {
            _device.ManualKind = kind;
            if (kind != null && kind != DeviceKind.Istemci) _device.WasClient = false;
        }
        DialogResult = true;
    }
}
