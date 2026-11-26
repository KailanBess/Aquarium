using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aquarium.Core;

/// <summary>A loaded settings revision. Keep it until a successful save returns the replacement.</summary>
public sealed class SettingsSnapshot<T> where T : class
{
    internal string Owner { get; }
    internal string? Revision { get; }
    internal JsonObject Document { get; }
    public T Value { get; }
    internal SettingsSnapshot(string owner, string? revision, JsonObject document, T value)
    { Owner = owner; Revision = revision; Document = document; Value = value; }
}

/// <summary>Bounded, versioned JSON settings with explicit migrations and optimistic concurrency.</summary>
public sealed class JsonSettingsStore<T> where T : class, new()
{
    private const string VersionKey = "_schemaVersion";
    private readonly string path;
    private readonly int version;
    private readonly int maximumBytes;
    private readonly IReadOnlyDictionary<int, Action<JsonObject>> migrations;
    private readonly Func<T, bool>? validate;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public JsonSettingsStore(string path, int schemaVersion,
        IReadOnlyDictionary<int, Action<JsonObject>>? migrations = null,
        Func<T, bool>? validate = null, int maximumBytes = 1024 * 1024)
    {
        if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (maximumBytes < 2 || maximumBytes > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.path = Path.GetFullPath(path); version = schemaVersion; this.maximumBytes = maximumBytes;
        this.migrations = migrations is null ? new Dictionary<int, Action<JsonObject>>() : new Dictionary<int, Action<JsonObject>>(migrations);
        this.validate = validate;
    }

    public SettingsSnapshot<T> Load()
    {
        var bytes = ReadCurrent();
        if (bytes is null)
        {
            var defaults = new T(); Validate(defaults);
            var document = JsonSerializer.SerializeToNode(defaults, Json) as JsonObject
                ?? throw new InvalidDataException("Settings must be a JSON object.");
            if (document.ContainsKey(VersionKey)) throw new InvalidDataException("The settings model uses the reserved schema key.");
            document[VersionKey] = version;
            return new(path, null, document, defaults);
        }
        using (var parsed = JsonDocument.Parse(bytes)) EnsureUniqueProperties(parsed.RootElement);
        var raw = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Settings must be a JSON object.");
        int storedVersion = 0;
        if (raw.TryGetPropertyValue(VersionKey, out var node) &&
            (node is not JsonValue value || !value.TryGetValue(out storedVersion)))
            throw new InvalidDataException("Invalid settings schema version.");
        if (storedVersion < 0 || storedVersion > version) throw new InvalidDataException("This settings schema is not supported by this app version.");
        while (storedVersion < version)
        {
            if (!migrations.TryGetValue(storedVersion, out var migrate))
                throw new InvalidDataException("A required settings migration is unavailable.");
            migrate(raw); // Read-only on disk: a migration is published only by an explicit SaveAsync.
            raw[VersionKey] = ++storedVersion;
        }
        var settings = raw.Deserialize<T>(Json) ?? throw new InvalidDataException("Settings are empty.");
        Validate(settings);
        return new(path, Hash(bytes), raw, settings);
    }

    public async Task<SettingsSnapshot<T>> SaveAsync(SettingsSnapshot<T> expected, T next,
        bool keepPrevious = false, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(next);
        if (!string.Equals(expected.Owner, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The settings revision belongs to another file.", nameof(expected));
        Validate(next);
        var updates = JsonSerializer.SerializeToNode(next, Json) as JsonObject
            ?? throw new InvalidDataException("Settings must be a JSON object.");
        if (updates.ContainsKey(VersionKey)) throw new InvalidDataException("The settings model uses the reserved schema key.");
        var merged = (JsonObject)expected.Document.DeepClone();
        Merge(merged, updates); merged[VersionKey] = version;
        return await CommitAsync(expected, merged, next, keepPrevious, token).ConfigureAwait(false);
    }

    public async Task<SettingsSnapshot<T>> RestorePreviousAsync(SettingsSnapshot<T> expected, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var backupStore = new JsonSettingsStore<T>(path + ".previous", version, migrations, validate, maximumBytes);
        var restored = backupStore.Load();
        if (restored.Revision is null) throw new FileNotFoundException("No previous settings are available.");
        return await CommitAsync(expected, restored.Document, restored.Value, false, token).ConfigureAwait(false);
    }

    private async Task<SettingsSnapshot<T>> CommitAsync(SettingsSnapshot<T> expected, JsonObject merged, T next,
        bool keepPrevious, CancellationToken token)
    {
        if (!string.Equals(expected.Owner, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The settings revision belongs to another file.", nameof(expected));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(merged, Json);
        if (bytes.Length > maximumBytes) throw new InvalidDataException("Settings exceed the supported size limit.");
        token.ThrowIfCancellationRequested();
        PathSafety.RefuseLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lockPath = path + ".lock"; PathSafety.RefuseLinks(lockPath);
        using var writer = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = ReadCurrent();
        if ((previous is null ? null : Hash(previous)) != expected.Revision)
            throw new IOException("Settings changed since they were loaded. Reload them before saving.");
        if (keepPrevious)
        {
            // First-save undo restores the defaults that were shown to the user.
            var backup = previous ?? JsonSerializer.SerializeToUtf8Bytes(expected.Document, Json);
            await AtomicFile.WriteAsync(path + ".previous", (output, cancellation) => output.WriteAsync(backup, cancellation).AsTask(), token).ConfigureAwait(false);
        }
        await AtomicFile.WriteAsync(path, (output, cancellation) => output.WriteAsync(bytes, cancellation).AsTask(), token).ConfigureAwait(false);
        return new(path, Hash(bytes), merged, next);
    }

    private byte[]? ReadCurrent()
    {
        PathSafety.RefuseLinks(path);
        FileStream input;
        try { input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (input)
        using (var bytes = new MemoryStream())
        {
            if (input.Length > maximumBytes) throw new InvalidDataException("Settings exceed the supported size limit.");
            var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer)) != 0)
            {
                if (bytes.Length + count > maximumBytes) throw new InvalidDataException("Settings exceed the supported size limit.");
                bytes.Write(buffer, 0, count);
            }
            return bytes.ToArray();
        }
    }

    private void Validate(T value)
    {
        if (validate is not null && !validate(value)) throw new InvalidDataException("Settings validation failed.");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void EnsureUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Settings contain duplicate property names.");
                EnsureUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) EnsureUniqueProperties(item);
    }
    private static void Merge(JsonObject destination, JsonObject updates)
    {
        foreach (var entry in updates)
            if (entry.Value is JsonObject nested && destination[entry.Key] is JsonObject existing) Merge(existing, nested);
            else destination[entry.Key] = entry.Value?.DeepClone();
    }
}
