using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Aquarium;

internal static class AquariumSelfUpdate
{
    internal static string CurrentVersion => typeof(AquariumSelfUpdate).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    internal static bool IsNewer(string tag, string current) =>
        ParseVersion(tag).CompareTo(ParseVersion(current)) > 0;

    private static (Version Version, int Preview) ParseVersion(string value)
    {
        var match = Regex.Match(value, @"\Av?(\d+\.\d+\.\d+)(?:-preview(?:\.(\d+))?)?\z");
        if (!match.Success) throw new InvalidDataException($"Unsupported Aquarium version: {value}");
        var preview = match.Groups[2];
        return (Version.Parse(match.Groups[1].Value),
            preview.Success ? int.Parse(preview.Value) : value.Contains("-preview") ? 0 : int.MaxValue);
    }

    internal static void CreateShortcuts(string directory)
    {
        var executable = Path.Combine(directory, "Aquarium.exe");
        var icon = executable;
        if (!File.Exists(executable))
            throw new InvalidDataException("Aquarium executable is missing.");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path)) throw new IOException($"Windows did not provide the {folder} folder.");
            dynamic shortcut = shell.CreateShortcut(Path.Combine(path, "Aquarium.lnk"));
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = directory;
            shortcut.Arguments = "";
            shortcut.IconLocation = icon + ",0";
            shortcut.Description = "Aquarium";
            shortcut.Save();
        }
    }

    internal static async Task<string> StageAsync(AppPackage package, string file, CancellationToken token)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "Updates");
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        PackageInstaller.RefuseLinks(directory);
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Aquarium update checksum mismatch.");
        stream.Position = 0;
        Directory.CreateDirectory(directory);
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            await PackageInstaller.ExtractAsync(archive, directory, token);
            if (archive.Entries.Count != 1 || archive.Entries[0].FullName != "Aquarium.exe")
                throw new InvalidDataException("Expected a single Aquarium launcher.");
            await PackageInstaller.ExtractAsync(archive, directory, token);
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(directory, "Aquarium.exe")).ProductVersion?.Split('+')[0];
            if (version != package.Version.TrimStart('v'))
                throw new InvalidDataException("Aquarium launcher version does not match the release tag.");
            token.ThrowIfCancellationRequested();
            return directory;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }
}
