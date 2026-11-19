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
