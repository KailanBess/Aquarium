using System.Security.Cryptography;
using System.Text.Json;

namespace FileOrganizer;

internal sealed record ComparisonLimits(int MaximumPaths = 10000, long MaximumFileBytes = 256L * 1024 * 1024, long MaximumReadBytes = 4L * 1024 * 1024 * 1024);
internal sealed record ComparedFile(string Path, long? Bytes, string Code, string? Sha256);
internal sealed record MatchingFiles(long Bytes, string Sha256, string[] Paths);
internal sealed record ComparisonProgress(int CompletedPaths, int TotalPaths, long BytesRead);
internal sealed record FileComparisonReport(int SchemaVersion, DateTimeOffset RecordedAtUtc, int InputPaths, long BytesRead, ComparisonLimits Limits, ComparedFile[] Files, MatchingFiles[] Groups)
{
    public bool Complete => Files.All(f => f.Code is "hashed" or "unique-length");
    public int SkippedPaths => Files.Count(f => f.Code is not ("hashed" or "unique-length"));
    public string Coverage => "Selected paths only; file length and SHA-256 compare content, not visual similarity. Files can change after comparison. Separate hard links may share storage. No files are modified, deleted or uploaded. Skipped paths remain unknown; a partial report cannot establish that the selection has no duplicates.";
}

internal static class FileComparison
{
    internal static async Task ExportNewAsync(string path, FileComparisonReport report, IEnumerable<string> protectedRoots, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path); var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetExtension(full).Equals(".json", StringComparison.OrdinalIgnoreCase) || report.Files.Any(f => f.Path.Equals(full, comparer)) ||
            protectedRoots.Any(root => full.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), comparer) || full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, comparer)))
            throw new InvalidDataException("Choose a new JSON filename outside selected files and the program/profile folders.");
        RefuseLinks(full); if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Existing files are never overwritten; choose a new JSON filename.");
        string folder = Path.GetDirectoryName(full)!; if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("The comparison report exceeds its export limit.");
        string temporary = Path.Combine(folder, ".Aquarium-comparison-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            token.ThrowIfCancellationRequested();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            { await output.WriteAsync(bytes, token); await output.FlushAsync(token); output.Flush(true); }
            token.ThrowIfCancellationRequested(); RefuseLinks(full); File.Move(temporary, full);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static async Task<FileComparisonReport> CompareAsync(IEnumerable<string> paths, CancellationToken token = default,
        IProgress<ComparisonProgress>? progress = null, ComparisonLimits? limits = null)
    {
        limits ??= new();
        if (limits.MaximumPaths is < 1 or > 10000 || limits.MaximumFileBytes is < 1 or > 256L * 1024 * 1024 || limits.MaximumReadBytes is < 0 or > 4L * 1024 * 1024 * 1024)
            throw new ArgumentException("Comparison limits exceed their supported range.");
        token.ThrowIfCancellationRequested();
        string[] input = paths.Take(limits.MaximumPaths + 1).ToArray();
        if (input.Length == 0) throw new ArgumentException("Select files to compare.");
        if (input.Length > limits.MaximumPaths || input.Sum(p => (long)(p?.Length ?? 0)) > 2 * 1024 * 1024)
            throw new InvalidDataException("Choose at most 10,000 paths with up to 2 MiB of path text.");
        var comparer = StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer); var files = new List<ComparedFile>();
        var candidates = new List<(int Index, string Path, long Bytes, DateTime LastWrite)>();
        foreach (var path in input)
        {
            token.ThrowIfCancellationRequested(); string full = path ?? "";
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path)) throw new ArgumentException();
                full = Path.GetFullPath(path); if (!seen.Add(full)) continue;
                RefuseLinks(full); var info = new FileInfo(full);
                if (!info.Exists) { files.Add(new(full, null, "missing-or-unavailable", null)); continue; }
                long length = info.Length; int index = files.Count;
                if (length > limits.MaximumFileBytes) { files.Add(new(full, length, "file-size-limit", null)); continue; }
                DateTime lastWrite = info.LastWriteTimeUtc;
                files.Add(new(full, length, "not-compared", null)); candidates.Add((index, full, length, lastWrite));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { files.Add(new(full, null, "unavailable-invalid-or-linked", null)); }
        }
        var ambiguous = OperatingSystem.IsWindows() ? candidates.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(c => c.Index)).ToHashSet() : [];
        foreach (int index in ambiguous) files[index] = files[index] with { Code = "case-variant-paths" };
        var comparable = candidates.Where(c => !ambiguous.Contains(c.Index)).ToArray();
        var sharedLengths = comparable.GroupBy(c => c.Bytes).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        long bytesRead = 0; int completed = files.Count - comparable.Length;
        foreach (var candidate in comparable)
        {
            token.ThrowIfCancellationRequested(); var entry = files[candidate.Index];
            if (!sharedLengths.Contains(candidate.Bytes)) files[candidate.Index] = entry with { Code = "unique-length" };
            else if (candidate.Bytes > limits.MaximumReadBytes - bytesRead) files[candidate.Index] = entry with { Code = "read-budget-limit" };
            else
            {
                try
                {
                    RefuseLinks(candidate.Path);
                    await using var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (stream.Length != candidate.Bytes || File.GetLastWriteTimeUtc(candidate.Path) != candidate.LastWrite) throw new InvalidDataException("changed");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[81920]; long read = 0;
                    while (read < candidate.Bytes)
                    {
                        token.ThrowIfCancellationRequested();
                        int requested = (int)Math.Min(buffer.Length, candidate.Bytes - read);
                        int count = await stream.ReadAsync(buffer.AsMemory(0, requested), token);
                        if (count == 0) throw new InvalidDataException("changed");
                        hash.AppendData(buffer, 0, count); read += count; bytesRead += count;
                        progress?.Report(new(completed, files.Count, bytesRead));
                    }
                    token.ThrowIfCancellationRequested();
                    if (stream.Length != candidate.Bytes || File.GetLastWriteTimeUtc(candidate.Path) != candidate.LastWrite) throw new InvalidDataException("changed");
                    files[candidate.Index] = entry with { Code = "hashed", Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
                }
                catch (InvalidDataException) { files[candidate.Index] = entry with { Code = "source-changed" }; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { files[candidate.Index] = entry with { Code = "unavailable-or-linked" }; }
            }
            completed++; progress?.Report(new(completed, files.Count, bytesRead));
        }
        token.ThrowIfCancellationRequested();
        var groups = files.Where(f => f.Code == "hashed").GroupBy(f => (f.Bytes, f.Sha256)).Where(g => g.Count() > 1)
            .Select(g => new MatchingFiles(g.Key.Bytes!.Value, g.Key.Sha256!, g.Select(f => f.Path).OrderBy(p => p, comparer).ToArray())).ToArray();
        return new(1, DateTimeOffset.UtcNow, input.Length, bytesRead, limits, files.ToArray(), groups);
    }
    private static void RefuseLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are unsupported."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
    }
}
