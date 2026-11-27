using Aquarium.TextFiles;
using Aquarium.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aquarium.TextEditor;

internal sealed record RecoveryEnvelope(int SchemaVersion, DocumentSnapshot Document);
internal sealed class RecoveryStore : IDisposable
{
    private const int MaximumRecord = 9 * 1024 * 1024;
    private readonly string root;
    private readonly FileStream lease;
    private readonly SemaphoreSlim writer = new(1, 1);
    internal RecoveryStore(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Encrypted recovery requires Windows user-scoped DPAPI.");
        root = Path.GetFullPath(directory); PathSafety.RefuseLinks(root); Directory.CreateDirectory(root);
        var lockPath = Path.Combine(root, "writer.lock"); PathSafety.RefuseLinks(lockPath);
        lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    internal string[] Pending()
    {
        PathSafety.RefuseLinks(root);
        var entries = Directory.EnumerateFiles(root, "*.dtp").Take(21).ToArray();
        if (entries.Length > 20 || entries.Any(p => !Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "N", out _)))
            throw new InvalidDataException("Recovery file limits/identities are unsupported; existing files were preserved.");
        return entries.Select(Path.GetFileNameWithoutExtension).Cast<string>().ToArray();
    }
    private string RecordPath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A recovery identity is required.");
        string path = Path.Combine(root, id.ToString("N") + ".dtp"); PathSafety.RefuseLinks(path); return path;
    }
    internal DocumentSnapshot Read(Guid id)
    {
        string path = RecordPath(id);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumRecord) throw new InvalidDataException("Recovery record exceeds its limit.");
        using var data = new MemoryStream(); file.CopyTo(data);
        var clear = WindowsRecoveryProtection.Unprotect(data.ToArray());
        try
        {
            using var json = JsonDocument.Parse(clear, new JsonDocumentOptions { MaxDepth = 12 }); CheckUnique(json.RootElement);
            var value = JsonSerializer.Deserialize<RecoveryEnvelope>(clear, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Recovery record is empty.");
            if (value.SchemaVersion != 1 || value.Document is null || value.Document.Id != id) throw new InvalidDataException("Unsupported recovery schema/identity.");
            _ = TextDocument.Recover(value.Document); return value.Document;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    internal async Task SaveAsync(DocumentSnapshot snapshot, CancellationToken token = default)
    {
        _ = TextDocument.Recover(snapshot);
        await writer.WaitAsync(token);
        try
        {
            var files = Pending(); string path = RecordPath(snapshot.Id);
            if (File.Exists(path)) _ = Read(snapshot.Id); // Never replace corrupt, unreadable or future recovery records.
            if (files.Length >= 20 && !File.Exists(path)) throw new IOException("Review existing recovery records before creating more than 20.");
            long total = files.Sum(id => new FileInfo(RecordPath(Guid.ParseExact(id, "N"))).Length);
            var clear = JsonSerializer.SerializeToUtf8Bytes(new RecoveryEnvelope(1, snapshot)); byte[] encrypted;
            try { encrypted = WindowsRecoveryProtection.Protect(clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
            if (encrypted.Length > MaximumRecord || total + encrypted.Length > 64 * 1024 * 1024) throw new IOException("Recovery storage limit reached; prior records were preserved.");
            await AtomicFile.WriteAsync(path, (output, cancellation) => output.WriteAsync(encrypted, cancellation).AsTask(), token);
        }
        finally { writer.Release(); }
    }
    internal async Task RemoveAsync(Guid id, CancellationToken token = default, bool discardUnrecognized = false)
    {
        await writer.WaitAsync(token);
        try { token.ThrowIfCancellationRequested(); var path = RecordPath(id); if (File.Exists(path) && !discardUnrecognized) _ = Read(id); File.Delete(path); }
        finally { writer.Release(); }
    }
    private static void CheckUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in element.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Duplicate recovery fields are unsupported."); CheckUnique(field.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckUnique(item);
    }
    public void Dispose() => lease.Dispose();
}

internal static class WindowsRecoveryProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { internal int Length; internal nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    internal static byte[] Protect(byte[] bytes) => Transform(bytes, true);
    internal static byte[] Unprotect(byte[] bytes) => Transform(bytes, false);
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows user-scoped DPAPI is unavailable.");
        if (bytes.Length > 9 * 1024 * 1024) throw new InvalidDataException("Recovery data exceeds its limit.");
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, null, 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new CryptographicException("Windows user-scoped recovery protection is unavailable.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (output.Length < 0 || output.Length > 9 * 1024 * 1024 || output.Data == 0) throw new InvalidDataException("Invalid recovery protection output.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            if (input.Data != 0) { for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0); Marshal.FreeHGlobal(input.Data); }
            if (output.Data != 0) { if (output.Length is >= 0 and <= 9 * 1024 * 1024) for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); _ = LocalFree(output.Data); }
        }
    }
}
