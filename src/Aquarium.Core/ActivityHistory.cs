using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aquarium.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ActivityLevel>))]
public enum ActivityLevel { Information, Warning, Error }
public sealed record ActivityEvent(Guid Id, DateTimeOffset RecordedAtUtc, ActivityLevel Level, string Code, string ApplicationVersion);
public sealed record ActivitySnapshot(int SchemaVersion, string ApplicationId, long DroppedEntries, IReadOnlyList<ActivityEvent> Events);

/// <summary>Bounded local symbolic-event history. No free-text messages, paths, stacks or automatic uploads.</summary>
public sealed class ActivityHistory
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path, applicationId;
    private readonly HashSet<string> allowedCodes;
    private readonly int capacity;
    private readonly SemaphoreSlim writer = new(1, 1);
    public ActivityHistory(string path, string applicationId, IEnumerable<string> allowedCodes, int capacity = 500)
    {
        ArgumentNullException.ThrowIfNull(allowedCodes);
        if (!Symbol(applicationId) || capacity is < 1 or > 1000) throw new ArgumentException("Use a stable app identity and a capacity from 1 to 1,000.");
        var codes = allowedCodes.Take(129).ToArray();
        if (codes.Length is < 1 or > 128 || codes.Any(c => !Symbol(c)) || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
            throw new ArgumentException("Declare up to 128 unique fixed event codes.", nameof(allowedCodes));
        this.path = Path.GetFullPath(path); this.applicationId = applicationId; this.capacity = capacity;
        this.allowedCodes = new(codes, StringComparer.Ordinal);
    }
    public ActivitySnapshot Read()
    {
        var document = Load();
        var events = Entries(document).Select(node => node!.Deserialize<ActivityEvent>(Json)!).ToArray();
        return new(1, applicationId, document["DroppedEntries"]!.GetValue<long>(), Array.AsReadOnly(events));
    }
    public async Task<ActivityEvent> AppendAsync(string code, ActivityLevel level, string applicationVersion, CancellationToken token = default)
    {
        if (!allowedCodes.Contains(code) || !Enum.IsDefined(level) || !VersionText(applicationVersion))
            throw new ArgumentException("Use a declared fixed event code, level and numeric app version.");
        await writer.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested(); PathSafety.RefuseLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var leasePath = path + ".writer.lock"; PathSafety.RefuseLinks(leasePath);
            using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var document = Load(); var entries = Entries(document);
            var entry = new ActivityEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, level, code, applicationVersion);
            entries.Add(JsonSerializer.SerializeToNode(entry, Json));
            long dropped = document["DroppedEntries"]!.GetValue<long>();
            while (entries.Count > capacity)
            {
                if (dropped == long.MaxValue) throw new InvalidDataException("Activity retention counter is exhausted.");
                entries.RemoveAt(0); dropped++;
            }
            document["DroppedEntries"] = dropped;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("Activity history exceeds its size limit; existing history was preserved.");
            await AtomicFile.WriteAsync(path, (output, cancellation) => output.WriteAsync(bytes, cancellation).AsTask(), token).ConfigureAwait(false);
            return entry;
        }
        finally { writer.Release(); }
    }
    private static bool Symbol(string? text) => text is not null && Regex.IsMatch(text, "\\A[a-z][a-z0-9._-]{0,63}\\z");
    private static bool VersionText(string? text) => text is not null && text.Length <= 32 && Regex.IsMatch(text, "\\A[0-9]+(?:\\.[0-9]+){1,3}\\z") && Version.TryParse(text, out _);
    private static JsonArray Entries(JsonObject document) => (JsonArray)document["Events"]!;
    private JsonObject Load()
    {
        PathSafety.RefuseLinks(path);
        byte[] bytes;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length > MaximumBytes) throw new InvalidDataException("Activity history exceeds its size limit and was preserved.");
            using var data = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer)) != 0)
            {
                if (data.Length + count > MaximumBytes) throw new InvalidDataException("Activity history exceeds its size limit and was preserved.");
                data.Write(buffer, 0, count);
            }
            bytes = data.ToArray();
        }
        catch (FileNotFoundException) { return Empty(); }
        catch (DirectoryNotFoundException) { return Empty(); }
        using (var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 })) CheckUnique(parsed.RootElement);
        var document = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject
            ?? throw new InvalidDataException("Activity history must be a JSON object.");
        try
        {
            if (document["SchemaVersion"]!.GetValue<int>() != 1 || document["ApplicationId"]!.GetValue<string>() != applicationId ||
                document["DroppedEntries"]!.GetValue<long>() < 0 || document["Events"] is not JsonArray events || events.Count > 1000)
                throw new InvalidDataException("Unsupported or invalid activity history; the file was preserved.");
            var ids = new HashSet<Guid>();
            foreach (var node in events)
            {
                if (node is not JsonObject) throw new InvalidDataException("Invalid activity event.");
                var item = node.Deserialize<ActivityEvent>(Json);
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.RecordedAtUtc.Offset != TimeSpan.Zero ||
                    !Enum.IsDefined(item.Level) || !Symbol(item.Code) || !VersionText(item.ApplicationVersion)) throw new InvalidDataException("Invalid activity event.");
                foreach (var key in new[] { "Id", "RecordedAtUtc", "Level", "Code", "ApplicationVersion" })
                    if (!((JsonObject)node).ContainsKey(key)) throw new InvalidDataException("Incomplete activity event.");
            }
            return document;
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException or JsonException)
        { throw new InvalidDataException("Activity history is invalid and was preserved.", ex); }
    }
    private JsonObject Empty() => new() { ["SchemaVersion"] = 1, ["ApplicationId"] = applicationId, ["DroppedEntries"] = 0L, ["Events"] = new JsonArray() };
    private static void CheckUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in value.EnumerateObject())
            { if (!names.Add(item.Name)) throw new InvalidDataException("Duplicate activity fields are unsupported."); CheckUnique(item.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckUnique(item);
    }
}
