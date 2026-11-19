using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace MetroKafeLANViewer;

/// <summary>
/// Bildirim alanı simgesi. Simgenin köşesindeki nokta ağın durumunu gösterir: yeşil sorun yok, kırmızı çakışma var.
/// Sol tık pencereyi açar; sağ tıkta koyu temalı bir menü çıkar.
/// </summary>
sealed class TrayIcon : IDisposable
{
    public enum Light { Gray, Green, Red }

    readonly Forms.NotifyIcon _icon;
    readonly Dictionary<Light, Icon> _icons = [];
    Light? _current;

    public event Action? ShowRequested;
    public event Action? ScanRequested;
    public event Action? ExitRequested;

    public TrayIcon(Stream ico)
    {
        var size = Forms.SystemInformation.SmallIconSize;
        using (var baseIcon = new Icon(ico, size))
        {
            foreach (var light in Enum.GetValues<Light>())
                _icons[light] = WithDot(baseIcon, size, light);
        }

        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()),
            BackColor = Color.FromArgb(0x15, 0x1C, 0x2A),
            ForeColor = Color.FromArgb(0xE5, 0xE7, 0xEB),
            Font = new Font("Segoe UI", 9.75f),
            ShowImageMargin = false,
            Padding = new Forms.Padding(4),
        };
        var show = new Forms.ToolStripMenuItem("Göster") { Font = new Font("Segoe UI", 9.75f, System.Drawing.FontStyle.Bold) };
        show.Click += (_, _) => ShowRequested?.Invoke();
        var scan = new Forms.ToolStripMenuItem("Şimdi tara");
        scan.Click += (_, _) => ScanRequested?.Invoke();
        var exit = new Forms.ToolStripMenuItem("Çıkış");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.AddRange([show, scan, new Forms.ToolStripSeparator(), exit]);
        foreach (Forms.ToolStripItem item in menu.Items) item.Padding = new Forms.Padding(6, 4, 6, 4);

        _icon = new Forms.NotifyIcon
        {
            Icon = _icons[Light.Gray],
            Text = "Metro Kafe LAN Viewer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke();
        };
        _icon.BalloonTipClicked += (_, _) => ShowRequested?.Invoke();
    }

    public void Update(Light light, string text)
    {
        if (_current != light)
        {
            _icon.Icon = _icons[light];
            _current = light;
        }
        // Windows en fazla 127 karakter gösterir
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void Notify(string title, string text, bool warning = false) =>
        _icon.ShowBalloonTip(8000, title, text.Length > 250 ? text[..250] : text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var i in _icons.Values) i.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    static Icon WithDot(Icon baseIcon, Size size, Light light)
    {
        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawIcon(baseIcon, new Rectangle(Point.Empty, size));
            var color = light switch
            {
                Light.Green => Color.FromArgb(0x22, 0xC5, 0x5E),
                Light.Red => Color.FromArgb(0xEF, 0x44, 0x44),
                _ => Color.FromArgb(0x8A, 0x94, 0xA6),
            };
            int d = Math.Max(6, size.Width * 9 / 16);
            var dot = new Rectangle(size.Width - d, size.Height - d, d - 1, d - 1);
            using var fill = new SolidBrush(color);
            using var edge = new Pen(Color.FromArgb(0x0B, 0x0F, 0x17), Math.Max(1f, size.Width / 16f));
            g.FillEllipse(fill, dot);
            g.DrawEllipse(edge, dot);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(h);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    sealed class DarkColors : Forms.ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(0x15, 0x1C, 0x2A);
        static readonly Color Hover = Color.FromArgb(0x26, 0x30, 0x43);
        static readonly Color Line = Color.FromArgb(0x26, 0x30, 0x43);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Bg;
    }
}
