using Aquarium.Core;
using Aquarium.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileOrganizer;

internal sealed record FolderEntry(string Path, string Name, string Kind, long? Bytes, DateTime? ModifiedUtc, string Code)
{
    public bool IsFile => Kind == "File" && Code == "observed";
    public bool IsFolder => Kind == "Folder" && Code == "observed";
}
internal sealed record FolderSnapshot(string Path, DateTimeOffset ObservedAtUtc, string Code, bool Complete, IReadOnlyList<FolderEntry> Entries)
{
    public string Coverage => "Immediate folder entries only; no recursive indexing or content reads. Linked folders are not entered. Listings can change after observation; unavailable metadata and limits remain explicit.";
}

internal static class FolderModel
{
    internal static FolderSnapshot Read(string path, CancellationToken token = default, int maximumEntries = 10000)
    {
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        token.ThrowIfCancellationRequested(); string full = BrowserPaths.Normalize(path);
        var rows = new List<FolderEntry>(); long pathText = 0;
        try
        {
            PathSafety.RefuseLinks(full);
            foreach (string item in Directory.EnumerateFileSystemEntries(full, "*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }))
            {
                token.ThrowIfCancellationRequested();
                if (rows.Count >= maximumEntries) return Result("entry-limit", false);
                pathText += item.Length; if (pathText > 2 * 1024 * 1024) return Result("path-text-limit", false);
                string name = System.IO.Path.GetFileName(item);
                try
                {
                    _ = BrowserPaths.Normalize(item);
                    var attributes = File.GetAttributes(item);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { rows.Add(new(item, name, "Linked entry", null, null, "observed-link")); continue; }
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    FileSystemInfo info = directory ? new DirectoryInfo(item) : new FileInfo(item);
                    DateTime? modified = info.LastWriteTimeUtc; if (modified <= DateTime.FromFileTimeUtc(0)) modified = null;
                    long? bytes = info is FileInfo file ? file.Length : null;
                    rows.Add(new(item, name, directory ? "Folder" : "File", bytes, modified, "observed"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { rows.Add(new(item, name, "Unknown", null, null, "metadata-unavailable")); }
            }
            bool complete = rows.All(r => r.Code is "observed" or "observed-link");
            return Result(complete ? "listing-complete" : "metadata-partial", complete);
        }
        catch (UnauthorizedAccessException) { return Result("permission-denied", false); }
        catch (Exception ex) when (ex is IOException or NotSupportedException) { return Result("folder-unavailable", false); }
        FolderSnapshot Result(string code, bool complete) => new(full, DateTimeOffset.UtcNow, code, complete,
            Array.AsReadOnly(rows.OrderBy(r => r.Kind == "Folder" ? 0 : 1).ThenBy(r => r.Name, StringComparer.Ordinal).ToArray()));
    }
    internal static FolderEntry[] Filter(FolderSnapshot snapshot, string query)
    {
        if (query.Length > 256) throw new ArgumentException("Use a filename filter up to 256 characters.");
        return snapshot.Entries.Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}

internal static class BrowserPaths
{
    internal static string Normalize(string path) => FilePathPolicy.NormalizeAbsolute(path);
}

internal sealed record FolderBookmark(Guid Id, string Name, string Path)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}
internal sealed class FolderSettings
{
    public List<FolderBookmark> Bookmarks { get; set; } = [];
}
internal sealed class FolderBookmarks
{
    private readonly JsonSettingsStore<FolderSettings> store;
    private SettingsSnapshot<FolderSettings>? current;
    internal bool Writable => current is not null;
    internal IReadOnlyList<FolderBookmark> Items => current is null ? [] : current.Value.Bookmarks.AsReadOnly();
    internal FolderBookmarks(string directory)
    {
        if (!System.IO.Path.IsPathFullyQualified(directory)) throw new ArgumentException("Use an absolute bookmark profile folder.");
        store = new(System.IO.Path.Combine(directory, "browser.json"), 1, validate: Valid, maximumBytes: 512 * 1024);
    }
    internal void Load() { current = null; current = store.Load(); }
    internal async Task AddAsync(string name, string path, CancellationToken token = default)
    {
        var snapshot = current ?? throw new InvalidOperationException("Reload supported bookmark records before editing.");
        var item = new FolderBookmark(Guid.NewGuid(), name.Trim(), BrowserPaths.Normalize(path));
        current = await store.SaveAsync(snapshot, new FolderSettings { Bookmarks = [.. snapshot.Value.Bookmarks, item] }, keepPrevious: true, token);
    }
    internal async Task RemoveAsync(Guid id, CancellationToken token = default)
    {
        var snapshot = current ?? throw new InvalidOperationException("Reload supported bookmark records before editing.");
        if (!snapshot.Value.Bookmarks.Any(b => b.Id == id)) throw new InvalidOperationException("Reload the selected bookmark.");
        current = await store.SaveAsync(snapshot, new FolderSettings { Bookmarks = snapshot.Value.Bookmarks.Where(b => b.Id != id).ToList() }, keepPrevious: true, token);
    }
    internal async Task UndoAsync(CancellationToken token = default)
    { current = await store.RestorePreviousAsync(current ?? throw new InvalidOperationException("Reload bookmarks first."), token); }
    private static bool Valid(FolderSettings settings) => settings.Bookmarks is not null && settings.Bookmarks.Count <= 100 &&
        settings.Bookmarks.All(b => b is not null && b.Id != Guid.Empty && !string.IsNullOrWhiteSpace(b.Name) && b.Name.Length <= 80 && !b.Name.Any(char.IsControl) &&
            b.Path is not null && b.Path.Length <= 4096 && !b.Path.Any(char.IsControl) && System.IO.Path.IsPathFullyQualified(b.Path)) &&
        settings.Bookmarks.Select(b => b.Id).Distinct().Count() == settings.Bookmarks.Count;
}
