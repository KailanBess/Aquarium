using System.Text.Json;

namespace Aquarium;

public sealed class LocalStore
{
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string RegistryPath { get; }
    public LocalStore(string? dataDirectory = null) => RegistryPath = Path.Combine(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium"), "registry.json");
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
            if (registry.SchemaVersion != 1 || registry.LibraryAppIds is null || registry.Installations is null || registry.UninstalledAppIds is null || registry.InstallationHistory is null)
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
        await writeGate.WaitAsync();
        try
        {
        if (!CanSave) throw new InvalidOperationException("The registry is read-only after a load error.");
        if (!Catalog.Any(a => a.Id == id)) throw new ArgumentException("Unknown catalog app.");
        var next = new LocalRegistry
        {
            LibraryAppIds = Registry.LibraryAppIds.Where(x => x != id).ToList(),
            Installations = Registry.Installations.ToList(),
            UninstalledAppIds = Registry.UninstalledAppIds.ToList(),
            InstallationHistory = Registry.InstallationHistory.ToList()
        };
        if (include) next.LibraryAppIds.Add(id);
        await CommitAsync(next);
        }
        finally { writeGate.Release(); }
    }

    public async Task RecordInstallationAsync(InstalledApp installation)
    {
        await writeGate.WaitAsync();
        try
        {
            if (!CanSave) throw new InvalidOperationException("The registry is read-only after a load error.");
            if (!Catalog.Any(a => a.Id == installation.AppId)) throw new InvalidOperationException("Unknown app.");
            var retained = Registry.Installations.Concat(Registry.InstallationHistory)
                .Where(item => item.AppId == installation.AppId && item.InstallDirectory != installation.InstallDirectory)
                .DistinctBy(item => item.InstallDirectory, StringComparer.OrdinalIgnoreCase).Take(5);
            var next = new LocalRegistry
            {
                LibraryAppIds = Registry.LibraryAppIds.ToList(),
                Installations = Registry.Installations.Where(item => item.AppId != installation.AppId).ToList(),
                UninstalledAppIds = Registry.UninstalledAppIds.Where(id => id != installation.AppId).ToList(),
                InstallationHistory = Registry.InstallationHistory.Where(item => item.AppId != installation.AppId).Concat(retained).ToList()
            };
            next.Installations.Add(installation);
            if (!next.LibraryAppIds.Contains(installation.AppId)) next.LibraryAppIds.Add(installation.AppId);
            await CommitAsync(next);
        }
        finally { writeGate.Release(); }
    }

    internal async Task RecordUninstallAsync(InstalledApp installation)
    {
        await writeGate.WaitAsync();
        try
        {
            if (!CanSave || !Registry.Installations.Contains(installation))
                throw new InvalidOperationException("The installation record changed. Refresh Aquarium and try again.");
            await CommitAsync(new LocalRegistry
            {
                LibraryAppIds = Registry.LibraryAppIds.ToList(),
                Installations = Registry.Installations.Where(app => app.AppId != installation.AppId).ToList(),
                UninstalledAppIds = Registry.UninstalledAppIds.Append(installation.AppId).Distinct().ToList(),
                InstallationHistory = Registry.InstallationHistory.Where(item => item.AppId != installation.AppId).ToList()
            });
        }
        finally { writeGate.Release(); }
    }

    private async Task CommitAsync(LocalRegistry next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var temporary = RegistryPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, Json));
        File.Move(temporary, RegistryPath, overwrite: true);
        Registry = next; // Commit in memory only after the durable write succeeds.
    }
}
