using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aquarium;

public sealed record ProfileBackup(string App, string File, byte[] Data);
public sealed record BackupReview(string[] Apps, int Files, long Bytes);

// Deliberately backs up app records, not programs, games, media or security policy.
public static class SuiteBackup
{
    private const int MaxBytes = 64 * 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AQBACK01");
    public static readonly string[] Apps = ["Aquarium", "FishBowl", "FishNet", "FishTank", "AquariumDeviceInventory", "AquariumWorkspaceLauncher"];
    private static readonly Dictionary<string, string[]> Files = new()
    {
        ["Aquarium"] = ["registry.json", "appearance.json", "appearance-presets.json", "notification-policy.json"],
        ["FishBowl"] = ["library.json", "window-layout.json"],
        ["FishNet"] = ["media-library.json", "playlists.json", "play-queue.json", "linked-library.json", "appearance.json", "watched-folders.json", "continue-watching.json", "audio-output.json", "radio-stations.json", "visual-library.json", "Podcasts/subscriptions.json"],
        ["FishTank"] = ["appearance.json"],
        ["AquariumDeviceInventory"] = ["inventory.json"],
        ["AquariumWorkspaceLauncher"] = ["workspaces.json"]
    };

    private static bool Allowed(string app, string file) => Files.TryGetValue(app, out var known) &&
        (known.Contains(file, StringComparer.Ordinal) || app == "FishNet" &&
         file.StartsWith("watch-positions/", StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal) &&
         GitHubPackages.IsHash(file[16..^5]));

    private static string Target(string root, string app, string file)
    {
        if (!Allowed(app, file)) throw new InvalidDataException("The backup contains an unsupported app record.");
        var target = Path.Combine(Path.GetFullPath(root), app, file.Replace('/', Path.DirectorySeparatorChar));
        PackageInstaller.RequireWithin(Path.Combine(root, app), target);
        PackageInstaller.RefuseLinks(target);
        return target;
    }

    public static List<ProfileBackup> Capture(string root, IEnumerable<string> apps, bool requireReadableJson = true)
    {
        var entries = new List<ProfileBackup>();
        foreach (var app in apps.Distinct())
        {
            if (!Files.TryGetValue(app, out var known)) throw new InvalidDataException("Unknown profile.");
            var names = known.AsEnumerable();
            if (app == "FishNet")
            {
                var watches = Path.Combine(root, app, "watch-positions");
                PackageInstaller.RefuseLinks(watches);
                if (Directory.Exists(watches)) names = names.Concat(Directory.EnumerateFiles(watches, "*.json").Select(p => "watch-positions/" + Path.GetFileName(p)));
            }
            foreach (var name in names)
            {
                var path = Target(root, app, name);
                if (!File.Exists(path)) continue;
                if (entries.Count >= 10020 || new FileInfo(path).Length > MaxBytes - entries.Sum(e => (long)e.Data.Length))
                    throw new InvalidDataException("App records exceed the 64 MB backup limit.");
                var data = File.ReadAllBytes(path);
                if (requireReadableJson) { using var document = JsonDocument.Parse(data); }
                entries.Add(new(app, name, data));
            }
        }
        Validate(entries, requireReadableJson);
        return entries;
    }

