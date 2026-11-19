using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace MetroKafeLANViewer;

/// <summary>
/// Haritanın altındaki bir bölme. <see cref="Def"/> null ise bölmesi olmayan cihazların durduğu "Diğer cihazlar"dır
/// (silinemez, adı değişmez; boyutu ayarlanır).
/// </summary>
public sealed class GroupView : INotifyPropertyChanged
{
    public GroupDef? Def { get; init; }
    public string? Id => Def?.Id;
    public ObservableCollection<Tile> Tiles { get; } = [];

    public string Name => Def?.Name ?? "Diğer cihazlar";
    public string Title => $"{Name}  ({Tiles.Count})";
    public Geometry Icon => Icons.ByKey(Def?.Icon ?? "Klasor");
    public Brush IconBrush => Icons.BrushByKey(Def?.Icon ?? "Klasor");
    public Visibility ButtonsVisibility => Def == null ? Visibility.Collapsed : Visibility.Visible;
    public string Hint => Def == null ? "Bölmesi olmayanlar — sürükleyip bölmelere bırakın" : "";

    public double Width
    {
        get => _width;
        set { _width = value; Changed(nameof(Width)); }
    }

    public double Height
    {
        get => _height;
        set { _height = value; Changed(nameof(Height)); }
    }

    /// <summary>Üzerine cihaz sürüklenirken kenar vurgulanır.</summary>
    public bool DropTarget
    {
        get => _drop;
        set
        {
            if (_drop == value) return;
            _drop = value;
            Changed(nameof(DropTarget));
        }
    }

    /// <summary>Bölmeler alanındaki yeri (sol üst köşe, ızgaraya oturur).</summary>
    public double X
    {
        get => _x;
        set { _x = value; Changed(nameof(X)); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; Changed(nameof(Y)); }
    }

    /// <summary>Taşınan bölme ötekilerin üstünde çizilir.</summary>
    public int Z
    {
        get => _z;
        set { _z = value; Changed(nameof(Z)); }
    }

    public Rect Bounds => new(X, Y, Width, Height);

    double _width, _height, _x, _y;
    bool _drop;
    int _z;

    public void RefreshTitle() => Changed(nameof(Title));

    void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}
