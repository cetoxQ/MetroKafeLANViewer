using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MetroKafeLANViewer;

/// <summary>
/// Haritadaki etkileşim: çoklu seçim (tık, Ctrl/Shift + tık, boş yerden sürükleyerek çerçeve), seçilenleri bölmelere
/// sürükleyip bırakma, bölme ekleme/düzenleme/silme/boyutlandırma, kutu boyutu ve otomatik tarama süresi.
/// </summary>
public partial class MainWindow
{
    const string DragFormat = "MetroKafeLANViewer.Cihazlar";

    /// <summary>Seçili cihazların anahtarları (aynı cihaz hem istemci ızgarasında hem bölmede görünebilir).</summary>
    readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    string? _anchorKey;
    (Tile Tile, Point At)? _press;
    Point? _bandStart;
    bool _bandAdditive, _suppressInterval;
    DispatcherTimer? _scaleSave;

    void InitMap()
    {
        // İç kaydırma alanları (bölmeler) fare basışını "işlendi" sayar; çerçeve seçimi oralarda da başlasın diye
        MapContent.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(Map_MouseDown), true);
        MapContent.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(Map_MouseUp), true);
        MapContent.MouseMove += Map_MouseMove;
        UpdateSelectionText();
    }

    // ---------------------------------------------------------------- tarama süresi

    void ShowInterval()
    {
        _suppressInterval = true;
        int s = _settings.IntervalSeconds;
        bool minutes = s >= 60 && s % 60 == 0;
        IntervalBox.Text = (minutes ? s / 60 : s).ToString();
        IntervalUnit.SelectedIndex = minutes ? 1 : 0;
        _suppressInterval = false;
    }

    void Interval_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _suppressInterval) return;
        if (!int.TryParse(IntervalBox.Text.Trim(), out int n) || n <= 0)
        {
            ShowInterval();
            return;
        }
        int seconds = Math.Clamp(n * (IntervalUnit.SelectedIndex == 1 ? 60 : 1), 15, 86400);
        if (seconds != n * (IntervalUnit.SelectedIndex == 1 ? 60 : 1))
        {
            // Sınır dışı değer düzeltildi
            _settings.IntervalSeconds = seconds;
            ShowInterval();
        }
        if (seconds == _settings.IntervalSeconds && _timer.Interval.TotalSeconds == seconds) return;
        _settings.IntervalSeconds = seconds;
        Store.SaveSettings(_settings);
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        if (_settings.AutoScan && _scan == null)
        {
            _timer.Stop();
            _timer.Start();
            ScanStatus.Text = (_lastScan != null ? $"Son tarama {_lastScan:HH:mm:ss} · " : "") + $"sonraki {DateTime.Now + _timer.Interval:HH:mm:ss}";
        }
    }

    void IntervalBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Interval_Changed(sender, e);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- kutu boyutu

    void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        var st = (ScaleTransform)FindResource("TileScale");
        st.ScaleX = st.ScaleY = e.NewValue;
        if (Math.Abs(_settings.TileScale - e.NewValue) < 0.001) return;
        _settings.TileScale = e.NewValue;
        // Çubuk sürüklenirken her adımda değil, bırakınca kaydedilir
        _scaleSave ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) =>
        {
            _scaleSave!.Stop();
            Store.SaveSettings(_settings);
        }, Dispatcher);
        _scaleSave.Stop();
        _scaleSave.Start();
    }

    // ---------------------------------------------------------------- bölmeler

    void BuildGroups()
    {
        _groups.Clear();
        _groups.Add(new GroupView { Width = _settings.OtherWidth, Height = _settings.OtherHeight });
        foreach (var def in _settings.Groups)
            _groups.Add(new GroupView { Def = def, Width = def.Width, Height = def.Height });
        FillGroups();
    }

    /// <summary>
    /// Bölmesi olan cihaz (açık ya da kapalı) kendi bölmesinde görünür; bölmesi olmayan, istemci aralığı dışındaki
    /// açık cihazlar "Diğer cihazlar"dadır.
    /// </summary>
    void FillGroups()
    {
        if (_groups.Count == 0) return;
        var byId = _groups.Where(g => g.Id != null).ToDictionary(g => g.Id!);
        var other = _groups[0];
        foreach (var g in _groups) g.Tiles.Clear();
        foreach (var d in _monitor.Devices.Where(d => d.IpNum != 0).OrderBy(d => d.IpNum))
        {
            GroupView? target = d.Group != null && byId.TryGetValue(d.Group, out var gv) ? gv : null;
            if (target == null)
            {
                if (!d.Online || d.InClientRange) continue;
                target = other;
            }
            var t = new Tile { Ip = d.IpNum };
            t.Set(d, null);
            t.Selected = _selected.Contains(d.Key);
            target.Tiles.Add(t);
        }
        foreach (var g in _groups) g.RefreshTitle();
    }

    void MoveToGroup(IEnumerable<Device> list, string? groupId)
    {
        foreach (var d in list) d.Group = groupId;
        _monitor.Save();
        FillGroups();
    }

    void GroupAdd_Click(object sender, RoutedEventArgs e) => AddGroup(null);

    void AddGroup(List<Device>? moveHere)
    {
        var w = new GroupWindow("", "Sunucu") { Owner = this };
        if (w.ShowDialog() != true) return;
        var def = new GroupDef { Name = w.GroupName, Icon = w.IconKey };
        _settings.Groups.Add(def);
        Store.SaveSettings(_settings);
        BuildGroups();
        if (moveHere != null) MoveToGroup(moveHere, def.Id);
    }

    static GroupView? GroupOf(object sender) => (sender as FrameworkElement)?.DataContext as GroupView;

    void GroupEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GroupOf(sender)?.Def is not { } def) return;
        var w = new GroupWindow(def.Name, def.Icon) { Owner = this };
        if (w.ShowDialog() != true) return;
        def.Name = w.GroupName;
        def.Icon = w.IconKey;
        Store.SaveSettings(_settings);
        BuildGroups();
    }

    void GroupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GroupOf(sender)?.Def is not { } def) return;
        if (MessageBox.Show(this, $"\"{def.Name}\" bölmesi silinsin mi? İçindeki cihazlar silinmez, Diğer cihazlar'a döner.",
                "Bölmeyi sil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.Groups.Remove(def);
        foreach (var d in _monitor.Devices.Where(d => d.Group == def.Id)) d.Group = null;
        Store.SaveSettings(_settings);
        _monitor.Save();
        BuildGroups();
    }

    void Group_Resize(object sender, DragDeltaEventArgs e)
    {
        if (GroupOf(sender) is not { } g) return;
        g.Width = Math.Max(220, g.Width + e.HorizontalChange);
        g.Height = Math.Max(110, g.Height + e.VerticalChange);
    }

    void Group_ResizeDone(object sender, DragCompletedEventArgs e)
    {
        if (GroupOf(sender) is not { } g) return;
        if (g.Def is { } def)
        {
            def.Width = Math.Round(g.Width);
            def.Height = Math.Round(g.Height);
        }
        else
        {
            _settings.OtherWidth = Math.Round(g.Width);
            _settings.OtherHeight = Math.Round(g.Height);
        }
        Store.SaveSettings(_settings);
    }

    void Group_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DragFormat);
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        if (ok && GroupOf(sender) is { } g)
        {
            foreach (var other in _groups) other.DropTarget = other == g;
        }
        e.Handled = true;
    }

    void Group_DragLeave(object sender, DragEventArgs e)
    {
        if (GroupOf(sender) is { } g) g.DropTarget = false;
    }

    void Group_Drop(object sender, DragEventArgs e)
    {
        foreach (var g in _groups) g.DropTarget = false;
        if (GroupOf(sender) is not { } target || e.Data.GetData(DragFormat) is not string[] keys) return;
        var set = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        MoveToGroup(_monitor.Devices.Where(d => set.Contains(d.Key)).ToList(), target.Id);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- seçim

    List<Device> SelectedDevices() => [.. _monitor.Devices.Where(d => _selected.Contains(d.Key)).OrderBy(d => d.IpNum)];

    void ApplySelection()
    {
        _selected.RemoveWhere(k => !_monitor.Devices.Any(d => string.Equals(d.Key, k, StringComparison.OrdinalIgnoreCase)));
        foreach (var t in _clientTiles.Concat(_groups.SelectMany(g => g.Tiles)))
            t.Selected = t.Device != null && _selected.Contains(t.Device.Key);
        UpdateSelectionText();
    }

    void ClearSelection()
    {
        _selected.Clear();
        _anchorKey = null;
        ApplySelection();
    }

    void UpdateSelectionText()
    {
        if (_selected.Count == 0)
        {
            SelectionText.Text = "Çoklu seçim: Ctrl / Shift + tık ya da boş yerden sürükleyin · seçilenleri bölmelere sürükleyin";
            SelectionText.Foreground = (Brush)FindResource("MutedBrush");
        }
        else
        {
            SelectionText.Text = $"{_selected.Count} cihaz seçili — sağ tık: toplu işlem · bölmeye sürükleyin · Esc: seçimi kaldır";
            SelectionText.Foreground = (Brush)FindResource("AccentBrush");
        }
    }

    /// <summary>Haritadaki kutular, ekrandaki sırayla (Shift ile aralık seçimi ve çerçeve seçimi için).</summary>
    List<Border> TileBorders()
    {
        var list = new List<Border>();
        void Walk(DependencyObject o)
        {
            int n = VisualTreeHelper.GetChildrenCount(o);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(o, i);
                if (c is Border { Tag: "tile", DataContext: Tile } b) list.Add(b);
                else Walk(c);
            }
        }
        Walk(MapContent);
        return list;
    }

    void ClickTile(Tile tile)
    {
        string? key = tile.Device?.Key;
        var mods = Keyboard.Modifiers;
        if (key == null)
        {
            if ((mods & ModifierKeys.Control) == 0) ClearSelection();
            return;
        }
        if ((mods & ModifierKeys.Control) != 0)
        {
            if (!_selected.Remove(key)) _selected.Add(key);
            _anchorKey = key;
        }
        else if ((mods & ModifierKeys.Shift) != 0 && _anchorKey != null)
        {
            var order = TileBorders().Select(b => ((Tile)b.DataContext).Device?.Key).ToList();
            int a = order.FindIndex(k => string.Equals(k, _anchorKey, StringComparison.OrdinalIgnoreCase));
            int b = order.IndexOf(key);
            if (a < 0 || b < 0)
            {
                _selected.Add(key);
            }
            else
            {
                _selected.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                    if (order[i] is { } k) _selected.Add(k);
            }
        }
        else
        {
            _selected.Clear();
            _selected.Add(key);
            _anchorKey = key;
        }
        ApplySelection();
    }

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Tile t }) return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _press = null;
            if (t.Device != null) EditDevice(t.Device);
            return;
        }
        _press = (t, e.GetPosition(MapContent));
        MapContent.CaptureMouse();
    }

    void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Tile t } el) return;
        ContextMenu menu;
        if (t.Device == null)
        {
            menu = BuildEmptyMenu(t.Ip);
        }
        else
        {
            // Seçili bir kutuya sağ tıklanırsa işlem bütün seçime uygulanır; seçili değilse yalnız o seçilir
            if (!_selected.Contains(t.Device.Key))
            {
                _selected.Clear();
                _selected.Add(t.Device.Key);
                _anchorKey = t.Device.Key;
                ApplySelection();
            }
            menu = BuildMenu(SelectedDevices());
        }
        menu.PlacementTarget = el;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Düğme, kaydırma çubuğu, boyut tutamağı gibi kendi işi olan öğelerde çerçeve seçimi başlamaz.</summary>
    bool IsControl(DependencyObject? o)
    {
        while (o != null && o != MapContent)
        {
            if (o is ButtonBase or Thumb or ScrollBar or TextBoxBase or ComboBox or Slider) return true;
            o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        }
        return false;
    }

    void Map_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_press != null || IsControl(e.OriginalSource as DependencyObject)) return;
        _bandStart = e.GetPosition(MapContent);
        _bandAdditive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        Canvas.SetLeft(SelectRect, _bandStart.Value.X);
        Canvas.SetTop(SelectRect, _bandStart.Value.Y);
        SelectRect.Width = SelectRect.Height = 0;
        MapContent.CaptureMouse();
        Focus();
    }

    void Map_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(MapContent);
        if (_press is { } p)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _press = null;
                return;
            }
            if ((pos - p.At).Length < 6 || p.Tile.Device == null) return;
            _press = null;
            MapContent.ReleaseMouseCapture();
            StartDrag(p.Tile.Device);
            return;
        }
        if (_bandStart is { } s)
        {
            var r = new Rect(s, pos);
            Canvas.SetLeft(SelectRect, r.X);
            Canvas.SetTop(SelectRect, r.Y);
            SelectRect.Width = r.Width;
            SelectRect.Height = r.Height;
            SelectRect.Visibility = Visibility.Visible;
        }
    }

    void Map_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_press is { } p)
        {
            _press = null;
            MapContent.ReleaseMouseCapture();
            ClickTile(p.Tile);
            return;
        }
        if (_bandStart is not { } s) return;
        _bandStart = null;
        MapContent.ReleaseMouseCapture();
        SelectRect.Visibility = Visibility.Collapsed;
        var r = new Rect(s, e.GetPosition(MapContent));
        if (r.Width < 4 && r.Height < 4)
        {
            // Boş yere tık: seçim kalkar
            if (!_bandAdditive) ClearSelection();
            return;
        }
        if (!_bandAdditive) _selected.Clear();
        foreach (var b in TileBorders())
        {
            if (b.DataContext is not Tile { Device: { } d } || !b.IsVisible) continue;
            var bounds = b.TransformToAncestor(MapContent).TransformBounds(new Rect(b.RenderSize));
            if (r.IntersectsWith(bounds)) _selected.Add(d.Key);
        }
        ApplySelection();
    }

    void StartDrag(Device d)
    {
        if (!_selected.Contains(d.Key))
        {
            _selected.Clear();
            _selected.Add(d.Key);
            _anchorKey = d.Key;
            ApplySelection();
        }
        DragDrop.DoDragDrop(MapContent, new DataObject(DragFormat, _selected.ToArray()), DragDropEffects.Move);
        foreach (var g in _groups) g.DropTarget = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (MapPanel.Visibility != Visibility.Visible || Keyboard.FocusedElement is TextBoxBase) return;
        if (e.Key == Key.Escape && _selected.Count > 0)
        {
            ClearSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            foreach (var b in TileBorders())
                if (b.DataContext is Tile { Device: { } d }) _selected.Add(d.Key);
            ApplySelection();
            e.Handled = true;
        }
    }

#if DEBUG
    public void DebugScale(double v)
    {
        ScaleSlider.Value = v;
        UpdateLayout();
        MapPanel.ScrollToEnd();
    }

    /// <summary>Yalnızca Debug: ekran görüntüsü testinde seçim görünümü için.</summary>
    public void DebugSelect(int count)
    {
        foreach (var t in _groups.SelectMany(g => g.Tiles).Where(t => t.Device != null).Take(count)) _selected.Add(t.Device!.Key);
        ApplySelection();
    }
#endif
}
