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
    var verified = await installer.VerifyInstalledFilesAsync(newer, valid, default);
    Check(verified.Checked == 2 && verified.Damaged == 0, "Verified installation did not match its package");
    await File.WriteAllTextAsync(Path.Combine(newer.InstallDirectory, newer.EntryPoint), "changed fixture");
    var damagedFiles = await installer.VerifyInstalledFilesAsync(newer, valid, default);
    Check(damagedFiles.Damaged == 1, "Modified program file was not reported");
    Check(store.Registry.InstallationHistory.Contains(installed), "Previous installation was not retained");
    await installer.RollbackAsync(installed);
    Check(store.Registry.Installations.Single() == installed && store.Registry.InstallationHistory.Contains(newer), "Rollback did not preserve both versions");
    await installer.RollbackAsync(newer);
    var profile = Path.Combine(root, "saved-settings.json");
    await File.WriteAllTextAsync(profile, "{}");
    await Refuses(() => installer.UninstallAsync(newer with { InstallDirectory = root }), "uninstall outside managed directory");
    Check(await installer.UninstallAsync(newer), "Managed files could not be removed");
    Check(!Directory.Exists(installed.InstallDirectory) && !Directory.Exists(newer.InstallDirectory), "A managed version survived uninstall");
    Check(File.Exists(profile), "Uninstall touched profile data");
    await reload.LoadAsync();
    Check(reload.Registry.Installations.Count == 0 && reload.Registry.UninstalledAppIds.Contains(source.AppId), "Uninstall did not persist");
    Check(reload.Registry.LibraryAppIds.Contains(source.AppId), "Uninstall removed the library bookmark");
    await installer.InstallZipAsync(source, "fixture-reinstall", valid, Hash(valid), progress, default);
    Check(!store.Registry.UninstalledAppIds.Contains(source.AppId), "Explicit reinstall did not clear the uninstall marker");
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
