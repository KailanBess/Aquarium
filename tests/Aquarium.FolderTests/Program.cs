using FileOrganizer;
using Aquarium.Tools;
using System.Text.Json.Nodes;

string root = Path.Combine(Path.GetTempPath(), "Aquarium-folder-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Reject<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
try
{
    string folder = Path.Combine(root, "folder"); Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(folder, "child"));
    File.WriteAllText(Path.Combine(folder, "first.txt"), "generated text"); File.WriteAllText(Path.Combine(folder, "second.json"), "{}");
    var snapshot = FolderModel.Read(folder);
    Check(snapshot.Complete && snapshot.Entries.Count == 3 && snapshot.Code == "listing-complete", "Regular folder listing lost entries/coverage");
    Check(snapshot.Entries[0].IsFolder && snapshot.Entries.Count(e => e.IsFile) == 2, "Entry metadata did not distinguish folders/files");
    Check(snapshot.Entries.Single(e => e.Name == "first.txt").Bytes == 14 && snapshot.ObservedAtUtc.Offset == TimeSpan.Zero, "Observed size/time attribution incorrect");
    Check(FolderModel.Filter(snapshot, "FIRST").Single().Name == "first.txt" && FolderModel.Filter(snapshot, "absent").Length == 0, "Literal filename filter incorrect");
    var limited = FolderModel.Read(folder, maximumEntries: 1); Check(!limited.Complete && limited.Code == "entry-limit" && limited.Entries.Count == 1, "Entry cap inferred complete coverage");
    var unavailable = FolderModel.Read(Path.Combine(root, "missing")); Check(!unavailable.Complete && unavailable.Entries.Count == 0, "Missing folder inferred an empty complete listing");
    using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); await Reject<OperationCanceledException>(() => { FolderModel.Read(folder, canceled.Token); return Task.CompletedTask; }); }
    await Reject<InvalidDataException>(() => { FolderModel.Read("relative-folder"); return Task.CompletedTask; });
    await Reject<ArgumentException>(() => { FolderModel.Filter(snapshot, new string('x', 257)); return Task.CompletedTask; });
    if (OperatingSystem.IsWindows())
    {
        foreach (var invalid in new[] { @"\\.\pipe\fixture", @"\\localhost\pipe\fixture", @"C:\NUL.txt", @"C:\COM¹.txt", @"C:\folder\file.txt:stream", @"C:\folder\ambiguous. " })
            await Reject<InvalidDataException>(() => { FilePathPolicy.NormalizeAbsolute(invalid); return Task.CompletedTask; });
    }
    else
    {
        string link = Path.Combine(folder, "linked"); Directory.CreateSymbolicLink(link, Path.Combine(folder, "child"));
        var links = FolderModel.Read(folder); Check(links.Entries.Single(e => e.Name == "linked").Code == "observed-link", "Linked folder was entered or lost");
    }
    string profile = Path.Combine(root, "profile"); var bookmarks = new FolderBookmarks(profile); bookmarks.Load();
    Check(bookmarks.Writable && bookmarks.Items.Count == 0 && !Directory.Exists(profile), "Loading absent bookmarks mutated profile");
    await bookmarks.AddAsync("Fixture", folder); Check(bookmarks.Items.Count == 1, "Explicit bookmark save failed");
    string path = Path.Combine(profile, "browser.json"); var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    document["FutureRoot"] = "retained"; document["Bookmarks"]![0]!["FutureItem"] = "retained too"; await File.WriteAllTextAsync(path, document.ToJsonString());
    bookmarks.Load(); await bookmarks.AddAsync("Second", Path.Combine(folder, "child"));
    var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    Check(saved["FutureRoot"]!.GetValue<string>() == "retained" && saved["Bookmarks"]![0]!["FutureItem"]!.GetValue<string>() == "retained too", "Bookmark edit dropped unknown root/item fields");
    var stale = new FolderBookmarks(profile); stale.Load(); await bookmarks.RemoveAsync(bookmarks.Items[1].Id);
    await Reject<IOException>(() => stale.AddAsync("Concurrent", folder));
    await bookmarks.UndoAsync(); Check(bookmarks.Items.Count == 2, "Bookmark previous-record undo failed");
    var future = JsonNode.Parse(await File.ReadAllTextAsync(path))!; future["_schemaVersion"] = 99; await File.WriteAllTextAsync(path, future.ToJsonString()); byte[] preserved = await File.ReadAllBytesAsync(path);
    await Reject<InvalidDataException>(() => { bookmarks.Load(); return Task.CompletedTask; });
    Check(!bookmarks.Writable, "Future bookmark schema left editing enabled");
    await Reject<InvalidOperationException>(() => bookmarks.AddAsync("Blocked", folder));
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(preserved), "Future bookmarks were overwritten");
    await File.WriteAllTextAsync(path, "damaged bookmark fixture"); await Reject<System.Text.Json.JsonException>(() => { bookmarks.Load(); return Task.CompletedTask; });
    Check(await File.ReadAllTextAsync(path) == "damaged bookmark fixture", "Corrupt bookmarks were changed");
    Console.WriteLine($"Folder browser: {checks} listing/filter/limits/bookmark/unknown-field/concurrency/preservation checks passed. Only generated data was accessed.");
}
finally
{
    string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-folder-tests-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup.");
    Directory.Delete(full, true);
}
