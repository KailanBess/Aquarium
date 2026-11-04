using System.Text.Json;

namespace Aquarium;

public sealed class LocalStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string RegistryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "registry.json");
    public IReadOnlyList<CatalogApp> Catalog { get; private set; } = [];
    public LocalRegistry Registry { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public bool CanSave { get; private set; } = true;
    public IReadOnlyDictionary<string, IAquariumModule> Modules { get; } = new Dictionary<string, IAquariumModule>
    {
        ["security"] = new PendingModule("security"),
        ["emulation"] = new PendingModule("emulation"),
        ["media"] = new PendingModule("media")
    };

    public async Task LoadAsync()
    {
        Catalog = JsonSerializer.Deserialize<List<CatalogApp>>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")), Json)
            ?? throw new InvalidDataException("The catalog is empty or invalid.");
        if (Catalog.Select(a => a.Id).Distinct().Count() != Catalog.Count)
            throw new InvalidDataException("Catalog IDs must be unique.");
        if (!File.Exists(RegistryPath)) return;
        try
        {
            var registry = JsonSerializer.Deserialize<LocalRegistry>(await File.ReadAllTextAsync(RegistryPath), Json)
                ?? throw new InvalidDataException("Registry is empty.");
            if (registry.SchemaVersion != 1 || registry.LibraryAppIds is null || registry.Installations is null)
                throw new InvalidDataException("Unsupported or invalid registry schema.");
            Registry = registry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            CanSave = false;
            LoadWarning = $"The local registry could not be read. It has been preserved; library changes are disabled. {ex.Message}";
        }
    }

    public async Task SetLibraryAsync(string id, bool include)
    {
        if (!CanSave) throw new InvalidOperationException("The registry is read-only after a load error.");
        if (!Catalog.Any(a => a.Id == id)) throw new ArgumentException("Unknown catalog app.");
        var next = new LocalRegistry
        {
            LibraryAppIds = Registry.LibraryAppIds.Where(x => x != id).ToList(),
            Installations = Registry.Installations.ToList()
        };
        if (include) next.LibraryAppIds.Add(id);
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var temporary = RegistryPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, Json));
        File.Move(temporary, RegistryPath, overwrite: true);
        Registry = next; // Commit in memory only after the durable write succeeds.
    }
}
