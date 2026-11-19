#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MetroKafeLANViewer;

/// <summary>
/// Yalnızca Debug: MKL_SNAPSHOT=klasör verilirse ilk tarama bitince her sekmenin ekran görüntüsünü
/// PNG olarak kaydeder ve kapanır (ekransız arayüz denetimi için).
/// </summary>
static class DebugSnapshot
{
    public static string? Folder => Environment.GetEnvironmentVariable("MKL_SNAPSHOT");

    public static void Capture(MainWindow w, Action<int> selectTab, Action exit)
    {
        string dir = Folder!;
        Directory.CreateDirectory(dir);
        for (int tab = 0; tab < 3; tab++)
        {
            selectTab(tab);
            // DataGrid sütun genişliklerini ertelenmiş bir işte hesaplar; o işler de bitsin
            for (int i = 0; i < 5; i++)
            {
                w.UpdateLayout();
                w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            Save(w, Path.Combine(dir, $"sekme{tab}.png"));
        }
        // Simge örnekleri (büyük) ve bulunan türler
        var sheet = new System.Windows.Controls.WrapPanel { Background = (Brush)w.FindResource("BgBrush"), Width = 900 };
        foreach (var k in Kinds.All)
        {
            var label = Icons.Label(k, Kinds.Text(k));
            ((FrameworkElement)((System.Windows.Controls.StackPanel)label).Children[0]).Width = 56;
            ((FrameworkElement)((System.Windows.Controls.StackPanel)label).Children[0]).Height = 56;
            label.Margin = new Thickness(14);
            sheet.Children.Add(label);
        }
        sheet.Measure(new Size(900, double.PositiveInfinity));
        sheet.Arrange(new Rect(sheet.DesiredSize));
        Save(sheet, Path.Combine(dir, "simgeler.png"));
        File.WriteAllLines(Path.Combine(dir, "turler.txt"), w.AllDevices().Where(d => d.Online).OrderBy(d => d.IpNum).Select(d => $"{d.Ip}	{d.KindText}	{d.KindReason}"));

        // Düzenleme penceresi (tür listesi açık)
        if (w.FirstDevice(DeviceKind.Kamera) is { } cam)
        {
            var dw = new DeviceWindow(cam) { Owner = w, WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000 };
            dw.Show();
            dw.UpdateLayout();
            Save((FrameworkElement)dw.Content, Path.Combine(dir, "duzenle.png"));
            dw.Close();
        }
        exit();
    }

    public static void Save(FrameworkElement el, string file)
    {
        var dpi = VisualTreeHelper.GetDpi(el);
        var bmp = new RenderTargetBitmap((int)(el.ActualWidth * dpi.DpiScaleX), (int)(el.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
#endif
