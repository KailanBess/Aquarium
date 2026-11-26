namespace Aquarium;

public sealed class Preferences
{
    public string Accent { get; set; } = "Aquarium Mint";
    public string AccentHex { get; set; } = "";
    public string Background { get; set; } = "Ocean";
    public bool Compact { get; set; }
    public string WindowBorder { get; set; } = "System";
    public string TitleBar { get; set; } = "Accent";
    public string CardShape { get; set; } = "Rounded";
    public string TextSize { get; set; } = "Standard";
    public string Sidebar { get; set; } = "Standard";
    public string ContentWidth { get; set; } = "Standard";
    public string LibraryIcons { get; set; } = "Standard";
}

internal static class AppearanceSettings
{
    internal static Core.JsonSettingsStore<Preferences> Create(string path) => new(path, 1,
        new Dictionary<int, Action<System.Text.Json.Nodes.JsonObject>> { [0] = _ => { } },
        validate: Valid, maximumBytes: 32768);

    private static bool Valid(Preferences value)
    {
        if (string.IsNullOrWhiteSpace(value.AccentHex)) return true;
        var hex = value.AccentHex.Trim().TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
