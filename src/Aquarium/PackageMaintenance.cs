using System.Diagnostics;

namespace Aquarium;

public sealed record UnusedInstallation(string AppId, string Directory, long Bytes);

public sealed partial class PackageInstaller
{
    internal static void RequireAppClosed(string appId)
    {
        var source = GitHubPackages.Sources.Single(s => s.AppId == appId);
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(source.Executable));
        try
        {
            if (processes.Any(p => !p.HasExited))
                throw new InvalidOperationException($"Close {Path.GetFileNameWithoutExtension(source.Executable)} first.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    // Keep every registered version, including rollback history. Never infer ownership from age.
    public IReadOnlyList<UnusedInstallation> FindUnusedInstallations()
    {
        var retained = store.Registry.Installations.Concat(store.Registry.InstallationHistory)
            .Select(i => Path.GetFullPath(i.InstallDirectory)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<UnusedInstallation>();
        foreach (var source in GitHubPackages.Sources)
        {
            var root = Path.Combine(AppRoot, source.AppId);
            RefuseLinks(root);
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || retained.Contains(directory)) continue;
                var bytes = InspectUnusedDirectory(directory);
                result.Add(new(source.AppId, directory, bytes));
            }
        }
        return result;
    }

    private static long InspectUnusedDirectory(string directory)
    {
        RefuseLinks(directory);
        long bytes = 0;
        var folders = new Stack<string>(); folders.Push(directory);
        while (folders.TryPop(out var folder))
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                RefuseLinks(entry);
                if (Path.GetFileName(entry).Equals("FishBowlData", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(entry).Equals("portable.flag", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("An unused installation contains portable data. Move that profile before cleanup.");
                if (Directory.Exists(entry)) folders.Push(entry);
                else bytes = checked(bytes + new FileInfo(entry).Length);
            }
        return bytes;
    }

    public async Task RemoveUnusedInstallationAsync(UnusedInstallation candidate)
    {
        if (!store.CanSave) throw new InvalidOperationException("Repair the read-only registry before cleaning storage.");
        var root = Path.Combine(AppRoot, candidate.AppId);
        RequireWithin(root, candidate.Directory);
        if (!Path.GetDirectoryName(candidate.Directory)!.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(candidate.Directory), "N", out _))
            throw new InvalidDataException("Only managed version folders can be cleaned.");
        RequireAppClosed(candidate.AppId);
        if (!FindUnusedInstallations().Any(item => item.Directory.Equals(candidate.Directory, StringComparison.OrdinalIgnoreCase) && item.Bytes == candidate.Bytes))
            throw new InvalidOperationException("Storage changed since the preview. Review it again.");
        // Recheck immediately before deleting; no profile or retained version is a cleanup candidate.
        _ = InspectUnusedDirectory(candidate.Directory);
        await Task.Run(() => Directory.Delete(candidate.Directory, recursive: true));
    }
}
