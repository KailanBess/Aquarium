using System.Runtime.InteropServices;
using System.Text.Json;

namespace Aquarium.Tools;

internal static class FamilyWindow
{
    internal static void Apply(Form form, string? appearancePath = null)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = new Font("Segoe UI", 10);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Padding = new Padding(12);
        var background = Color.FromArgb(9, 21, 29); var surface = Color.FromArgb(16, 36, 47); var accent = Color.FromArgb(103, 232, 211);
        try
        {
            var path = appearancePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "appearance.json");
            if (File.Exists(path) && new FileInfo(path).Length <= 32768)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (json.RootElement.TryGetProperty("Background", out var preset))
                    (background, surface) = preset.GetString() switch
                    {
                        "Midnight" => (Color.FromArgb(12, 14, 22), Color.FromArgb(30, 32, 46)),
                        "Slate" => (Color.FromArgb(27, 35, 43), Color.FromArgb(45, 56, 65)),
                        _ => (background, surface)
                    };
                if (json.RootElement.TryGetProperty("Accent", out var color)) accent = color.GetString() switch
                { "Aquarium Blue" or "Blue" => Color.FromArgb(113, 190, 255), "Aquarium Coral" or "Coral" => Color.FromArgb(255, 170, 150), _ => accent };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
        var ink = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(241, 247, 255);
        if (SystemInformation.HighContrast) { background = SystemColors.Window; surface = SystemColors.Window; accent = SystemColors.Highlight; }
        void Style(Control control)
        {
            control.ForeColor = ink;
            control.BackColor = control is TextBoxBase or ListBox ? surface : background;
            if (control is Button button)
            {
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = accent;
                button.Padding = new Padding(10, 5, 10, 5); button.Margin = new Padding(4);
                button.MinimumSize = new Size(0, 34); button.UseVisualStyleBackColor = false;
            }
            foreach (Control child in control.Controls) Style(child);
        }
        Style(form);
        form.HandleCreated += (_, _) =>
        {
            int dark = SystemInformation.HighContrast ? 0 : 1;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    internal static T ReadRecords<T>(string path, Func<T, bool> valid)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The saved records are too large and were preserved.");
        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        if (value is null || !valid(value)) throw new InvalidDataException("Saved records are invalid and were preserved. Restore a known-good backup from Aquarium Recovery.");
        return value;
    }
}
