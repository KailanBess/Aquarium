namespace Aquarium;

public sealed record CatalogApp(string Id, string Name, string Category, string Summary,
    string Description, string ModuleId, string Stage);

// A library bookmark is deliberately separate from an installed package.
public sealed record InstalledApp(string AppId, string Version, string InstallDirectory, DateTimeOffset InstalledAt,
    string EntryPoint = "", string PackageSha256 = "", string Repository = "");
public sealed class LocalRegistry
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> LibraryAppIds { get; set; } = [];
    public List<InstalledApp> Installations { get; set; } = [];
    public List<string> UninstalledAppIds { get; set; } = [];
    public List<InstalledApp> InstallationHistory { get; set; } = [];
    public Dictionary<string, ReleaseChannel> ReleaseChannels { get; set; } = [];
    public Dictionary<string, string> VersionPins { get; set; } = [];
}

