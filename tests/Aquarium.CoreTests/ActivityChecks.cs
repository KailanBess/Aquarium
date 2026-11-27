using Aquarium.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ActivityChecks
{
    internal static async Task Run(string root)
    {
        int checks = 0;
        void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
        async Task Reject<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
        var folder = Path.Combine(root, "activity"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "history.json");
        var store = new ActivityHistory(path, "generated.app", ["operation.started", "operation.failed"], 3);
        Check(store.Read().Events.Count == 0 && !File.Exists(path), "Reading empty history wrote a file");
        var first = await store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3");
        Check(first.Id != Guid.Empty && first.RecordedAtUtc.Offset == TimeSpan.Zero && store.Read().Events[0] == first, "Event identity/timing was lost");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["FutureRoot"] = new JsonObject { ["retained"] = true };
        document["Events"]![0]!["FutureEvent"] = "retained";
        document["Events"]![0]!["Code"] = "future.event";
        File.WriteAllText(path, document.ToJsonString());
        await store.AppendAsync("operation.failed", ActivityLevel.Error, "1.2.3");
        var retained = JsonNode.Parse(File.ReadAllText(path))!;
        Check(retained["FutureRoot"]!["retained"]!.GetValue<bool>() && retained["Events"]![0]!["FutureEvent"]!.GetValue<string>() == "retained", "Unknown root/event fields were removed");
        Check(retained["Events"]![0]!["Code"]!.GetValue<string>() == "future.event", "Older client removed a valid future event code");
        var snapshot = store.Read(); await store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3");
        Check(!JsonSerializer.Serialize(snapshot).Contains("FutureRoot") && !JsonSerializer.Serialize(snapshot).Contains("FutureEvent"), "Typed export exposed unknown extra fields");
        await store.AppendAsync("operation.started", ActivityLevel.Warning, "1.2.3");
        Check(store.Read().Events.Count == 3 && store.Read().DroppedEntries == 1 && store.Read().Events.All(e => e.Id != first.Id), "Retention or drop count failed");
        Check(snapshot.Events.Count == 2 && snapshot.DroppedEntries == 0, "Captured read snapshot mutated during append");
        var prior = File.ReadAllBytes(path);
        await Reject<ArgumentException>(() => store.AppendAsync("unregistered-private-text", ActivityLevel.Error, "1.2.3"));
        await Reject<ArgumentException>(() => store.AppendAsync("operation.started", (ActivityLevel)99, "1.2.3"));
        await Reject<ArgumentException>(() => store.AppendAsync("operation.started", ActivityLevel.Error, "secret"));
        await Reject<ArgumentException>(() => store.AppendAsync("operation.started", ActivityLevel.Error, " 1.2.3 "));
        Check(File.ReadAllBytes(path).SequenceEqual(prior), "Rejected event changed history");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Reject<OperationCanceledException>(() => store.AppendAsync("operation.started", ActivityLevel.Error, "1.2.3", cancel.Token));
        Check(File.ReadAllBytes(path).SequenceEqual(prior), "Canceled event changed history");
        using (var locked = new FileStream(path + ".writer.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Reject<IOException>(() => new ActivityHistory(path, "generated.app", ["operation.started"]).AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
        Check(File.ReadAllBytes(path).SequenceEqual(prior), "Conflicting writer changed history");
        var exhausted = JsonNode.Parse(prior)!.AsObject(); exhausted["DroppedEntries"] = long.MaxValue;
        File.WriteAllText(path, exhausted.ToJsonString()); var exhaustedBytes = File.ReadAllBytes(path);
        await Reject<InvalidDataException>(() => store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
        Check(File.ReadAllBytes(path).SequenceEqual(exhaustedBytes), "Exhausted counter changed history");
        var future = JsonNode.Parse(prior)!.AsObject(); future["SchemaVersion"] = 2;
        File.WriteAllText(path, future.ToJsonString()); var futureBytes = File.ReadAllBytes(path);
        await Reject<InvalidDataException>(() => store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
        Check(File.ReadAllBytes(path).SequenceEqual(futureBytes), "Future schema was overwritten");
        foreach (var invalid in new[] { "{\"SchemaVersion\":1,\"schemaversion\":1}", "{\"SchemaVersion\":1}", "[]" })
        {
            File.WriteAllText(path, invalid);
            await Reject<InvalidDataException>(() => store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
            Check(File.ReadAllText(path) == invalid, "Malformed history was overwritten");
        }
        File.WriteAllText(path, "{");
        await Reject<JsonException>(() => store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
        Check(File.ReadAllText(path) == "{", "Malformed JSON was reset");
        File.WriteAllText(path, new string(' ', 1024 * 1024 + 1));
        await Reject<InvalidDataException>(() => store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
        var nearlyFull = new JsonObject { ["SchemaVersion"] = 1, ["ApplicationId"] = "generated.app", ["DroppedEntries"] = 0, ["Events"] = new JsonArray(), ["FuturePayload"] = "" };
        int emptyLength = System.Text.Encoding.UTF8.GetByteCount(nearlyFull.ToJsonString());
        nearlyFull["FuturePayload"] = new string('x', 1024 * 1024 - emptyLength - 8);
        File.WriteAllText(path, nearlyFull.ToJsonString()); var largeBytes = File.ReadAllBytes(path);
        await Reject<InvalidDataException>(() => store.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3"));
        Check(File.ReadAllBytes(path).SequenceEqual(largeBytes), "Oversized publication changed prior history");
        var parallelPath = Path.Combine(folder, "parallel.json"); var parallel = new ActivityHistory(parallelPath, "generated.app", ["operation.started"]);
        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => parallel.AppendAsync("operation.started", ActivityLevel.Information, "1.2.3")));
        Check(parallel.Read().Events.Count == 25 && parallel.Read().Events.Select(e => e.Id).Distinct().Count() == 25, "Concurrent in-process writes lost/duplicated events");
        Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "Activity writes leaked temporary files");
        foreach (int capacity in new[] { 0, 1001 }) { await Reject<ArgumentException>(() => { _ = new ActivityHistory(path, "generated.app", ["code"], capacity); return Task.CompletedTask; }); }
        await Reject<ArgumentException>(() => { _ = new ActivityHistory(path, "../app", ["code"]); return Task.CompletedTask; });
        await Reject<ArgumentException>(() => { _ = new ActivityHistory(path, "generated.app", ["code", "code"]); return Task.CompletedTask; });
        await Reject<ArgumentException>(() => { _ = new ActivityHistory(path, "generated.app", Enumerable.Range(0, 129).Select(i => "code" + i)); return Task.CompletedTask; });
        if (!OperatingSystem.IsWindows())
        {
            var linked = Path.Combine(folder, "linked.json"); File.CreateSymbolicLink(linked, parallelPath);
            await Reject<InvalidDataException>(() => new ActivityHistory(linked, "generated.app", ["code"]).AppendAsync("code", ActivityLevel.Information, "1.2.3"));
            File.Delete(linked);
        }
        Console.WriteLine($"Activity history: {checks} retention, concurrency, privacy-boundary, cancellation and preservation checks passed.");
    }
}
