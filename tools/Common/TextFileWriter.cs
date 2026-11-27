using Aquarium.Core;
using Aquarium.Tools;
using System.Security.AccessControl;

namespace Aquarium.TextFiles;

internal static class TextFileWriter
{
    internal static async Task WriteAsync(string path, byte[] bytes, bool replace, Action validate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); PathSafety.RefuseLinks(path);
        string folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
        string temporary = Path.Combine(folder, ".Aquarium-text-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (new FileStream(temporary, options)) { }
            // Restrict the empty temporary file before any document bytes enter it.
            if (replace && OperatingSystem.IsWindows())
            {
                new FileInfo(temporary).SetAccessControl(new FileInfo(path).GetAccessControl(AccessControlSections.Access));
                if ((File.GetAttributes(path) & FileAttributes.Encrypted) != 0) File.Encrypt(temporary);
            }
            await using (var output = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes, token); await output.FlushAsync(token); output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); PathSafety.RefuseLinks(path); validate();
            if (replace)
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                File.Replace(temporary, path, null); // Preserve supported destination metadata; never ignore merge errors.
            }
            else File.Move(temporary, path); // A newly appeared destination must not be overwritten.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