    private static void Validate(List<ProfileBackup> entries, bool requireReadableJson = true)
    {
        if (entries.Count > 10020 || entries.Any(e => e is null || e.Data is null || e.App is null || e.File is null) ||
            entries.Sum(e => (long)e.Data.Length) > MaxBytes ||
            entries.Select(e => e.App + "/" + e.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("The backup record list is invalid or too large.");
        foreach (var entry in entries)
        {
            if (!Allowed(entry.App, entry.File)) throw new InvalidDataException("The backup contains an unsupported file.");
            if (requireReadableJson) { using var document = JsonDocument.Parse(entry.Data); }
        }
    }

    public static byte[] Encrypt(List<ProfileBackup> entries, string passphrase)
    {
        Validate(entries, false); // Before-restore evidence can include a corrupt original record.
        if (passphrase.Length < 12 || passphrase.Length > 1024) throw new InvalidDataException("Use a passphrase of 12–1,024 characters.");
        var plain = JsonSerializer.SerializeToUtf8Bytes(entries);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var result = new byte[52 + plain.Length];
        Magic.CopyTo(result, 0); salt.CopyTo(result, 8); nonce.CopyTo(result, 24);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 600000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, result.AsSpan(52), result.AsSpan(36, 16), result.AsSpan(0, 36));
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public static List<ProfileBackup> Decrypt(byte[] envelope, string passphrase)
    {
        if (envelope.Length < 52 || envelope.Length > MaxBytes * 2 || !envelope.AsSpan(0, 8).SequenceEqual(Magic) || passphrase.Length > 1024)
            throw new InvalidDataException("Not a supported Aquarium backup.");
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, envelope.AsSpan(8, 16), 600000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[envelope.Length - 52];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.AsSpan(24, 12), envelope.AsSpan(52), envelope.AsSpan(36, 16), plain, envelope.AsSpan(0, 36));
            var entries = JsonSerializer.Deserialize<List<ProfileBackup>>(plain) ?? throw new InvalidDataException("Empty backup.");
            Validate(entries, false); // Review and Restore independently require readable JSON before applying.
            return entries;
        }
        catch (CryptographicException) { throw new InvalidDataException("The passphrase is incorrect or the backup has changed."); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public static BackupReview Review(List<ProfileBackup> entries)
    {
        Validate(entries);
        return new(entries.Select(e => e.App).Distinct().ToArray(), entries.Count, entries.Sum(e => (long)e.Data.Length));
    }

    public static void Restore(string root, List<ProfileBackup> entries, Action? beforePublish = null)
    {
        Validate(entries);
        var staged = new List<(string Target, string Temporary, byte[]? Previous)>();
        var published = new List<(string Target, byte[]? Previous)>();
        long preservedBytes = 0;
        try
        {
            foreach (var entry in entries)
            {
                var target = Target(root, entry.App, entry.File);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                byte[]? previous = null;
                if (File.Exists(target))
                {
                    preservedBytes = checked(preservedBytes + new FileInfo(target).Length);
                    if (preservedBytes > MaxBytes) throw new InvalidDataException("Existing app records exceed the 64 MB recovery limit.");
                    previous = File.ReadAllBytes(target);
                }
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((target, temporary, previous));
                File.WriteAllBytes(temporary, entry.Data);
            }
            beforePublish?.Invoke();
            foreach (var item in staged)
            {
                PackageInstaller.RefuseLinks(item.Target);
                File.Move(item.Temporary, item.Target, overwrite: true);
                published.Add((item.Target, item.Previous));
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<string>();
            foreach (var item in published.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Previous is null) File.Delete(item.Target);
                    else File.WriteAllBytes(item.Target, item.Previous);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rollbackFailures.Add(Path.GetFileName(item.Target)); }
            }
            if (rollbackFailures.Count > 0) throw new IOException("Restore stopped and some records require recovery from the encrypted before-restore backup: " + string.Join(", ", rollbackFailures), failure);
            throw;
        }
        finally
        {
            foreach (var item in staged)
            {
                if (File.Exists(item.Temporary)) File.Delete(item.Temporary);
                if (item.Previous is not null) CryptographicOperations.ZeroMemory(item.Previous);
            }
        }
    }

    public static void Clear(IEnumerable<ProfileBackup> entries)
    {
        foreach (var entry in entries) CryptographicOperations.ZeroMemory(entry.Data);
    }

    public static void Save(string path, List<ProfileBackup> entries, string passphrase)
    {
        PackageInstaller.RefuseLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = Encrypt(entries, passphrase);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(encrypted); output.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
