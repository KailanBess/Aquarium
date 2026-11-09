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
}

public interface IAquariumModule
{
    string Id { get; }
    bool CanLaunch { get; }
    string Status { get; }
    Task LaunchAsync(InstalledApp installation);
}

// Adapters replace these only after inspection of the corresponding project.
public sealed class PendingModule(string id) : IAquariumModule
{
    public string Id => id;
    public bool CanLaunch => false;
    public string Status => "No verified executable or package is configured.";
    public Task LaunchAsync(InstalledApp installation) =>
        Task.FromException(new NotSupportedException(Status));
}

public interface IPackageInstaller
{
    Task InstallAsync(CatalogApp app, CancellationToken cancellationToken);
}

public sealed class UnavailablePackageInstaller : IPackageInstaller
{
    public Task InstallAsync(CatalogApp app, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("No approved package is available."));
}
