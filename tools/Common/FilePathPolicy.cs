namespace Aquarium.Tools;

/// <summary>Conservative ordinary-file path rules; not a filesystem security sandbox.</summary>
internal static class FilePathPolicy
{
    internal static string NormalizeAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Choose an absolute filesystem path up to 4,096 characters.");
        if (OperatingSystem.IsWindows())
        {
            CheckWindows(path);
            string rawRelative = path[Path.GetPathRoot(path)!.Length..];
            foreach (string part in rawRelative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
                if (part is not ("." or "..") && (part.EndsWith(' ') || part.EndsWith('.')))
                    throw new InvalidDataException("Ambiguous trailing-space/dot names are unsupported.");
        }
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) CheckWindows(full);
        return full;
    }
    private static void CheckWindows(string full)
    {
        if (full.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) || full.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) && !(full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(full, @"\A\\\\\?\\[A-Za-z]:\\"))) throw new InvalidDataException("Device namespaces are unsupported.");
        string ordinary = full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + full[8..] : full;
        string root = Path.GetPathRoot(ordinary)!;
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = root.Trim('\\').Split('\\');
            if (parts.Length >= 2 && (parts[1].Equals("pipe", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("IPC$", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("IPC namespaces are unsupported.");
        }
        foreach (string part in ordinary[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            if (part.Contains(':') || System.Text.RegularExpressions.Regex.IsMatch(part.TrimEnd(' ', '.'), @"\A(?:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new InvalidDataException("Device names and alternate streams are unsupported.");
    }
}
