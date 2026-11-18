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

    public async Task LoadAsync()
    {
        CanSave = true;
        LoadWarning = null;
        Registry = new();
        Catalog = JsonSerializer.Deserialize<List<CatalogApp>>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")), Json)
            ?? throw new InvalidDataException("The catalog is empty or invalid.");
        if (Catalog.Select(a => a.Id).Distinct().Count() != Catalog.Count)
            throw new InvalidDataException("Catalog IDs must be unique.");
        if (!File.Exists(RegistryPath)) return;
        try
        {
            if (new FileInfo(RegistryPath).Length > 1024 * 1024) throw new InvalidDataException("The installation registry is too large.");
            var registry = JsonSerializer.Deserialize<LocalRegistry>(await File.ReadAllTextAsync(RegistryPath), Json)
                ?? throw new InvalidDataException("Registry is empty.");
            if (registry.SchemaVersion != 1 || registry.LibraryAppIds is null || registry.Installations is null || registry.UninstalledAppIds is null || registry.InstallationHistory is null || registry.ReleaseChannels is null || registry.VersionPins is null)
                throw new InvalidDataException("Unsupported or invalid registry schema.");
            var ids = Catalog.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            bool InvalidInstallation(InstalledApp item) => item is null || !ids.Contains(item.AppId) || string.IsNullOrWhiteSpace(item.Version) || item.Version.Length > 128 ||
                string.IsNullOrWhiteSpace(item.InstallDirectory) || !Path.IsPathFullyQualified(item.InstallDirectory) || item.EntryPoint is null || item.PackageSha256 is null || item.Repository is null;
            if (registry.Installations.Count > ids.Count || registry.InstallationHistory.Count > ids.Count * 5 ||
                registry.Installations.Any(InvalidInstallation) || registry.InstallationHistory.Any(InvalidInstallation) ||
                registry.Installations.Select(i => i.AppId).Distinct().Count() != registry.Installations.Count ||
                registry.LibraryAppIds.Count > ids.Count || registry.UninstalledAppIds.Count > ids.Count ||
                registry.LibraryAppIds.Concat(registry.UninstalledAppIds).Any(id => !ids.Contains(id)) ||
                registry.ReleaseChannels.Any(p => !ids.Contains(p.Key) || !Enum.IsDefined(p.Value)) ||
                registry.VersionPins.Any(p => !ids.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 128))
                throw new InvalidDataException("The registry contains invalid or duplicate app records.");
            Registry = registry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            CanSave = false;
            LoadWarning = $"The local registry could not be read. It has been preserved; library changes are disabled. {ex.Message}";
        }
    }

    public ReleaseChannel GetReleaseChannel(string appId)
    {
        return Registry.ReleaseChannels.GetValueOrDefault(appId) switch
        {
            ReleaseChannel.Stable => ReleaseChannel.Stable,
            ReleaseChannel.Preview => ReleaseChannel.Preview,
            _ => ReleaseChannel.Default
        };
    }

    public async Task SetReleaseChannelAsync(string appId, ReleaseChannel channel)
    {
        await writeGate.WaitAsync();
        try
        {
            if (!CanSave) throw new InvalidOperationException("The registry is read-only after a load error.");
            if (!Catalog.Any(app => app.Id == appId) || !Enum.IsDefined(channel))
                throw new ArgumentException("Unknown app or release channel.");
            var next = new LocalRegistry
            {
                AdditionalFields = Registry.AdditionalFields is null ? null : new(Registry.AdditionalFields),
                LibraryAppIds = Registry.LibraryAppIds.ToList(),
                Installations = Registry.Installations.ToList(),
                UninstalledAppIds = Registry.UninstalledAppIds.ToList(),
                InstallationHistory = Registry.InstallationHistory.ToList(),
                ReleaseChannels = new(Registry.ReleaseChannels),
                VersionPins = new(Registry.VersionPins)
            };
            if (channel == ReleaseChannel.Default) next.ReleaseChannels.Remove(appId);
            else next.ReleaseChannels[appId] = channel;
            await CommitAsync(next);
        }
        finally { writeGate.Release(); }
    }

    public string? GetVersionPin(string appId) => Registry.VersionPins.GetValueOrDefault(appId);

    public bool AllowsPublishedVersion(string appId, string version) =>
        GetVersionPin(appId) is not string pinned || string.Equals(pinned, version, StringComparison.OrdinalIgnoreCase);

    public async Task SetVersionPinAsync(string appId, bool pin)
    {
        await writeGate.WaitAsync();
        try
        {
            if (!CanSave) throw new InvalidOperationException("The registry is read-only after a load error.");
            if (!Catalog.Any(app => app.Id == appId)) throw new ArgumentException("Unknown app.");
            var installed = Registry.Installations.SingleOrDefault(item => item.AppId == appId);
            if (pin && installed is null) throw new InvalidOperationException("Install the app before pinning a version.");
            var next = new LocalRegistry
            {
                AdditionalFields = Registry.AdditionalFields is null ? null : new(Registry.AdditionalFields),
                LibraryAppIds = Registry.LibraryAppIds.ToList(),
                Installations = Registry.Installations.ToList(),
                UninstalledAppIds = Registry.UninstalledAppIds.ToList(),
                InstallationHistory = Registry.InstallationHistory.ToList(),
                ReleaseChannels = new(Registry.ReleaseChannels),
                VersionPins = new(Registry.VersionPins)
            };
            if (pin) next.VersionPins[appId] = installed!.Version;
            else next.VersionPins.Remove(appId);
            await CommitAsync(next);
        }
        finally { writeGate.Release(); }
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
            AdditionalFields = Registry.AdditionalFields is null ? null : new(Registry.AdditionalFields),
                LibraryAppIds = Registry.LibraryAppIds.Where(x => x != id).ToList(),
            Installations = Registry.Installations.ToList(),
            UninstalledAppIds = Registry.UninstalledAppIds.ToList(),
            InstallationHistory = Registry.InstallationHistory.ToList(),
            ReleaseChannels = new(Registry.ReleaseChannels),
            VersionPins = new(Registry.VersionPins)
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
                AdditionalFields = Registry.AdditionalFields is null ? null : new(Registry.AdditionalFields),
                LibraryAppIds = Registry.LibraryAppIds.ToList(),
                Installations = Registry.Installations.Where(item => item.AppId != installation.AppId).ToList(),
                UninstalledAppIds = Registry.UninstalledAppIds.Where(id => id != installation.AppId).ToList(),
                InstallationHistory = Registry.InstallationHistory.Where(item => item.AppId != installation.AppId).Concat(retained).ToList(),
                ReleaseChannels = new(Registry.ReleaseChannels),
                VersionPins = new(Registry.VersionPins)
            };
            next.Installations.Add(installation);
            if (next.VersionPins.ContainsKey(installation.AppId)) next.VersionPins[installation.AppId] = installation.Version;
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
            var next = new LocalRegistry
            {
                AdditionalFields = Registry.AdditionalFields is null ? null : new(Registry.AdditionalFields),
                LibraryAppIds = Registry.LibraryAppIds.ToList(),
                Installations = Registry.Installations.Where(app => app.AppId != installation.AppId).ToList(),
                UninstalledAppIds = Registry.UninstalledAppIds.Append(installation.AppId).Distinct().ToList(),
                InstallationHistory = Registry.InstallationHistory.Where(item => item.AppId != installation.AppId).ToList(),
                ReleaseChannels = new(Registry.ReleaseChannels),
                VersionPins = new(Registry.VersionPins)
            };
            next.VersionPins.Remove(installation.AppId);
            await CommitAsync(next);
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
