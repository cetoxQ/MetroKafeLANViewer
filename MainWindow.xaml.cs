using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using MetroKafeLANViewer.Net;

namespace MetroKafeLANViewer;

public partial class MainWindow : Window
{
    readonly Monitor _monitor = new();
    readonly ICollectionView _view;
    readonly DispatcherTimer _timer = new();
    readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    readonly ObservableCollection<Tile> _clientTiles = [];
    readonly ObservableCollection<GroupView> _groups = [];
    readonly Dictionary<uint, Tile> _tileByIp = [];
    readonly TrayIcon _tray;
    AppSettings _settings = Store.LoadSettings();
    CancellationTokenSource? _scan;
    DateTime? _lastScan;
    UpdateInfo? _update;
    bool _exiting;
    int _seenEvents;

    public MainWindow()
    {
        InitializeComponent();
        SetUpdateStatus(_settings.CheckUpdates ? "denetleniyor..." : "otomatik denetim kapalı", false);

        _view = CollectionViewSource.GetDefaultView(_monitor.Devices);
        _view.Filter = Matches;
        _view.SortDescriptions.Add(new SortDescription(nameof(Device.IpNum), ListSortDirection.Ascending));
        if (_view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            // Açık/kapalı ve çakışma süzgeçleri tarama sırasında kendiliğinden güncellensin
            live.IsLiveFiltering = true;
            foreach (var p in new[] { nameof(Device.Online), nameof(Device.Conflict), nameof(Device.Kind) }) live.LiveFilteringProperties.Add(p);
        }
        DeviceGrid.ItemsSource = _view;
        EventsList.ItemsSource = _monitor.Events;
        ClientTilesView.ItemsSource = _clientTiles;
        GroupsView.ItemsSource = _groups;
        _monitor.NewConflicts += OnNewConflicts;
        foreach (var k in new[] { DeviceKind.Istemci, DeviceKind.Bilgisayar, DeviceKind.Kamera, DeviceKind.Modem, DeviceKind.Router,
                     DeviceKind.AgCihazi, DeviceKind.Mobil, DeviceKind.Yazici, DeviceKind.Bilinmiyor })
        {
            var label = Icons.Label(k, Kinds.Text(k));
            label.Margin = new Thickness(0, 0, 18, 0);
            ((TextBlock)((StackPanel)label).Children[1]).Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            ((TextBlock)((StackPanel)label).Children[1]).FontSize = 12;
            IconLegend.Children.Add(label);
        }
        _monitor.Events.CollectionChanged += (_, _) => UpdateEventsTab();
        InitMap();

        _tray = new TrayIcon(Application.GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream);
        _tray.ShowRequested += ShowFromTray;
        _tray.ScanRequested += () => StartScan();
        _tray.ExitRequested += ExitApp;

        _timer.Tick += (_, _) => StartScan();
        _updateTimer.Tick += async (_, _) => await CheckUpdateAsync(silent: true);

        ApplySettings();
        Loaded += (_, _) => ((RadioButton)(_settings.Tab switch { 1 => TabList, 2 => TabEvents, _ => TabMap })).IsChecked = true;

        _monitor.Log(EventLevel.Bilgi, $"Program açıldı (v{Updater.CurrentVersion.ToString(3)}). Tarama: {_settings.ScanRanges}, istemci aralığı: {_settings.ClientRanges}");
        _seenEvents = _monitor.Events.Count;
        Dispatcher.BeginInvoke(() => StartScan(), DispatcherPriority.ApplicationIdle);
        if (_settings.CheckUpdates)
        {
            var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            first.Tick += async (_, _) => { first.Stop(); await CheckUpdateAsync(silent: true); };
            first.Start();
            _updateTimer.Start();
        }
    }

