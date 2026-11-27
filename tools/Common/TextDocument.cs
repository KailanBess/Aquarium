using Aquarium.Core;
using Aquarium.Tools;
using System.Security.Cryptography;
using System.Text;

namespace Aquarium.TextFiles;

internal enum TextEncodingKind { Utf8, Utf8Bom, Utf16LittleEndian, Utf16BigEndian }
internal sealed record DocumentSnapshot(Guid Id, string? Path, string? SourceSha256, string Text, TextEncodingKind Encoding, string Newline, bool Dirty);

internal sealed class TextDocument
{
    internal const int MaximumBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal Guid Id { get; private set; } = Guid.NewGuid();
    internal string? Path { get; private set; }
    internal string? SourceSha256 { get; private set; }
    internal string Text { get; private set; } = "";
    internal TextEncodingKind Encoding { get; private set; }
    internal string Newline { get; private set; } = "CRLF";
    internal bool Dirty { get; private set; }
    internal string DisplayName => Path is null ? "Untitled" : System.IO.Path.GetFileName(Path);
    internal static TextDocument Open(string path)
    {
        var full = FilePathPolicy.NormalizeAbsolute(path); PathSafety.RefuseLinks(full);
        var bytes = ReadBounded(full); int skip = 0; TextEncodingKind kind = TextEncodingKind.Utf8;
        System.Text.Encoding decoder = Utf8;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { kind = TextEncodingKind.Utf8Bom; skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { kind = TextEncodingKind.Utf16LittleEndian; decoder = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { kind = TextEncodingKind.Utf16BigEndian; decoder = new UnicodeEncoding(true, false, true); skip = 2; }
        string text;
        try { text = decoder.GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Use valid UTF-8 or BOM-marked UTF-16 text. Source bytes were preserved.", ex); }
        if (text.Contains('\0')) throw new InvalidDataException("Binary/NUL text is unsupported; source bytes were preserved.");
        var normalized = Normalize(text);
        var kinds = new[] { text.Contains("\r\n"), text.Replace("\r\n", "").Contains('\n'), text.Replace("\r\n", "").Contains('\r') };
        var newline = kinds.Count(x => x) > 1 ? "Mixed" : kinds[0] ? "CRLF" : kinds[2] ? "CR" : "LF";
        return new TextDocument { Path = full, SourceSha256 = Hash(bytes), Text = normalized, Encoding = kind, Newline = newline };
    }
    internal void Edit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = Normalize(text);
        if (text.Length > MaximumBytes || text.Contains('\0')) throw new InvalidDataException("The document exceeds its text limit or contains NUL characters.");
        if (Text != text) { Text = text; Dirty = true; }
    }
    internal DocumentSnapshot Snapshot() => new(Id, Path, SourceSha256, Text, Encoding, Newline, Dirty);
    internal static TextDocument Recover(DocumentSnapshot value)
    {
        if (value.Id == Guid.Empty || !Enum.IsDefined(value.Encoding) || value.Newline is not ("LF" or "CRLF" or "CR" or "Mixed") ||
            value.Path is not null && !System.IO.Path.IsPathFullyQualified(value.Path) ||
            value.SourceSha256 is not null && (value.SourceSha256.Length != 64 || value.SourceSha256.Any(c => !Uri.IsHexDigit(c))))
            throw new InvalidDataException("Unsupported recovery document.");
        var document = new TextDocument { Id = value.Id, Path = value.Path, SourceSha256 = value.SourceSha256, Encoding = value.Encoding, Newline = value.Newline };
        document.Edit(value.Text); document.Dirty = true; return document;
    }
    internal async Task SaveAsync(string destination, TextEncodingKind encoding, string newline, bool replaceReviewedDestination, CancellationToken token = default)
    {
        if (!Enum.IsDefined(encoding) || newline is not ("LF" or "CRLF" or "CR" or "Mixed")) throw new ArgumentException("Choose an encoding and newline format.");
        var full = FilePathPolicy.NormalizeAbsolute(destination); PathSafety.RefuseLinks(full);
        bool same = Path is not null && full.Equals(Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        // An unchanged save is a no-op, retaining exact original bytes including mixed newlines.
        if (same && !Dirty && encoding == Encoding && Newline == newline) return;
        if (newline == "Mixed") throw new InvalidOperationException("Choose LF, CRLF or CR explicitly before converting or copying mixed-newline text.");
        string? expected = null;
        if (File.Exists(full))
        {
            expected = Hash(ReadBounded(full));
            if (same && expected != SourceSha256) throw new IOException("The source changed outside this editor. Use Save as to preserve both versions.");
            if (!same && !replaceReviewedDestination) throw new IOException("Review the existing destination before replacing it.");
        }
        else if (same && SourceSha256 is not null) throw new IOException("The source was moved or deleted. Use Save as.");
        var savedText = Text;
        var body = savedText.Replace("\n", newline == "CRLF" ? "\r\n" : newline == "CR" ? "\r" : "\n");
        System.Text.Encoding encoder = encoding is TextEncodingKind.Utf16LittleEndian or TextEncodingKind.Utf16BigEndian ? new UnicodeEncoding(encoding == TextEncodingKind.Utf16BigEndian, true, true) : new UTF8Encoding(encoding == TextEncodingKind.Utf8Bom, true);
        var bytes = encoder.GetPreamble().Concat(encoder.GetBytes(body)).ToArray();
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Encoded text exceeds 1 MiB. Choose another destination/document size; prior bytes were preserved.");
        await TextFileWriter.WriteAsync(full, bytes, expected is not null, () => {
            PathSafety.RefuseLinks(full);
            if (expected is null ? File.Exists(full) : !File.Exists(full) || Hash(ReadBounded(full)) != expected)
                throw new IOException("The destination changed while preparing the save; prior bytes were preserved.");
        }, token);
        Path = full; SourceSha256 = Hash(bytes); Encoding = encoding; Newline = newline; Dirty = Text != savedText;
    }
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] ReadBounded(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > MaximumBytes) throw new InvalidDataException("Open text files up to 1 MiB.");
        using var data = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = input.Read(buffer)) != 0) { if (data.Length + count > MaximumBytes) throw new InvalidDataException("The text file exceeds 1 MiB."); data.Write(buffer, 0, count); }
        return data.ToArray();
    }
}
