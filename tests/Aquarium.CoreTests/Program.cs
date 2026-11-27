using Aquarium.Core;
using System.Text;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "aquarium-core-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Refuses<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { checks++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}
try
{
    await SettingsChecks.Run(root);
    await DiagnosticChecks.Run();
    await ActivityChecks.Run(root);
    var target = Path.Combine(root, "settings.json");
    await AtomicFile.WriteJsonAsync(target, new { Version = 1 });
    Check(JsonDocument.Parse(File.ReadAllText(target)).RootElement.GetProperty("Version").GetInt32() == 1, "Initial write");
    await Refuses<IOException>(() => AtomicFile.WriteAsync(target, async (stream, token) =>
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
        throw new IOException("Injected interrupted serialization");
    }));
    Check(File.ReadAllText(target) == "{\"Version\":1}", "Failed write replaced good data");
    Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Failed write left temporary data");
    using var cancellation = new CancellationTokenSource();
    await Refuses<OperationCanceledException>(() => AtomicFile.WriteAsync(target, async (stream, token) =>
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes("new data"), token);
        cancellation.Cancel();
    }, cancellation.Token));
    Check(File.ReadAllText(target) == "{\"Version\":1}", "Canceled write replaced data");
    Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Canceled write leaked temporary data");
    await AtomicFile.WriteJsonAsync(target, new { Version = 2 });
    Check(File.ReadAllText(target) == "{\"Version\":2}", "Successful replacement");
    var blocked = Path.Combine(root, "directory-instead-of-file"); Directory.CreateDirectory(blocked);
    await Refuses<IOException>(async () =>
    {
        try { await AtomicFile.WriteJsonAsync(blocked, new { Version = 3 }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new IOException("Publication refused", ex); }
    });
    Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Failed publication leaked temp file");

    var profile = Path.Combine(root, "profile");
    using (ProfileLock.Acquire(profile))
        await Refuses<IOException>(() => { using var duplicate = ProfileLock.Acquire(profile); return Task.CompletedTask; });
    using (ProfileLock.Acquire(profile)) checks++;
    PathSafety.RequireWithin(root, target); checks++;
    foreach (var path in new[] { root, root + "-sibling/file", Path.Combine(root, "..", "outside") })
        await Refuses<InvalidDataException>(() => { PathSafety.RequireWithin(root, path); return Task.CompletedTask; });
    if (!OperatingSystem.IsWindows())
    {
        await Refuses<InvalidDataException>(() => { PathSafety.RequireWithin(root, root.ToUpperInvariant() + "/file"); return Task.CompletedTask; });
        var dangling = Path.Combine(root, "dangling");
        File.CreateSymbolicLink(dangling, Path.Combine(root, "nonexistent"));
        await Refuses<InvalidDataException>(() => AtomicFile.WriteJsonAsync(dangling, new { Version = 4 }));
        File.Delete(dangling);
        var linked = Path.Combine(root, "linked-profile");
        Directory.CreateSymbolicLink(linked, profile);
        await Refuses<InvalidDataException>(() => { using var lease = ProfileLock.Acquire(linked); return Task.CompletedTask; });
        Directory.Delete(linked);
    }
    Console.WriteLine($"Aquarium Core: {checks} checks passed.");
}
finally
{
    var full = Path.GetFullPath(root); var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\Aaquarium-core-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup path.");
    Directory.Delete(full, recursive: true);
}
