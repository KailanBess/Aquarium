namespace Aquarium.Core;

/// <summary>Lexical boundaries and link checks for app-owned files.</summary>
public static class PathSafety
{
    public static void RequireWithin(string root, string path)
    {
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(boundary) ? boundary : boundary + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, comparison))
            throw new InvalidDataException("Path is outside the app directory.");
    }

    public static void RefuseLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // GetAttributes also detects dangling links, which File.Exists can miss.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked application paths are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
