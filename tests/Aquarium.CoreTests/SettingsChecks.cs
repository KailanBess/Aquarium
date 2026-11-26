using Aquarium.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SettingsChecks
{
    private sealed class Settings
    {
        public string Theme { get; set; } = "Ocean";
        public Nested Detail { get; set; } = new();
    }
    private sealed class Nested { public int Size { get; set; } = 12; }
    internal static async Task Run(string root)
    {
        var path = Path.Combine(root, "versioned-settings.json"); int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        async Task Refuses<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
        Dictionary<int, Action<JsonObject>> Migrations() => new() { [0] = _ => { } };
        JsonSettingsStore<Settings> Store() => new(path, 1, Migrations(), value => value.Detail is not null && value.Detail.Size > 0);
        var store = Store(); var empty = store.Load();
        Check(empty.Value.Theme == "Ocean" && !File.Exists(path), "Loading defaults wrote a file");
        var loaded = await store.SaveAsync(empty, new Settings { Theme = "Slate" }, keepPrevious: true);
        Check(File.Exists(path + ".previous"), "First save omitted default undo");
        var defaults = await store.RestorePreviousAsync(loaded);
        Check(defaults.Value.Theme == "Ocean", "First save undo failed");
        await File.WriteAllTextAsync(path, "{\"Theme\":\"Ocean\",\"Future\":42,\"Detail\":{\"Size\":10,\"FutureNested\":\"kept\"}}");
        var before = File.ReadAllText(path); loaded = store.Load();
        Check(File.ReadAllText(path) == before, "Migration wrote during load");
        loaded = await store.SaveAsync(loaded, new Settings { Theme = "Midnight", Detail = new Nested { Size = 20 } }, keepPrevious: true);
        using (var json = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Check(json.RootElement.GetProperty("_schemaVersion").GetInt32() == 1, "Missing schema version");
            Check(json.RootElement.GetProperty("Future").GetInt32() == 42, "Unknown setting lost");
            Check(json.RootElement.GetProperty("Detail").GetProperty("FutureNested").GetString() == "kept", "Nested unknown setting lost");
            Check(json.RootElement.GetProperty("Detail").GetProperty("Size").GetInt32() == 20, "Known setting not updated");
        }
        Check(File.ReadAllText(path + ".previous") == before, "Prior file not backed up exactly");
        var restored = await store.RestorePreviousAsync(loaded);
        Check(restored.Value.Theme == "Ocean" && restored.Value.Detail.Size == 10, "Undo did not restore known settings");
        Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Future").GetInt32() == 42, "Undo lost unknown field");
        await Refuses<IOException>(() => store.SaveAsync(loaded, new Settings()));
        var secondStore = Store(); var first = store.Load(); var second = secondStore.Load();
        await store.SaveAsync(first, new Settings { Theme = "Winner" });
        await Refuses<IOException>(() => secondStore.SaveAsync(second, new Settings { Theme = "Stale" }));
        Check(store.Load().Value.Theme == "Winner", "Stale writer overwrote newer settings");
        var simultaneous = store.Load();
        async Task<bool> TrySave(JsonSettingsStore<Settings> target, string theme)
        {
            try { await target.SaveAsync(simultaneous, new Settings { Theme = theme }); return true; }
            catch (IOException) { return false; }
        }
        var results = await Task.WhenAll(TrySave(store, "First"), TrySave(secondStore, "Second"));
        Check(results.Count(value => value) == 1, "Concurrent writers both committed the same revision");
        var validBytes = File.ReadAllBytes(path);
        var fresh = store.Load();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Refuses<OperationCanceledException>(() => store.SaveAsync(fresh, new Settings(), token: cancel.Token));
        Check(File.ReadAllBytes(path).SequenceEqual(validBytes), "Canceled save changed settings");
        await Refuses<InvalidDataException>(() => store.SaveAsync(fresh, new Settings { Detail = new Nested { Size = -1 } }));
        var other = new JsonSettingsStore<Settings>(Path.Combine(root, "other.json"), 1, Migrations());
        await Refuses<ArgumentException>(() => other.SaveAsync(fresh, new Settings()));
        var noBackup = new JsonSettingsStore<Settings>(Path.Combine(root, "no-backup.json"), 1, Migrations());
        await Refuses<FileNotFoundException>(() => noBackup.RestorePreviousAsync(noBackup.Load()));
        foreach (var invalid in new[] { "{\"_schemaVersion\":2}", "{\"_schemaVersion\":-1}", "{\"_schemaVersion\":null}", "{\"_schemaVersion\":\"1\"}", "[]", "null", "{\"Detail\":null}", "{\"Theme\":\"A\",\"Theme\":\"B\"}", "{\"Detail\":{\"Size\":1,\"Size\":2}}" })
        {
            await File.WriteAllTextAsync(path, invalid);
            await Refuses<InvalidDataException>(() => { store.Load(); return Task.CompletedTask; });
            Check(File.ReadAllText(path) == invalid, "Invalid settings were replaced");
        }
        await File.WriteAllTextAsync(path, "{broken}");
        await Refuses<JsonException>(() => { store.Load(); return Task.CompletedTask; });
        await Refuses<IOException>(() => store.RestorePreviousAsync(fresh)); // A malformed current record cannot be overwritten by a stale revision.
        await File.WriteAllTextAsync(path, "{\"Theme\":\"Legacy\"}");
        var noMigration = new JsonSettingsStore<Settings>(path, 1);
        await Refuses<InvalidDataException>(() => { noMigration.Load(); return Task.CompletedTask; });
        var migrated = new JsonSettingsStore<Settings>(path, 2, new Dictionary<int, Action<JsonObject>>
        {
            [0] = document => document["Theme"] = "Migrated", [1] = document => document["AddedInV2"] = true
        });
        var version2 = migrated.Load();
        Check(version2.Value.Theme == "Migrated" && !File.ReadAllText(path).Contains("AddedInV2"), "Migration was not read-only");
        await migrated.SaveAsync(version2, version2.Value);
        Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("AddedInV2").GetBoolean(), "Migration data not retained");
        var small = new JsonSettingsStore<Settings>(path, 2, maximumBytes: 2);
        await Refuses<InvalidDataException>(() => { small.Load(); return Task.CompletedTask; });
        Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Settings left temporary files");
        Console.WriteLine($"Settings SDK: {checks} checks passed.");
    }
}
