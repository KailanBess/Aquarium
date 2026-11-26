namespace Aquarium.Core;

/// <summary>An exclusive process-lifetime lease, without requiring a hub process.</summary>
public static class ProfileLock
{
    public static FileStream Acquire(string directory)
    {
        directory = Path.GetFullPath(directory);
        PathSafety.RefuseLinks(directory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "profile.lock");
        PathSafety.RefuseLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
