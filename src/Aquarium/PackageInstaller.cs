using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Aquarium;

public sealed class PackageInstaller(LocalStore store, string? appRoot = null)
{
    public string AppRoot { get; } = Path.GetFullPath(appRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "Apps"));
    public async Task<InstalledApp> DownloadAndInstallAsync(AppPackage package, IProgress<string> progress, CancellationToken token)
    {
        GitHubPackages.ValidateAssetUrl(package.Source, package.DownloadUrl);
        if (!GitHubPackages.IsHash(package.Sha256)) throw new InvalidDataException("Invalid checksum.");
        var cache = Path.Combine(AppRoot, ".downloads");
        RefuseLinks(cache); Directory.CreateDirectory(cache);
        var file = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var client = GitHubPackages.Client();
            using var response = await client.GetAsync(package.DownloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += count;
                    if (received > package.Size || received > 512L * 1024 * 1024) throw new InvalidDataException("Package exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    progress.Report($"Downloading {package.Source.Repository}: {received / 1048576d:F1} / {package.Size / 1048576d:F1} MB");
                }
                if (received != package.Size) throw new InvalidDataException("The package download is incomplete.");
            }
            return await InstallZipAsync(package.Source, package.Version, file, package.Sha256, progress, token);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
    public async Task<InstalledApp> InstallZipAsync(AppSource source, string version, string zipPath, string expectedHash, IProgress<string> progress, CancellationToken token)
    {
        if (!GitHubPackages.Sources.Contains(source) || !GitHubPackages.IsHash(expectedHash)) throw new InvalidDataException("Unknown app or invalid package checksum.");
        if (!store.CanSave) throw new InvalidOperationException("The local registry is read-only.");
        progress.Report("Verifying package checksum…");
        await using var input = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package checksum mismatch. Nothing was installed.");
        input.Position = 0;
        var install = Path.Combine(AppRoot, source.AppId, Guid.NewGuid().ToString("N"));
        RefuseLinks(install); Directory.CreateDirectory(install);
        var registered = false;
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            await ExtractAsync(archive, install, token);
            var executables = Directory.GetFiles(install, source.Executable, SearchOption.AllDirectories);
            if (executables.Length != 1) throw new InvalidDataException("Package must contain exactly one expected app executable.");
            var entry = Path.GetRelativePath(install, executables[0]);
            progress.Report("Registering installation…");
            token.ThrowIfCancellationRequested();
            var record = new InstalledApp(source.AppId, version, install, DateTimeOffset.Now, entry, actual.ToLowerInvariant(), source.Repository);
            await store.RecordInstallationAsync(record);
            registered = true;
            progress.Report($"Installed {source.Repository} {version}. Ready in Library.");
            return record;
        }
        finally
        {
            // This fresh GUID directory belongs only to this attempt. Previous versions remain untouched.
            if (!registered) { RequireWithin(AppRoot, install); RefuseLinks(install); Directory.Delete(install, true); }
        }
    }
    internal static async Task ExtractAsync(ZipArchive archive, string destination, CancellationToken token)
    {
        if (archive.Entries.Count > 20000) throw new InvalidDataException("Too many package files.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(Path.DirectorySeparatorChar).Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Unsafe package path.");
            var path = Path.GetFullPath(Path.Combine(destination, relative));
            RequireWithin(destination, path);
            if (!seen.Add(path)) throw new InvalidDataException("Duplicate package path.");
            total += entry.Length;
            if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Expanded package is too large.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var source = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920]; long written = 0; int count;
            while ((count = await source.ReadAsync(buffer, token)) > 0)
            {
                written += count;
                if (written > entry.Length) throw new InvalidDataException("Expanded file exceeded its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (written != entry.Length) throw new InvalidDataException("Package file is truncated.");
        }
    }
    public void Launch(InstalledApp app)
    {
        ValidateInstallation(app);
        var executable = Path.GetFullPath(Path.Combine(app.InstallDirectory, app.EntryPoint));
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! })?.Dispose();
    }
    public string VerifiedExecutable(InstalledApp app)
    {
        ValidateInstallation(app);
        return Path.GetFullPath(Path.Combine(app.InstallDirectory, app.EntryPoint));
    }
    public async Task RollbackAsync(InstalledApp previous)
    {
        if (!store.Registry.InstallationHistory.Contains(previous)) throw new InvalidDataException("This installation is not in retained history.");
        ValidateInstallation(previous);
        await store.RecordInstallationAsync(previous);
    }
    private void ValidateInstallation(InstalledApp app)
    {
        var source = GitHubPackages.Sources.Single(s => s.AppId == app.AppId);
        RequireWithin(Path.Combine(AppRoot, source.AppId), app.InstallDirectory); RefuseLinks(app.InstallDirectory);
        var executable = Path.GetFullPath(Path.Combine(app.InstallDirectory, app.EntryPoint));
        RequireWithin(app.InstallDirectory, executable); RefuseLinks(executable);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals(source.Executable, StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("The registered app executable is missing or invalid.");
    }
    internal static void RequireWithin(string root, string path)
    {
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path is outside the app directory.");
    }
    internal static void RefuseLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked installation paths are not supported.");
    }
}
