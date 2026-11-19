using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MetroKafeLANViewer;

public partial class App : Application
{
    const string MutexName = @"Local\MetroKafeLANViewer";
    const string ShowEventName = @"Local\MetroKafeLANViewer.Goster";

    Mutex? _mutex;
    EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--guncelle", StringComparer.OrdinalIgnoreCase))
        {
            UpdateFromCommandLine();
            return;
        }
        // Program zaten açıksa onu öne getirip çıkılır
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        Net.Oui.Preload();
        Updater.CleanupOldFiles();
        bool hidden = e.Args.Contains("--gizli", StringComparer.OrdinalIgnoreCase);
        var main = new MainWindow();
        MainWindow = main;
        if (!hidden) main.Show();

        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(main.ShowFromTray);
        }) { IsBackground = true };
        thread.Start();
    }

    /// <summary>
    /// "MetroKafeLANViewer.exe --guncelle": pencere açmadan yeni sürümü denetler ve kurar (zamanlanmış görev için).
    /// Sonuç veri\guncelleme.txt'ye yazılır.
    /// </summary>
    void UpdateFromCommandLine()
    {
        string result;
        try
        {
            var s = Store.LoadSettings();
            var info = Task.Run(() => Updater.CheckAsync(s)).GetAwaiter().GetResult();
            if (info == null)
            {
                result = $"Güncel (v{Updater.CurrentVersion.ToString(3)})";
            }
            else
            {
                Task.Run(() => Updater.InstallAsync(s, info, _ => { })).GetAwaiter().GetResult();
                result = $"v{info.Latest.ToString(3)} kuruldu";
            }
        }
        catch (Exception ex)
        {
            result = "Hata: " + ex.Message;
        }
        try
        {
            File.AppendAllText(Path.Combine(Store.Folder, "guncelleme.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{result}\r\n");
        }
        catch { }
        Shutdown();
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(Store.Folder, "hata.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{e.Exception}\r\n\r\n");
        }
        catch { }
        MessageBox.Show(e.Exception.Message, "Metro Kafe LAN Viewer — hata", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
