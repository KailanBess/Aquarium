namespace Aquarium;

internal sealed class CompanionLeases : IDisposable
{
    private readonly List<FileStream> leases = [];
    internal CompanionLeases(string root, IEnumerable<string> apps)
    {
        try
        {
            foreach (var app in apps.Distinct().Where(a => a is "FishBowl" or "FishNet"))
                leases.Add(ProfileLease.Acquire(Path.Combine(root, app)));
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { foreach (var lease in leases) lease.Dispose(); leases.Clear(); }
}
