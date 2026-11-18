namespace Aquarium;

internal sealed class CompanionLeases : IDisposable
{
    private readonly List<FileStream> leases = [];
    internal CompanionLeases(string root, IEnumerable<string> apps)
    {
        try
        {
            foreach (var app in apps.Distinct())
            {
                if (app is "FishBowl" or "FishNet") leases.Add(ProfileLease.Acquire(Path.Combine(root, app)));
                else if (app is "AquariumDeviceInventory" or "AquariumWorkspaceLauncher")
                {
                    var folder = Path.Combine(root, app);
                    PackageInstaller.RefuseLinks(folder); Directory.CreateDirectory(folder);
                    var file = Path.Combine(folder, app == "AquariumDeviceInventory" ? "inventory.json.lock" : "workspaces.json.lock");
                    PackageInstaller.RefuseLinks(file);
                    leases.Add(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { foreach (var lease in leases) lease.Dispose(); leases.Clear(); }
}
