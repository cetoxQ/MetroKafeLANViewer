using System.Windows;
using MetroKafeLANViewer.Net;
using Microsoft.Win32;

namespace MetroKafeLANViewer;

public partial class SettingsWindow : Window
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "MetroKafeLANViewer";

    readonly AppSettings _old;
    public AppSettings Result { get; private set; }
    public event Action? CheckUpdateRequested;

    public SettingsWindow(AppSettings s)
    {
        InitializeComponent();
        _old = s;
        Result = s;
        ScanBox.Text = s.ScanRanges;
        ClientBox.Text = s.ClientRanges;
        PatternBox.Text = s.ClientNamePattern;
        IntervalBox.Text = s.IntervalSeconds.ToString();
        PingBox.Text = s.PingTimeoutMs.ToString();
        SoundBox.IsChecked = s.Sound;
        NotifyBox.IsChecked = s.Notify;
        TrayBox.IsChecked = s.CloseToTray;
        StartupBox.IsChecked = Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunName) != null;
        UpdateBox.IsChecked = s.CheckUpdates;
        TokenHint.Text = s.Token != null
            ? "Anahtar girilmiş. Değiştirmek için yenisini yazın; silmek için \"-\" yazın."
            : $"Depo: {s.Repo}. \"Anahtar al\" → Repository access: Only select repositories → {s.Repo.Split('/')[^1]}, "
              + "Permissions → Contents: Read-only → Generate token. Çıkan anahtarı buraya yapıştırın. Depo herkese açılınca gerekmez.";
        FolderText.Text = "Kayıtlar: " + Store.Folder;
        FolderText.ToolTip = Store.Folder;
    }

    bool TryBuild(out AppSettings s)
    {
        s = new AppSettings
        {
            ScanRanges = ScanBox.Text.Trim(),
            ClientRanges = ClientBox.Text.Trim(),
            ClientNamePattern = PatternBox.Text.Trim(),
            Sound = SoundBox.IsChecked == true,
            Notify = NotifyBox.IsChecked == true,
            CloseToTray = TrayBox.IsChecked == true,
            CheckUpdates = UpdateBox.IsChecked == true,
            AutoScan = _old.AutoScan,
            Repo = _old.Repo,
            Token = _old.Token,
            Tab = _old.Tab,
            TileScale = _old.TileScale,
            Groups = _old.Groups,
            OtherWidth = _old.OtherWidth,
            OtherHeight = _old.OtherHeight,
            OtherX = _old.OtherX,
            OtherY = _old.OtherY,
        };
        string? error = null;
        if (!IpRange.TryParse(s.ScanRanges, out _, out var e1)) error = "Tarama aralığı: " + e1;
        else if (!IpRange.TryParse(s.ClientRanges, out _, out var e2)) error = "İstemci aralığı: " + e2;
        else if (s.ClientNamePattern.Length > 0 && Monitor.TryRegex(s.ClientNamePattern) == null) error = "Ad kalıbı geçerli bir düzenli ifade değil.";
        else if (!int.TryParse(IntervalBox.Text, out int interval) || interval < 15 || interval > 3600) error = "Tarama aralığı 15 ile 3600 saniye arasında olmalı.";
        else if (!int.TryParse(PingBox.Text, out int ping) || ping < 200 || ping > 5000) error = "Ping süresi 200 ile 5000 ms arasında olmalı.";
        else
        {
            s.IntervalSeconds = interval;
            s.PingTimeoutMs = ping;
        }

        string token = TokenBox.Password.Trim();
        if (token == "-") s.Token = null;
        else if (token.Length > 0) s.Token = Store.Protect(token);

        Error.Text = error ?? "";
        Error.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        return error == null;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuild(out var s)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (StartupBox.IsChecked == true) key.SetValue(RunName, $"\"{Updater.ExePath}\" --gizli");
            else key.DeleteValue(RunName, false);
        }
        catch { }
        Result = s;
        DialogResult = true;
    }

    void GetToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/settings/personal-access-tokens/new") { UseShellExecute = true });
        }
        catch { }
    }

    void Check_Click(object sender, RoutedEventArgs e)
    {
        // Yeni yazılan anahtarla denetlensin diye önce ayarlar kaydedilir
        if (!TryBuild(out var s)) return;
        Store.SaveSettings(s);
        _old.Token = s.Token;
        CheckUpdateRequested?.Invoke();
    }
}
