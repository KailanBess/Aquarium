using System.Text.Json;

namespace Aquarium.Core;

/// <summary>Publishes a complete file through a same-directory rename.</summary>
public static class AtomicFile
{
    public static Task WriteJsonAsync<T>(string path, T value, JsonSerializerOptions? options = null,
        CancellationToken token = default) => WriteAsync(path,
            (output, cancellation) => JsonSerializer.SerializeAsync(output, value, options, cancellation), token);

    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        path = Path.GetFullPath(path);
        token.ThrowIfCancellationRequested();
        PathSafety.RefuseLinks(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await write(output, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            PathSafety.RefuseLinks(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // Only the uniquely named file created by this attempt is cleaned up.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
