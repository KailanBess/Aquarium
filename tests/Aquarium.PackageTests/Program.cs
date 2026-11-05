using Aquarium;
using System.IO.Compression;
using System.Security.Cryptography;

var root = Path.Combine(Path.GetTempPath(), "Aquarium-package-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Refuses(Func<Task> operation, string label)
{
    try { await operation(); } catch (InvalidDataException) { checks++; return; }
    throw new Exception("Did not refuse " + label);
}
string Zip(string name, params string[] paths)
{
    var file = Path.Combine(root, name + ".zip");
    using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
    foreach (var path in paths) { using var writer = new StreamWriter(archive.CreateEntry(path).Open()); writer.Write("Generated test fixture; never executed."); }
    return file;
}
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
try
{
    Check(!new UpdateSchedule().IsDue(DateTimeOffset.UtcNow), "Update checks default on");
    Check(new UpdateSchedule { Hours = 24, LastAttempt = DateTimeOffset.UtcNow.AddDays(-2) }.IsDue(DateTimeOffset.UtcNow), "Missed scheduled check not due");
    Check(!new UpdateSchedule { Hours = 24, LastAttempt = DateTimeOffset.UtcNow.AddHours(-1) }.IsDue(DateTimeOffset.UtcNow), "Schedule repeats too soon");
    var store = new LocalStore(Path.Combine(root, "registry")); await store.LoadAsync();
    var installer = new PackageInstaller(store, Path.Combine(root, "Apps"));
    var source = GitHubPackages.Sources.Single(s => s.AppId == "aquarium.security");
    var progress = new Progress<string>();
    var valid = Zip("valid", "FishTank.exe", "FishTank.exe.config");
    var installed = await installer.InstallZipAsync(source, "fixture-v1", valid, Hash(valid), progress, default);
    Check(File.Exists(Path.Combine(installed.InstallDirectory, installed.EntryPoint)), "Expected entry point missing");
    Check(store.Registry.Installations.Single() == installed, "Installation not registered");
    Check(store.Registry.LibraryAppIds.Contains(source.AppId), "Installed app not added to Library");
    var reload = new LocalStore(Path.Combine(root, "registry")); await reload.LoadAsync();
    Check(reload.Registry.Installations.Single().EntryPoint == installed.EntryPoint, "Entry point not persisted");
    var upgraded = await installer.InstallZipAsync(source, "fixture-v2", valid, Hash(valid), progress, default);
    Check(store.Registry.InstallationHistory.Single() == installed, "Previous installation not retained for rollback");
    await installer.RollbackAsync(installed);
    Check(store.Registry.Installations.Single() == installed && store.Registry.InstallationHistory.Contains(upgraded), "Rollback lost the newer version");
    await store.SetLibraryAsync(source.AppId, true);
    var historyReload = new LocalStore(Path.Combine(root, "registry")); await historyReload.LoadAsync();
    Check(historyReload.Registry.InstallationHistory.Contains(upgraded), "Library save lost rollback history");
    await Refuses(() => installer.RollbackAsync(installed with { InstallDirectory = Path.Combine(root, "outside") }), "untracked rollback path");
    await Refuses(() => installer.InstallZipAsync(source, "bad", valid, new string('0', 64), progress, default), "checksum mismatch");
    foreach (var paths in new[] { new[] { "../escape.exe", "FishTank.exe" }, new[] { "FishTank.exe", "fishtank.EXE" }, new[] { "other.exe" }, new[] { "folder/../../escape.exe" } })
    {
        var bad = Zip(Guid.NewGuid().ToString("N"), paths);
        await Refuses(() => installer.InstallZipAsync(source, "bad", bad, Hash(bad), progress, default), "unsafe or unsupported package");
        Check(store.Registry.Installations.Single() == installed, "Failed attempt changed registry");
    }
    Check(!File.Exists(Path.Combine(root, "Apps", source.AppId, "escape.exe")), "Package escaped extraction directory");
    var newer = await installer.InstallZipAsync(source, "fixture-v2", valid, Hash(valid), progress, default);
    Check(File.Exists(Path.Combine(installed.InstallDirectory, installed.EntryPoint)), "Previous version was removed");
    Check(store.Registry.Installations.Single().Version == "fixture-v2", "New version not activated");
    var retainedExe = Path.Combine(installed.InstallDirectory, installed.EntryPoint);
    File.Move(retainedExe, retainedExe + ".saved");
    try { await installer.RollbackAsync(installed); throw new Exception("Missing rollback executable accepted"); }
    catch (FileNotFoundException) { checks++; }
    Check(store.Registry.Installations.Single() == newer, "Failed rollback changed active version");
    File.Move(retainedExe + ".saved", retainedExe);
    try { PackageInstaller.RequireSpace(root, long.MaxValue - 64L * 1024 * 1024); throw new Exception("Insufficient space accepted"); }
    catch (IOException) { checks++; }
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel();
        try { await installer.InstallZipAsync(source, "canceled", valid, Hash(valid), progress, canceled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { checks++; }
        Check(store.Registry.Installations.Single() == newer, "Cancellation changed active installation");
    }
    await Task.WhenAll(store.SetLibraryAsync("aquarium.emulation", true), store.RecordInstallationAsync(newer));
    Check(store.Registry.LibraryAppIds.Contains("aquarium.emulation"), "Concurrent library change lost");
    Check(GitHubPackages.Sources.Count(s => s.IncludedByDefault) == 2 && !GitHubPackages.Sources.Single(s => s.AppId == "aquarium.emulation").IncludedByDefault, "Default bundle must exclude FishBowl");
    var bytes = File.ReadAllBytes(valid);
    var package = new AppPackage(source, "resumed", "fixture.zip", "https://github.com/" + source.Repository + "/releases/download/fixture/fixture.zip", Hash(valid), bytes.Length);
    var cache = Path.Combine(installer.AppRoot, ".downloads"); Directory.CreateDirectory(cache);
    var partial = Path.Combine(cache, package.Sha256.ToLowerInvariant() + ".part");
    foreach (var honorsRange in new[] { true, false })
    {
        File.WriteAllBytes(partial, bytes.Take(bytes.Length / 2).ToArray());
        var handler = new DownloadFixture(bytes, honorsRange);
        var resumedInstaller = new PackageInstaller(store, installer.AppRoot, () => new System.Net.Http.HttpClient(handler));
        await resumedInstaller.DownloadAndInstallAsync(package, progress, default);
        Check(handler.RequestedOffset == bytes.Length / 2, "Existing download was not requested by range");
        Check(!File.Exists(partial), "Successful download cache not cleared");
    }
    await File.WriteAllTextAsync(store.RegistryPath, "invalid JSON");
    var damaged = new LocalStore(Path.Combine(root, "registry")); await damaged.LoadAsync();
    Check(!damaged.CanSave && await File.ReadAllTextAsync(store.RegistryPath) == "invalid JSON", "Invalid registry was overwritten");
    Console.WriteLine($"PASS: {checks} package and persistence checks. No fixture executable was run.");
}

finally
{
    var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!Path.GetFullPath(root).StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("Aquarium-package-tests-")) throw new Exception("Unsafe fixture cleanup path.");
    Directory.Delete(root, true);
}

sealed class DownloadFixture(byte[] bytes, bool honorsRange) : System.Net.Http.HttpMessageHandler
{
    public long? RequestedOffset { get; private set; }
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedOffset = request.Headers.Range?.Ranges.Single().From;
        var offset = honorsRange ? (int)(RequestedOffset ?? 0) : 0;
        var response = new System.Net.Http.HttpResponseMessage(honorsRange ? System.Net.HttpStatusCode.PartialContent : System.Net.HttpStatusCode.OK)
        { Content = new System.Net.Http.ByteArrayContent(bytes.Skip(offset).ToArray()) };
        if (honorsRange) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
        return Task.FromResult(response);
    }
}
