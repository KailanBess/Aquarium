using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aquarium;

public sealed class Preferences
{
    public string Accent { get; set; } = "Aquarium Mint";
    public string Background { get; set; } = "Ocean";
    public bool Compact { get; set; }
    public string WindowBorder { get; set; } = "System";
    public string TitleBar { get; set; } = "Accent";
    public string CardShape { get; set; } = "Rounded";
    public string TextSize { get; set; } = "Standard";
    public string Sidebar { get; set; } = "Standard";
}

// Separate from the catalog and library so appearance never rewrites app records.
public sealed class Appearance
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "appearance.json");
    private Preferences current = new();
    private Preferences? preview;
    private Preferences Active => preview ?? current;
    private bool writable = true;
    public string? Warning { get; private set; }
    public bool Compact => Active.Compact;
    public string TitleBar => Active.TitleBar;
    public double CardRadius => Active.CardShape switch { "Square" => 0, "Soft" => 28, _ => 16 };
    public double TitleSize => Active.TextSize switch { "Small" => 30, "Large" => 44, _ => 36 };
    public double SidebarWidth => Active.Sidebar switch { "Narrow" => 168, "Wide" => 280, _ => 224 };
    public void EndPreview() => preview = null;
    public void Load()
    {
        try
        {
            if (File.Exists(path)) current = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty preferences.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { writable = false; Warning = "Appearance settings could not be read and were preserved. " + ex.Message; }
    }
    private static readonly Dictionary<string, Windows.UI.Color> Palette = new()
    {
        ["Ocean"] = ColorHelper.FromArgb(255, 89, 190, 255),
        ["Sunset"] = ColorHelper.FromArgb(255, 255, 158, 79),
        ["Amethyst"] = ColorHelper.FromArgb(255, 177, 122, 255),
        ["Rose"] = ColorHelper.FromArgb(255, 228, 100, 181),
        ["Lime"] = ColorHelper.FromArgb(255, 155, 213, 98),
        ["Gold"] = ColorHelper.FromArgb(255, 236, 178, 62),
        ["Ice"] = ColorHelper.FromArgb(255, 103, 198, 232),
        ["Cobalt"] = ColorHelper.FromArgb(255, 77, 127, 255),
        ["Indigo"] = ColorHelper.FromArgb(255, 129, 114, 237),
        ["Violet"] = ColorHelper.FromArgb(255, 193, 86, 237),
        ["Orchid"] = ColorHelper.FromArgb(255, 225, 143, 234),
        ["Magenta"] = ColorHelper.FromArgb(255, 240, 92, 203),
        ["Ruby"] = ColorHelper.FromArgb(255, 240, 91, 112),
        ["Coral"] = ColorHelper.FromArgb(255, 255, 130, 115),
        ["Tangerine"] = ColorHelper.FromArgb(255, 255, 171, 50),
        ["Amber"] = ColorHelper.FromArgb(255, 240, 196, 70),
        ["Lemon"] = ColorHelper.FromArgb(255, 230, 223, 103),
        ["Emerald"] = ColorHelper.FromArgb(255, 67, 200, 134),
        ["Mint"] = ColorHelper.FromArgb(255, 131, 226, 188),
        ["Teal"] = ColorHelper.FromArgb(255, 59, 197, 186),
        ["Turquoise"] = ColorHelper.FromArgb(255, 64, 216, 228),
        ["Sapphire"] = ColorHelper.FromArgb(255, 72, 158, 221),
        ["Lavender"] = ColorHelper.FromArgb(255, 193, 166, 235),
        ["Peach"] = ColorHelper.FromArgb(255, 241, 177, 154),
        ["Copper"] = ColorHelper.FromArgb(255, 207, 145, 101),
        ["Silver"] = ColorHelper.FromArgb(255, 195, 203, 216),
        ["Pearl"] = ColorHelper.FromArgb(255, 245, 239, 228),
        ["Graphite"] = ColorHelper.FromArgb(255, 135, 149, 166),
        ["Aquarium Mint"] = ColorHelper.FromArgb(255, 103, 232, 211),
        ["Aquarium Blue"] = ColorHelper.FromArgb(255, 113, 190, 255),
        ["Aquarium Coral"] = ColorHelper.FromArgb(255, 255, 170, 150),
    };

    public Windows.UI.Color Accent => Palette.GetValueOrDefault(AccentName, Palette["Aquarium Mint"]);
    private string AccentName => Active.Accent == "Blue" ? "Aquarium Blue" : Active.Accent ?? "Aquarium Mint";
    public Windows.UI.Color? BorderColor => Active.WindowBorder switch
    {
        "Match accent" => Accent,
        string name when Palette.TryGetValue(name, out var color) => color,
        _ => null
    };
    public bool HideBorder => Active.WindowBorder == "Hidden";

    private (Windows.UI.Color Page, Windows.UI.Color Surface) Background => Active.Background switch
    {
        "Midnight" => (ColorHelper.FromArgb(255, 12, 14, 22), ColorHelper.FromArgb(255, 30, 32, 46)),
        "Slate" => (ColorHelper.FromArgb(255, 27, 35, 43), ColorHelper.FromArgb(255, 45, 56, 65)),
        "Kelp" => (ColorHelper.FromArgb(255, 12, 31, 26), ColorHelper.FromArgb(255, 23, 52, 43)),
        "Graphite" => (ColorHelper.FromArgb(255, 26, 26, 28), ColorHelper.FromArgb(255, 44, 44, 49)),
        "Reef" => (ColorHelper.FromArgb(255, 27, 20, 22), ColorHelper.FromArgb(255, 55, 33, 38)),
        "Plum" => (ColorHelper.FromArgb(255, 33, 21, 43), ColorHelper.FromArgb(255, 54, 37, 66)),
        _ => (ColorHelper.FromArgb(255, 9, 21, 29), ColorHelper.FromArgb(255, 16, 36, 47))
    };

    public SolidColorBrush PageBrush => new(Background.Page);
    public SolidColorBrush SurfaceBrush => new(Background.Surface);

    public StackPanel Editor(Action changed, Action<string> error)
    {
        var panel = new StackPanel { Spacing = 12 };
        var controls = new StackPanel { Spacing = 10 };
        panel.Children.Add(controls);

        ComboBox Choice(string header, IEnumerable<string> options, string? value)
        {
            var choices = options.ToArray();
            var box = new ComboBox
            {
                Header = header, ItemsSource = choices,
                SelectedItem = choices.Contains(value) ? value : choices[0]
            };
            controls.Children.Add(box);
            return box;
        }

        var accent = Choice("Accent", Palette.Keys, AccentName);
        var background = Choice("Background", ["Ocean", "Midnight", "Slate", "Kelp", "Plum", "Graphite", "Reef"], Active.Background);
        var border = Choice("Window border", new[] { "System", "Match accent", "Hidden" }.Concat(Palette.Keys), Active.WindowBorder);
        var titleBar = Choice("Window title bar", ["Accent", "Match background", "System"], Active.TitleBar);
        var corners = Choice("Card corners", ["Rounded", "Square", "Soft"], Active.CardShape);
        var textSize = Choice("Page heading size", ["Standard", "Small", "Large"], Active.TextSize);
        var sidebar = Choice("Sidebar width", ["Standard", "Narrow", "Wide"], Active.Sidebar);
        var compact = new ToggleSwitch { Header = "Compact collection spacing", IsOn = Active.Compact };
        controls.Children.Add(compact);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = "Apply appearance", IsEnabled = writable };
        var reset = new Button { Content = "Preview defaults" };
        var status = new TextBlock { Text = "Changes preview immediately. Apply appearance saves them." };
        actions.Children.Add(save);
        actions.Children.Add(reset);
        panel.Children.Add(actions);
        panel.Children.Add(status);

        Preferences ReadControls() => new()
        {
            Accent = (string)accent.SelectedItem, Background = (string)background.SelectedItem,
            WindowBorder = (string)border.SelectedItem, TitleBar = (string)titleBar.SelectedItem,
            CardShape = (string)corners.SelectedItem, TextSize = (string)textSize.SelectedItem,
            Sidebar = (string)sidebar.SelectedItem, Compact = compact.IsOn
        };
        void Preview()
        {
            preview = ReadControls();
            status.Text = "Previewing changes. Apply appearance to save.";
            changed();
        }
        foreach (var box in controls.Children.OfType<ComboBox>())
            box.SelectionChanged += (_, _) => Preview();
        compact.Toggled += (_, _) => Preview();
        reset.Click += (_, _) =>
        {
            var defaults = new Preferences();
            accent.SelectedItem = defaults.Accent; background.SelectedItem = defaults.Background;
            border.SelectedItem = defaults.WindowBorder; titleBar.SelectedItem = defaults.TitleBar;
            corners.SelectedItem = defaults.CardShape; textSize.SelectedItem = defaults.TextSize;
            sidebar.SelectedItem = defaults.Sidebar; compact.IsOn = defaults.Compact;
            Preview();
        };
        save.Click += (_, _) =>
        {
            controls.IsHitTestVisible = save.IsEnabled = reset.IsEnabled = false;
            var next = ReadControls();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next));
                File.Move(path + ".tmp", path, true);
                current = next;
                preview = null;
                status.Text = "Appearance saved.";
                changed();
            }
            catch (Exception ex) { error("Appearance was not saved: " + ex.Message); }
            finally
            {
                controls.IsHitTestVisible = reset.IsEnabled = true;
                save.IsEnabled = writable;
            }
        };
        if (Warning is not null) panel.Children.Add(new TextBlock { Text = Warning, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}
