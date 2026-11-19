using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MetroKafeLANViewer;

/// <summary>
/// Cihaz türü simgeleri: 24×24'lük alanda çizgi çizimler (dolgusuz, yuvarlak uçlu). Her türün kendi rengi vardır.
/// XAML'de <c>Viewbox &gt; Canvas 24×24 &gt; Path</c> ile kullanılır; kodda <see cref="Make"/> hazır öğe verir.
/// </summary>
public static class Icons
{
    static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // Oyun bilgisayarı ekranı
    static readonly Geometry Client = G("M4,3 H20 A2,2 0 0 1 22,5 V15 A2,2 0 0 1 20,17 H4 A2,2 0 0 1 2,15 V5 A2,2 0 0 1 4,3 Z M8,21 H16 M12,17 V21 M10,8 L14,10 L10,12 Z");

    // Ekran + kasa
    static readonly Geometry Computer = G("M3,5 H12 A1,1 0 0 1 13,6 V13 A1,1 0 0 1 12,14 H3 A1,1 0 0 1 2,13 V6 A1,1 0 0 1 3,5 Z M7.5,14 V18 M4.5,18 H10.5 "
        + "M17,3 H21 A1,1 0 0 1 22,4 V20 A1,1 0 0 1 21,21 H17 A1,1 0 0 1 16,20 V4 A1,1 0 0 1 17,3 Z M19,7 H19.01 M19,10 H19.01");

    // Duvara bağlı güvenlik kamerası
    static readonly Geometry Camera = G("M3,8 L15,4.5 L17,10.5 L5,14 Z M15.8,6.5 L20.5,5.5 V10.5 L17.4,10 M9,12.8 L10,17 H3.5 M3,14 V20 M7,9 H7.01");

    // Kablolu internet modemi: ışıklı kutu, arkadan telefon/fiber kablosu
    static readonly Geometry Modem = G("M5,8 H19 A2,2 0 0 1 21,10 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 V10 A2,2 0 0 1 5,8 Z "
        + "M7,15 H7.01 M10,15 H10.01 M13,15 H13.01 M16,15 H17.5 M7,11.5 H17 M6.5,19 V21 M17.5,19 V21 M12,8 V5 Q12,3 14,3 H17");

    // İki antenli Wi-Fi router ve yayın dalgaları
    static readonly Geometry Router = G("M4,14 H20 A2,2 0 0 1 22,16 V19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V16 A2,2 0 0 1 4,14 Z "
        + "M6,17.5 H6.01 M9,17.5 H9.01 M12,17.5 H12.01 M6,14 L4.5,6 M18,14 L19.5,6 "
        + "M9.9,10.5 A3,3 0 0 1 14.1,10.5 M8,8 A5.6,5.6 0 0 1 16,8 M6,5.5 A8.4,8.4 0 0 1 18,5.5");

    // Switch / erişim noktası: bağlantı noktaları sıralı kutu
    static readonly Geometry Switch = G("M3,8 H21 A1,1 0 0 1 22,9 V15 A1,1 0 0 1 21,16 H3 A1,1 0 0 1 2,15 V9 A1,1 0 0 1 3,8 Z "
        + "M5,12 H7 M9,12 H11 M13,12 H15 M17.5,11.5 H19.5 V13 H17.5 Z");

    static readonly Geometry Printer = G("M6,9 V2 H18 V9 M6,18 H4 A2,2 0 0 1 2,16 V11 A2,2 0 0 1 4,9 H20 A2,2 0 0 1 22,11 V16 A2,2 0 0 1 20,18 H18 M6,14 H18 V22 H6 Z");

    static readonly Geometry Phone = G("M7,2 H17 A2,2 0 0 1 19,4 V20 A2,2 0 0 1 17,22 H7 A2,2 0 0 1 5,20 V4 A2,2 0 0 1 7,2 Z M11,18.5 H13");

    static readonly Geometry Unknown = G("M12,2 A10,10 0 1 1 11.99,2 Z M9.1,9 A3,3 0 0 1 14.9,10 C14.9,12 12,12.5 12,14 M12,17.5 H12.01");

    public static Geometry Of(DeviceKind k) => k switch
    {
        DeviceKind.Istemci => Client,
        DeviceKind.Bilgisayar => Computer,
        DeviceKind.Kamera => Camera,
        DeviceKind.Modem => Modem,
        DeviceKind.Router => Router,
        DeviceKind.AgCihazi => Switch,
        DeviceKind.Yazici => Printer,
        DeviceKind.Mobil => Phone,
        _ => Unknown,
    };

    static readonly Dictionary<DeviceKind, Brush> Colors = new()
    {
        [DeviceKind.Istemci] = B("#4ADE80"),
        [DeviceKind.Bilgisayar] = B("#A78BFA"),
        [DeviceKind.Kamera] = B("#60A5FA"),
        [DeviceKind.Modem] = B("#FBBF24"),
        [DeviceKind.Router] = B("#2DD4BF"),
        [DeviceKind.AgCihazi] = B("#94A3B8"),
        [DeviceKind.Yazici] = B("#CBD5E1"),
        [DeviceKind.Mobil] = B("#F472B6"),
        [DeviceKind.Bilinmiyor] = B("#8A94A6"),
    };

    public static Brush BrushOf(DeviceKind k) => Colors[k];

    /// <summary>Kodda kullanmak için hazır simge.</summary>
    public static FrameworkElement Make(DeviceKind k, double size, Brush? brush = null) => new Viewbox
    {
        Width = size,
        Height = size,
        Child = new Canvas
        {
            Width = 24,
            Height = 24,
            Children =
            {
                new Path
                {
                    Data = Of(k),
                    Stroke = brush ?? BrushOf(k),
                    StrokeThickness = 1.8,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                },
            },
        },
    };

    /// <summary>Simge + yazı (menü ve açılır liste öğeleri için).</summary>
    public static FrameworkElement Label(DeviceKind k, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Make(k, 18);
        icon.Margin = new Thickness(0, 0, 9, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
