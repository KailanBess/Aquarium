namespace Aquarium;

// A file lease covers the entire process lifetime and is released by Windows after a crash.
internal static class ProfileLease
{
    internal static FileStream Acquire(string directory)
    {
        return Core.ProfileLock.Acquire(directory);
    }
}
