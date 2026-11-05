using System.IO.Compression;
using System.Security.Cryptography;

namespace Aquarium.Launcher;

public sealed record PayloadFile(string Path, long Size, string Sha256);
public sealed record PayloadManifest(string Version, string Sha256, PayloadFile[] Files);

public static class RuntimeCache
{
    const long MaxBytes = 2L * 1024 * 1024 * 1024;
    public static string Prepare(Stream payload, PayloadManifest manifest, string root)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(manifest.Version, @"^\d+\.\d+\.\d+$") ||
            !ValidHash(manifest.Sha256) || manifest.Files.Length is < 1 or > 5000)
            throw new InvalidDataException("Invalid Aquarium runtime manifest.");
        root = System.IO.Path.GetFullPath(root);
        RefuseLinks(root);
        Directory.CreateDirectory(root);
        var target = System.IO.Path.Combine(root, manifest.Version + "-" + manifest.Sha256[..16]);
        using var mutex = new Mutex(false, @"Local\Aquarium.Runtime." + manifest.Sha256);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromMinutes(5)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new IOException("Another Aquarium window is still preparing this version. Please try again.");
            var expected = new Dictionary<string, PayloadFile>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var item in manifest.Files)
            {
                SafePath(target, item.Path);
                if (item.Size < 0 || !ValidHash(item.Sha256) || !expected.TryAdd(item.Path, item) || (total = checked(total + item.Size)) > MaxBytes)
                    throw new InvalidDataException("Invalid Aquarium file manifest.");
            }
            if (!expected.ContainsKey("Aquarium.exe") || !expected.ContainsKey("Aquarium.pri"))
                throw new InvalidDataException("The Aquarium runtime is incomplete.");
            if (Directory.Exists(target) && Verify(target, expected)) return System.IO.Path.Combine(target, "Aquarium.exe");
            using (var sha = SHA256.Create())
                if (!Convert.ToHexString(sha.ComputeHash(payload)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The embedded Aquarium package failed its integrity check. Download Aquarium again.");
            payload.Position = 0;
            var stage = System.IO.Path.Combine(root, ".preparing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                using var zip = new ZipArchive(payload, ZipArchiveMode.Read, true);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    if (!expected.TryGetValue(entry.FullName, out var item) || !seen.Add(entry.FullName) ||
                        entry.Length != item.Size || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                        throw new InvalidDataException("Unexpected Aquarium payload entry.");
                    var path = SafePath(stage, entry.FullName);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    using var input = entry.Open();
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        if (written > item.Size) throw new InvalidDataException("Aquarium payload exceeds its declared size.");
                        output.Write(buffer, 0, read);
                    }
                    if (written != item.Size) throw new InvalidDataException("Truncated Aquarium payload.");
                }
                if (seen.Count != expected.Count || !Verify(stage, expected)) throw new InvalidDataException("Aquarium runtime verification failed.");
                RefuseLinks(root);
                if (Directory.Exists(target))
                {
                    RefuseLinks(target);
                    Directory.Move(target, target + ".damaged-" + Guid.NewGuid().ToString("N"));
                }
                Directory.Move(stage, target);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            return System.IO.Path.Combine(target, "Aquarium.exe");
        }
        finally { if (owned) mutex.ReleaseMutex(); }
    }

    static bool ValidHash(string value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-fA-F0-9]{64}$");
    static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') ||
            relative.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0))
            throw new InvalidDataException("Unsafe Aquarium runtime path.");
        var result = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!result.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Aquarium runtime path escapes its folder.");
        return result;
    }
    static bool Verify(string folder, Dictionary<string, PayloadFile> files)
    {
        RefuseLinks(folder);
        foreach (var item in files.Values)
        {
            var path = SafePath(folder, item.Path);
            RefuseLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != item.Size) return false;
            using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        // Reject unexpected files as well: DLL loading must use only the packaged runtime.
        int count = 0;
        var folders = new Stack<string>();
        folders.Push(folder);
        while (folders.Count > 0)
            foreach (var path in Directory.EnumerateFileSystemEntries(folders.Pop()))
            {
                RefuseLinks(path);
                if (Directory.Exists(path)) folders.Push(path);
                else count++;
            }
        return count == files.Count;
    }
    static void RefuseLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Aquarium runtime folders cannot be symbolic links or junctions.");
    }
}
