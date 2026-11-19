using System.Windows;

namespace MetroKafeLANViewer;

public sealed record IconChoice(string Key, string Text)
{
    public System.Windows.Media.Geometry Icon => Icons.ByKey(Key);
    public System.Windows.Media.Brush Brush => Icons.BrushByKey(Key);
}

/// <summary>Bölme ekleme/düzenleme: ad ve simge.</summary>
public partial class GroupWindow : Window
{
    public string GroupName => NameBox.Text.Trim();
    public string IconKey => (IconList.SelectedItem as IconChoice)?.Key ?? "Klasor";

    public GroupWindow(string name, string icon)
    {
        InitializeComponent();
        Title = name.Length == 0 ? "Bölme ekle" : "Bölmeyi düzenle";
        NameBox.Text = name;
        var choices = Icons.GroupIcons.Select(i => new IconChoice(i.Key, i.Text)).ToList();
        IconList.ItemsSource = choices;
        IconList.SelectedItem = choices.FirstOrDefault(c => c.Key == icon) ?? choices[0];
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (GroupName.Length == 0)
        {
            Error.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