    void ApplySettings()
    {
        _monitor.Configure(_settings);
        AutoBox.IsChecked = _settings.AutoScan;
        ShowInterval();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(15, _settings.IntervalSeconds));
        ScaleSlider.Value = Math.Clamp(_settings.TileScale, ScaleSlider.Minimum, ScaleSlider.Maximum);
        BuildTiles();
        BuildGroups();
        RefreshAll();
    }

    // ---------------------------------------------------------------- tarama

    async void StartScan()
    {
        if (_scan != null) return;
        _timer.Stop();
        if (!IpRange.TryParse(_settings.ScanRanges, out var ranges, out string? error))
        {
            ScanStatus.Text = "Tarama aralığı hatalı: " + error;
            return;
        }
        var ips = IpRange.Enumerate(ranges.Concat(_monitor.ClientRanges)).ToList();
        _scan = new CancellationTokenSource();
        var token = _scan.Token;
        ScanButtonText.Text = "Durdur";
        var started = DateTime.Now;
        int lastShown = -1;

        var scanner = new Scanner { PingTimeoutMs = Math.Clamp(_settings.PingTimeoutMs, 200, 5000) };
        try
        {
            await scanner.ScanAsync(ips,
                r => Dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _monitor.Apply(r, started);
                    UpdateTileFor(r.Ip);
                }),
                done => Dispatcher.BeginInvoke(() =>
                {
                    // Her sonuçta değil, yüzde değişince yazılır
                    int p = done * 100 / ips.Count;
                    if (p == lastShown) return;
                    lastShown = p;
                    ScanStatus.Text = $"Taranıyor... %{p} ({done}/{ips.Count})";
                }),
                token);
            // Kuyruktaki son sonuçlar da işlensin
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        finally
        {
            bool cancelled = token.IsCancellationRequested;
            _scan.Dispose();
            _scan = null;
            ScanButtonText.Text = "Şimdi tara";
            if (!cancelled)
            {
                _lastScan = DateTime.Now;
                _monitor.FinishScan();
            }
            RefreshAll();
            ScanStatus.Text = cancelled ? "Tarama durduruldu" : $"Son tarama {_lastScan:HH:mm:ss} ({(DateTime.Now - started).TotalSeconds:0} sn)"
                + (_settings.AutoScan ? $" · sonraki {DateTime.Now + _timer.Interval:HH:mm:ss}" : "");
            if (_settings.AutoScan) _timer.Start();
#if DEBUG
            if (!cancelled && DebugSnapshot.Folder != null)
                DebugSnapshot.Capture(this, tab => ((RadioButton)(tab switch { 1 => TabList, 2 => TabEvents, _ => TabMap })).IsChecked = true, ExitApp);
#endif
        }
    }

    void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scan != null) _scan.Cancel();
        else StartScan();
    }

    void AutoBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoScan = AutoBox.IsChecked == true;
        Store.SaveSettings(_settings);
        if (_settings.AutoScan && _scan == null) _timer.Start();
        else _timer.Stop();
    }

    void OnNewConflicts(IReadOnlyList<Device> list)
    {
        if (_settings.Sound) SystemSounds.Exclamation.Play();
        if (_settings.Notify)
        {
            string text = string.Join("\n", list.Take(4).Select(d => $"{d.Ip}: {Kinds.Text(d.Kind)} {d.DisplayName ?? d.Vendor}".Trim()));
            if (list.Count > 4) text += $"\n... ve {list.Count - 4} cihaz daha";
            _tray.Notify($"{list.Count} cihaz istemci IP'si aldı", text, warning: true);
        }
        // Pencere açıksa görev çubuğunda yanıp sönsün
        if (IsVisible && !IsActive) FlashWindow.Flash(this);
    }

    // ---------------------------------------------------------------- görünüm

    void BuildTiles()
    {
        _clientTiles.Clear();
        _tileByIp.Clear();
        foreach (uint ip in IpRange.Enumerate(_monitor.ClientRanges))
        {
            var t = new Tile { Ip = ip };
            _clientTiles.Add(t);
            _tileByIp[ip] = t;
        }
        var ranges = _monitor.ClientRanges;
        ClientHeader.Text = ranges.Count == 0 ? "İstemci aralığı ayarlanmamış (Ayarlar)"
            : "İstemci aralığı: " + string.Join(", ", ranges.Select(r => $"{IpRange.FromUInt(r.From)} – {r.To & 0xFF}"));
    }

    void UpdateTileFor(uint ip)
    {
        if (_tileByIp.TryGetValue(ip, out var t))
        {
            t.Set(_monitor.Devices.FirstOrDefault(d => d.Online && d.IpNum == ip), _monitor.ClientOwnerOf(ip));
            t.Selected = t.Device != null && _selected.Contains(t.Device.Key);
        }
        UpdateSummary();
    }

    void RefreshAll()
    {
        var online = _monitor.Devices.Where(d => d.Online).GroupBy(d => d.IpNum).ToDictionary(g => g.Key, g => g.First());
        foreach (var t in _clientTiles)
        {
            t.Set(online.GetValueOrDefault(t.Ip), _monitor.ClientOwnerOf(t.Ip));
            t.Selected = t.Device != null && _selected.Contains(t.Device.Key);
        }
        FillGroups();
        _view.Refresh();
        UpdateSummary();
    }

    void UpdateSummary()
    {
        var online = _monitor.Devices.Where(d => d.Online).ToList();
        int clients = online.Count(d => d.Kind == DeviceKind.Istemci);
        int cameras = online.Count(d => d.Kind == DeviceKind.Kamera);
        var conflicts = online.Where(d => d.Conflict).OrderBy(d => d.IpNum).ToList();
        Summary.Text = $"Açık: {online.Count}   ·   İstemci: {clients}   ·   Kamera: {cameras}   ·   Diğer: {online.Count - clients - cameras}"
            + (conflicts.Count > 0 ? $"   ·   ÇAKIŞMA: {conflicts.Count}" : "");
        Summary.Foreground = conflicts.Count > 0 ? (System.Windows.Media.Brush)FindResource("DangerBrush") : (System.Windows.Media.Brush)FindResource("TextBrush");

        if (conflicts.Count > 0)
        {
            ConflictBanner.Visibility = Visibility.Visible;
            ConflictTitle.Text = $"⚠  {conflicts.Count} cihaz istemci IP aralığında — bu IP'lerdeki bilgisayarlar ağdan açılamaz";
            ConflictLines.Text = string.Join("\n", conflicts.Take(6).Select(d =>
                $"{d.Ip}  →  {(string.IsNullOrEmpty(d.DisplayName) ? d.Vendor ?? "adı yok" : d.DisplayName)}  ({d.Mac})  —  {d.ConflictText}"))
                + (conflicts.Count > 6 ? $"\n... ve {conflicts.Count - 6} cihaz daha" : "");
        }
        else
        {
            ConflictBanner.Visibility = Visibility.Collapsed;
        }

        var light = _lastScan == null ? TrayIcon.Light.Gray : conflicts.Count > 0 ? TrayIcon.Light.Red : TrayIcon.Light.Green;
        _tray.Update(light, conflicts.Count > 0
            ? $"LAN Viewer: {conflicts.Count} ÇAKIŞMA\n" + string.Join("\n", conflicts.Take(3).Select(d => $"{d.Ip} {Kinds.Text(d.Kind)}"))
            : $"LAN Viewer: sorun yok\nAçık {online.Count}, istemci {clients}, kamera {cameras}");
    }

    void UpdateEventsTab()
    {
        int unseen = TabEvents.IsChecked == true ? 0 : Math.Max(0, _monitor.Events.Count - _seenEvents);
        EventsTabText.Text = unseen > 0 ? $"Olaylar ({unseen})" : "Olaylar";
        if (TabEvents.IsChecked == true) _seenEvents = _monitor.Events.Count;
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (MapPanel == null) return;
        MapPanel.Visibility = TabMap.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ListPanel.Visibility = TabList.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        EventsPanel.Visibility = TabEvents.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        int tab = TabList.IsChecked == true ? 1 : TabEvents.IsChecked == true ? 2 : 0;
        if (tab == 2) _seenEvents = _monitor.Events.Count;
        UpdateEventsTab();
        if (_settings.Tab != tab)
        {
            _settings.Tab = tab;
            Store.SaveSettings(_settings);
        }
    }

    // ---------------------------------------------------------------- liste süzgeci

    bool Matches(object o)
    {
        if (o is not Device d) return false;
        bool chip = FConflict.IsChecked == true ? d.Conflict
            : FCamera.IsChecked == true ? d.Kind == DeviceKind.Kamera
            : FClient.IsChecked == true ? d.Kind == DeviceKind.Istemci
            : FOther.IsChecked == true ? d.Kind is not (DeviceKind.Istemci or DeviceKind.Kamera)
            : FOnline.IsChecked == true ? d.Online
            : FOffline.IsChecked != true || !d.Online;
        if (!chip) return false;
        string q = SearchBox?.Text.Trim() ?? "";
        if (q.Length == 0) return true;
        return new[] { d.Ip, d.DisplayName, d.Mac, d.Vendor, d.Note, d.KindText, d.PortsText, d.Hostname }
            .Any(s => s != null && s.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    void Filter_Checked(object sender, RoutedEventArgs e) => _view?.Refresh();
    void Search_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    void ShowConflicts_Click(object sender, RoutedEventArgs e)
    {
        TabList.IsChecked = true;
        FConflict.IsChecked = true;
    }

    // ---------------------------------------------------------------- cihaz işlemleri

    void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindRow() is { Item: Device d }) EditDevice(d);
    }

    void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject)?.FindRow();
        if (row == null)
        {
            e.Handled = true;
            return;
        }
        if (!row.IsSelected)
        {
            DeviceGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
        var selected = DeviceGrid.SelectedItems.OfType<Device>().ToList();
        var menu = BuildMenu(selected);
        DeviceGrid.ContextMenu = menu;
    }

    ContextMenu BuildMenu(List<Device> list)
    {
        var menu = new ContextMenu();
        var d = list[0];
        void Add(string text, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = text, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (list.Count == 1)
        {
            Add("Düzenle (ad, tür, not)...", () => EditDevice(d));
            menu.Items.Add(new Separator());
            Add("Web arayüzünü aç", () => Open($"http://{d.Ip}" + (d.OpenPorts.Contains(80) || !d.OpenPorts.Contains(8080) ? "" : ":8080")), d.Online);
            Add("Sürekli ping (komut penceresi)", () => Open("cmd.exe", $"/k ping -t {d.Ip}"), d.IpNum != 0);
            Add("Paylaşılan klasörler", () => Open($@"\\{d.Ip}"), d.Online && d.OpenPorts.Contains(445));
            Add("Uzak masaüstü", () => Open("mstsc.exe", $"/v:{d.Ip}"), d.Online && d.OpenPorts.Contains(3389));
            Add("Yeniden tara", async () => await RescanAsync(d), d.IpNum != 0 && _scan == null);
            menu.Items.Add(new Separator());
            Add("IP'yi kopyala", () => Clipboard.SetText(d.Ip), d.IpNum != 0);
            Add("MAC'i kopyala", () => Clipboard.SetText(d.Mac ?? ""), d.Mac != null);
        }
        Add(list.Count == 1 ? "Uyandır (Wake-on-LAN)" : $"{list.Count} cihazı uyandır (Wake-on-LAN)", async () =>
        {
            foreach (var x in list.Where(x => x.Mac != null)) await Probes.WakeAsync(x.Mac!);
        }, list.Any(x => x.Mac != null));
        menu.Items.Add(new Separator());
        foreach (var kind in new[] { DeviceKind.Istemci, DeviceKind.Kamera, DeviceKind.Modem, DeviceKind.Router, DeviceKind.Mobil, DeviceKind.Bilgisayar })
        {
            var item = new MenuItem { Header = Icons.Label(kind, $"Türü \"{Kinds.Text(kind)}\" yap") };
            item.Click += (_, _) => SetKind(list, kind);
            menu.Items.Add(item);
        }
        Add("Türü otomatik bulsun", () => SetKind(list, null), list.Any(x => x.ManualKind != null));
        bool ignore = !d.IgnoreConflict;
        Add(ignore ? "Bu cihaz için uyarma (bilerek orada, ör. sabit IP)" : "Bu cihaz için yine uyar", () =>
        {
            foreach (var x in list)
            {
                x.IgnoreConflict = ignore;
                _monitor.Evaluate(x);
            }
            _monitor.FinishScan();
            RefreshAll();
        });
        menu.Items.Add(new Separator());
        foreach (var g in _settings.Groups)
        {
            var item = new MenuItem { Header = Icons.Label(g.Icon, $"\"{g.Name}\" bölmesine taşı") };
            item.Click += (_, _) => MoveToGroup(list, g.Id);
            menu.Items.Add(item);
        }
        Add("Yeni bölmeye taşı...", () => AddGroup(list));
        if (list.Any(x => x.Group != null)) Add("Bölmeden çıkar (Diğer cihazlar'a)", () => MoveToGroup(list, null));
        if (_selected.Count > 0) Add("Seçimi temizle", ClearSelection);
        menu.Items.Add(new Separator());
        Add(list.Count == 1 ? "Kayıttan sil" : $"{list.Count} cihazı kayıttan sil", () =>
        {
            if (MessageBox.Show(this, $"{list.Count} cihaz kayıtlardan silinsin mi? (Ad ve notları da silinir; ağda görülürse yeniden eklenir.)",
                    "Kayıttan sil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var x in list) _monitor.Remove(x);
            RefreshAll();
        });
        return menu;
    }

    ContextMenu BuildEmptyMenu(uint ip)
    {
        var menu = new ContextMenu();
        var item = new MenuItem { Header = "Sürekli ping (komut penceresi)" };
        item.Click += (_, _) => Open("cmd.exe", $"/k ping -t {IpRange.FromUInt(ip)}");
        menu.Items.Add(item);
        if (_monitor.ClientOwnerOf(ip) is { Mac: { } mac } owner)
        {
            var wake = new MenuItem { Header = $"{owner.DisplayName} bilgisayarını uyandır" };
            wake.Click += async (_, _) => await Probes.WakeAsync(mac);
            menu.Items.Add(wake);
        }
        return menu;
    }

    void SetKind(List<Device> list, DeviceKind? kind)
    {
        foreach (var d in list)
        {
            d.ManualKind = kind;
            // "İstemci değil" denen cihaz bir daha kendiliğinden istemci sayılmasın
            if (kind != null && kind != DeviceKind.Istemci) d.WasClient = false;
            _monitor.Evaluate(d);
        }
        _monitor.FinishScan();
        RefreshAll();
    }

    void EditDevice(Device d)
    {
        var w = new DeviceWindow(d) { Owner = this };
        if (w.ShowDialog() != true) return;
        _monitor.Evaluate(d);
        _monitor.FinishScan();
        RefreshAll();
    }

    async Task RescanAsync(Device d)
    {
        ScanStatus.Text = $"{d.Ip} taranıyor...";
        var r = await new Scanner { PingTimeoutMs = _settings.PingTimeoutMs }.ProbeAsync(d.IpNum);
        _monitor.Apply(r, DateTime.Now);
        _monitor.FinishScan();
        RefreshAll();
        ScanStatus.Text = $"{d.Ip} tarandı: {(r.Alive ? "açık" : "yanıt yok")}";
    }

    static void Open(string file, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Açılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Store.LogFolder);
        Open(Store.LogFolder);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (Excel)|*.csv",
            FileName = $"ag-cihazlari-{DateTime.Now:yyyy-MM-dd-HHmm}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;
        static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Durum;IP;Ad;Tür;MAC;Üretici;Ping;Portlar;İlk görülme;Son görülme;Not;Uyarı\r\n");
        foreach (var d in _view.Cast<Device>())
            sb.AppendLine(string.Join(";", Q(d.StatusText), Q(d.Ip), Q(d.DisplayName), Q(d.KindText), Q(d.Mac), Q(d.Vendor), Q(d.PingText),
                Q(d.PortsText), Q(d.FirstSeen.ToString("dd.MM.yyyy HH:mm")), Q(d.LastSeen.ToString("dd.MM.yyyy HH:mm")), Q(d.Note), Q(d.ConflictText)));
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    // ---------------------------------------------------------------- ayarlar ve güncelleme

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_settings) { Owner = this };
        w.CheckUpdateRequested += async () => await CheckUpdateAsync(silent: false);
        if (w.ShowDialog() != true) return;
        _settings = w.Result;
        Store.SaveSettings(_settings);
        if (_settings.CheckUpdates) _updateTimer.Start(); else _updateTimer.Stop();
        ApplySettings();
        if (_scan == null)
        {
            _timer.Stop();
            StartScan();
        }
    }

    /// <summary>Alt çubuktaki "Sürüm: v1.0.3 · güncel" yazısı; sorun varsa turuncu.</summary>
    void SetUpdateStatus(string status, bool warning)
    {
        VersionText.Text = $"Sürüm: v{Updater.CurrentVersion.ToString(3)} · {status}";
        VersionText.Foreground = (System.Windows.Media.Brush)FindResource(warning ? "WarnBrush" : "MutedBrush");
    }

    async void VersionText_Click(object sender, MouseButtonEventArgs e)
    {
        // Anahtar eksikse doğrudan ayarlara götürülür
        if (_settings.Token == null && _updateError != null) Settings_Click(this, new RoutedEventArgs());
        else await CheckUpdateAsync(silent: false);
    }

    string? _updateError;

    async Task CheckUpdateAsync(bool silent)
    {
        SetUpdateStatus("denetleniyor...", false);
        try
        {
            _update = await Updater.CheckAsync(_settings);
            _updateError = null;
            if (_update == null)
            {
                SetUpdateStatus($"güncel ({DateTime.Now:HH:mm})", false);
                UpdateBanner.Visibility = Visibility.Collapsed;
                if (!silent) MessageBox.Show(this, $"Program güncel (v{Updater.CurrentVersion.ToString(3)}).", "Güncelleme", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            UpdateText.Text = $"Yeni sürüm hazır: v{_update.Latest.ToString(3)} (şu an v{Updater.CurrentVersion.ToString(3)})"
                + (string.IsNullOrWhiteSpace(_update.Notes) ? "" : " — " + _update.Notes.Trim().Replace("\r", "").Replace("\n", " · "));
            UpdateButton.IsEnabled = true;
            UpdateBanner.Visibility = Visibility.Visible;
            SetUpdateStatus($"yeni sürüm var: v{_update.Latest.ToString(3)}", true);
            if (silent && !IsVisible) _tray.Notify("Yeni sürüm hazır", $"Metro Kafe LAN Viewer v{_update.Latest.ToString(3)}");
            if (!silent) UpdateNow_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            _updateError = ex.Message;
            SetUpdateStatus(_settings.Token == null
                ? "güncelleme denetlenemedi: GitHub anahtarı gerekli (tıklayın)"
                : "güncelleme denetlenemedi (tıklayın)", true);
            VersionText.ToolTip = ex.Message;
            if (!silent) MessageBox.Show(this, ex.Message, "Güncelleme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        if (MessageBox.Show(this, $"v{_update.Latest.ToString(3)} kurulsun mu? Program kapanıp yeniden açılacak.", "Güncelleme",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        UpdateButton.IsEnabled = false;
        try
        {
            await Updater.InstallAsync(_settings, _update, p => Dispatcher.Invoke(() => UpdateText.Text = p));
            _monitor.Save();
            // Yeni exe, bu süreç kapanınca tek-kopya kilidine takılmasın diye biraz bekleyip açılır
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & start \"\" \"{Updater.ExePath}\"")
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            });
            ExitApp();
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Güncelleme başarısız: " + ex.Message;
            UpdateButton.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- pencere

#if DEBUG
    public Device? FirstDevice(DeviceKind k) => _monitor.Devices.FirstOrDefault(d => d.Online && d.Kind == k);
    public IEnumerable<Device> AllDevices() => _monitor.Devices;
#endif

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _settings.CloseToTray)
        {
            // Kapatınca bildirim alanında taramaya devam eder
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_exiting) ExitApp();
    }

    void ExitApp()
    {
        _exiting = true;
        _scan?.Cancel();
        _monitor.Save();
        _tray.Dispose();
        Application.Current.Shutdown();
    }
}

static class TreeHelpers
{
    public static DataGridRow? FindRow(this DependencyObject? d)
    {
        while (d != null && d is not DataGridRow)
            d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as DataGridRow;
    }
}

static class FlashWindow
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FLASHWINFO info);

    public static void Flash(Window w)
    {
        var info = new FLASHWINFO
        {
            hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle,
            dwFlags = 3 | 12, // başlık + görev çubuğu, pencereye dönülene kadar
            uCount = uint.MaxValue,
        };
        info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);
        FlashWindowEx(ref info);
    }
}
