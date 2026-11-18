namespace Aquarium;

// A file lease covers the entire process lifetime and is released by Windows after a crash.
internal static class ProfileLease
{
    internal static FileStream Acquire(string directory)
    {
        PackageInstaller.RefuseLinks(directory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "profile.lock");
        PackageInstaller.RefuseLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
