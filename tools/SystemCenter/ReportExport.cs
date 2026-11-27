using Aquarium.Core;
using System.Text.Json;

namespace Aquarium.SystemCenter;

public static class ReportExport
{
    public static void ValidateDestination(string path, string applicationDirectory)
    {
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a .json report filename.");
        var program = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory)) + Path.DirectorySeparatorChar;
        if (path.StartsWith(program, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Save reports outside the application folder so updates/removal cannot replace them.");
    }
    public static async Task SaveAsync(string path, string applicationDirectory, SystemCenterReport snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateDestination(path, applicationDirectory);
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await AtomicFile.WriteAsync(path, (output, cancellation) => output.WriteAsync(data, cancellation).AsTask(), token);
    }
}
