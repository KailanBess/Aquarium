using Aquarium;
using System.Text.Json;

internal static class AppearanceSettingsChecks
{
    internal static async Task Run(string root)
    {
        var path = Path.Combine(root, "appearance-sdk.json");
        const string legacy = "{\"Background\":\"Midnight\",\"AccentHex\":\"#67E8D3\",\"FutureOption\":{\"Enabled\":true}}";
        await File.WriteAllTextAsync(path, legacy);
        var store = AppearanceSettings.Create(path); var original = store.Load();
        if (original.Value.Background != "Midnight" || File.ReadAllText(path) != legacy) throw new Exception("Appearance migration changed the legacy record during load");
        var saved = await store.SaveAsync(original, new Preferences { Background = "Slate", AccentHex = "#123ABC" }, keepPrevious: true);
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            if (!document.RootElement.GetProperty("FutureOption").GetProperty("Enabled").GetBoolean() || document.RootElement.GetProperty("Background").GetString() != "Slate")
                throw new Exception("Appearance apply lost a future setting or omitted the new background");
        if (File.ReadAllText(path + ".previous") != legacy) throw new Exception("Appearance undo omitted exact legacy bytes");
        var restored = await store.RestorePreviousAsync(saved);
        if (restored.Value.Background != "Midnight" || restored.Value.AccentHex != "#67E8D3") throw new Exception("Appearance undo lost prior preferences");
        var bytes = File.ReadAllBytes(path);
        try { await store.SaveAsync(restored, new Preferences { AccentHex = "invalid" }); throw new Exception("Invalid appearance was saved"); }
        catch (InvalidDataException) { }
        if (!bytes.SequenceEqual(File.ReadAllBytes(path))) throw new Exception("Invalid appearance changed the current file");
        await File.WriteAllTextAsync(path + ".previous", "{\"_schemaVersion\":9}");
        try { await store.RestorePreviousAsync(restored); throw new Exception("Future appearance backup was restored"); }
        catch (InvalidDataException) { }
        if (!bytes.SequenceEqual(File.ReadAllBytes(path))) throw new Exception("Unsupported undo replaced current appearance");
        Console.WriteLine("Appearance SDK: legacy migration, future-field retention, exact backup, undo, color validation and unsupported-schema preservation passed.");
    }
}
