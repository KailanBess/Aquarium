using Aquarium;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class OfflineChecks
{
    internal static async Task Run(string parent)
    {
        var root = Path.Combine(parent, "offline"); Directory.CreateDirectory(root);
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        async Task Refuses<T>(Func<Task> action) where T : Exception
        {
            try { await action(); } catch (T) { checks++; return; }
            throw new Exception("Offline package accepted: expected " + typeof(T).Name);
        }
        var source = GitHubPackages.Sources.Single(s => s.AppId == "aquarium.media");
        var payload = Path.Combine(root, "fixture.zip");
        using (var zip = ZipFile.Open(payload, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(zip.CreateEntry("FishNet.exe").Open()); writer.Write("Generated inert fixture; never executed."); }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(payload)));
        var package = new AppPackage(source, "v0.4.1", "FishNet-windows-x64.zip",
            "https://github.com/KailanBess/FishNet/releases/download/v0.4.1/FishNet-windows-x64.zip", hash, new FileInfo(payload).Length);
        var file = Path.Combine(root, "valid.aqpkg");
        var staging = Path.Combine(root, "staging");
        await OfflinePackages.ExportAsync(package, payload, file, default);
        var manifest = new OfflinePackageManifest(1, source.AppId, source.Repository, package.Version, hash, package.Size);
        using (var prepared = await OfflinePackages.PrepareAsync(file, staging, default))
        {
            Check(prepared.Manifest == manifest, "Manifest changed during export/import");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prepared.PayloadPath))) == hash, "Payload changed");
            await Refuses<IOException>(() => { File.WriteAllText(prepared.PayloadPath, "changed after review"); return Task.CompletedTask; });
            var store = new LocalStore(Path.Combine(root, "registry")); await store.LoadAsync();
            var installer = new PackageInstaller(store, Path.Combine(root, "Apps"), () => throw new Exception("Offline install attempted network access"));
            var installed = await installer.InstallZipAsync(prepared.Source, manifest.Version, prepared.PayloadPath,
                manifest.Sha256, new Progress<string>(), default);
            Check(installed.AppId == source.AppId && installed.PackageSha256.Equals(hash, StringComparison.OrdinalIgnoreCase), "Offline installation identity");
            Check(File.Exists(Path.Combine(installed.InstallDirectory, "FishNet.exe")), "Offline app not extracted");
        }
        Check(Directory.GetDirectories(staging).Length == 0, "Successful review leaked payload");
        string Bundle(string name, OfflinePackageManifest data, byte[]? bytes = null, string? extra = null)
        {
            var path = Path.Combine(root, name + ".aqpkg");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var output = archive.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(output, data);
            using (var output = archive.CreateEntry("package.zip").Open()) output.Write(bytes ?? File.ReadAllBytes(payload));
            if (extra is not null) { using var output = archive.CreateEntry(extra).Open(); output.WriteByte(1); }
            return path;
        }
        foreach (var data in new[]
        {
            manifest with { SchemaVersion = 2 }, manifest with { AppId = "unknown" },
            manifest with { Repository = "other/FishNet" }, manifest with { Version = "" },
            manifest with { Version = "v1\nmisleading" }, manifest with { Size = 0 },
            manifest with { Size = 513L * 1024 * 1024 }, manifest with { Sha256 = "bad" },
            manifest with { Sha256 = new string('0', 64) }, manifest with { Size = package.Size + 1 }
        })
        {
            var invalid = Bundle("invalid-" + checks, data);
            await Refuses<InvalidDataException>(() => OfflinePackages.PrepareAsync(invalid, staging, default));
            Check(Directory.GetDirectories(staging).Length == 0, "Rejected import leaked staging");
        }
        foreach (var extra in new[] { "../escape", "manifest.json", "package.zip" })
            await Refuses<InvalidDataException>(() => OfflinePackages.PrepareAsync(Bundle("extra-" + checks, manifest, extra: extra), staging, default));
        var corrupt = File.ReadAllBytes(payload); corrupt[0] ^= 1;
        await Refuses<InvalidDataException>(() => OfflinePackages.PrepareAsync(Bundle("corrupt", manifest, corrupt), staging, default));
        Check(!File.Exists(Path.Combine(root, "escape")), "Traversal created a file");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Refuses<OperationCanceledException>(() => OfflinePackages.PrepareAsync(file, staging, canceled.Token));
        await Refuses<OperationCanceledException>(() => OfflinePackages.ExportAsync(package, payload, Path.Combine(root, "canceled.aqpkg"), canceled.Token));
        Check(!File.Exists(Path.Combine(root, "canceled.aqpkg")), "Canceled export published a file");
        var previous = File.ReadAllBytes(file);
        await Refuses<InvalidDataException>(() => OfflinePackages.ExportAsync(package with { Sha256 = new string('0', 64) }, payload, file, default));
        Check(File.ReadAllBytes(file).SequenceEqual(previous), "Failed export destroyed previous package");
        Check(Directory.GetDirectories(staging).Length == 0, "Failed review leaked staging");
        Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Export left temporary files");
        Console.WriteLine($"Offline packages: {checks} checks passed.");
    }
}
