using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Aquarium.Core;

namespace Aquarium;

public sealed record OfflinePackageManifest(int SchemaVersion, string AppId, string Repository,
    string Version, string Sha256, long Size);

/// <summary>One verified, held payload. Disposing removes its private staging directory.</summary>
public sealed class PreparedOfflinePackage : IDisposable
{
    private readonly string directory;
    private readonly FileStream held;
    public AppSource Source { get; }
    public OfflinePackageManifest Manifest { get; }
    internal string PayloadPath { get; }
    internal PreparedOfflinePackage(string directory, string path, FileStream held,
        AppSource source, OfflinePackageManifest manifest)
    { this.directory = directory; PayloadPath = path; this.held = held; Source = source; Manifest = manifest; }

    public void Dispose()
    {
        held.Dispose();
        if (Directory.Exists(directory))
        {
            PathSafety.RefuseLinks(directory);
            Directory.Delete(directory, recursive: true);
        }
    }
}

public static class OfflinePackages
{
    private const long MaxPayload = 512L * 1024 * 1024;
    private const int MaxManifest = 8192;

    public static async Task ExportAsync(AppPackage package, string payload, string destination,
        CancellationToken token)
    {
        if (!GitHubPackages.Sources.Contains(package.Source)) throw new InvalidDataException("Unknown offline app.");
        GitHubPackages.ValidateAssetUrl(package.Source, package.DownloadUrl);
        var manifest = new OfflinePackageManifest(1, package.Source.AppId, package.Source.Repository,
            package.Version, package.Sha256, package.Size);
        Validate(manifest);
        await using var input = new FileStream(payload, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous);
        if (input.Length != manifest.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input, token))
            .Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Offline package checksum or size mismatch.");
        input.Position = 0;
        await AtomicFile.WriteAsync(destination, async (output, cancellation) =>
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            await using (var metadata = archive.CreateEntry("manifest.json").Open())
                await JsonSerializer.SerializeAsync(metadata, manifest, cancellationToken: cancellation);
            await using var content = archive.CreateEntry("package.zip", CompressionLevel.NoCompression).Open();
            await input.CopyToAsync(content, cancellation);
        }, token);
    }

    public static async Task<PreparedOfflinePackage> PrepareAsync(string archivePath, string stagingRoot,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous);
        if (input.Length > MaxPayload + 65536) throw new InvalidDataException("Offline package is too large.");
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count != 2 || archive.Entries.Count(e => e.FullName == "manifest.json") != 1 ||
            archive.Entries.Count(e => e.FullName == "package.zip") != 1)
            throw new InvalidDataException("Offline package must contain only manifest.json and package.zip.");
        var metadata = archive.GetEntry("manifest.json")!;
        if (metadata.Length <= 0 || metadata.Length > MaxManifest) throw new InvalidDataException("Invalid offline manifest size.");
        // Bound actual decompressed bytes too; declared ZIP lengths alone are untrusted.
        await using var metadataStream = metadata.Open();
        using var manifestBytes = new MemoryStream();
        await CopyBoundedAsync(metadataStream, manifestBytes, metadata.Length, token);
        var manifest = JsonSerializer.Deserialize<OfflinePackageManifest>(manifestBytes.ToArray())
            ?? throw new InvalidDataException("Missing offline manifest.");
        var source = Validate(manifest);
        var payload = archive.GetEntry("package.zip")!;
        if (payload.Length != manifest.Size) throw new InvalidDataException("Offline payload size mismatch.");
        stagingRoot = Path.GetFullPath(stagingRoot);
        PathSafety.RefuseLinks(stagingRoot);
        var directory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        PathSafety.RequireWithin(stagingRoot, directory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "package.zip");
        FileStream? held = null;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            await using (var content = payload.Open())
                await CopyBoundedAsync(content, output, manifest.Size, token);
            held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(held, token));
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Offline package checksum mismatch. Nothing was installed.");
            token.ThrowIfCancellationRequested();
            return new PreparedOfflinePackage(directory, path, held, source, manifest);
        }
        catch
        {
            held?.Dispose();
            PathSafety.RefuseLinks(directory);
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private static AppSource Validate(OfflinePackageManifest manifest)
    {
        var source = GitHubPackages.Sources.SingleOrDefault(s => s.AppId == manifest.AppId);
        if (manifest.SchemaVersion != 1 || source is null || source.Repository != manifest.Repository ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 128 || manifest.Version.Any(char.IsControl) ||
            !GitHubPackages.IsHash(manifest.Sha256) || manifest.Size <= 0 || manifest.Size > MaxPayload)
            throw new InvalidDataException("Unsupported or invalid offline package manifest.");
        return source;
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long expected, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            total += count;
            if (total > expected) throw new InvalidDataException("Offline package exceeded its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (total != expected) throw new InvalidDataException("Offline package is incomplete.");
    }
}
