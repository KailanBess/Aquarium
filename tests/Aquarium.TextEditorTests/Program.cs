using Aquarium.TextFiles;
using Aquarium.TextEditor;
using System.Security.Cryptography;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "Aquarium-text-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
async Task Reject<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
try
{
    string path = Path.Combine(root, "source.txt");
    foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
    {
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("Hello Ω\r\nsecond\nthird\rfourth")).ToArray();
        await File.WriteAllBytesAsync(path, bytes); var document = TextDocument.Open(path);
        Check(document.Newline == "Mixed" && !document.Dirty && document.Text == "Hello Ω\nsecond\nthird\nfourth", "Encoding/newline import changed text");
        await document.SaveAsync(path, document.Encoding, "Mixed", false);
        Check((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes), "Unedited mixed-newline save changed source bytes");
        document.Edit("changed Ω\nline"); await document.SaveAsync(path, document.Encoding, "CRLF", false);
        Check(!document.Dirty && TextDocument.Open(path).Text == "changed Ω\nline", "Encoded save changed text or dirty state");
    }
    await File.WriteAllTextAsync(path, "original"); var edited = TextDocument.Open(path); edited.Edit("our edit");
    await File.WriteAllTextAsync(path, "external edit");
    await Reject<IOException>(() => edited.SaveAsync(path, TextEncodingKind.Utf8, "LF", false));
    Check(await File.ReadAllTextAsync(path) == "external edit" && edited.Dirty, "Conflict overwrote external edits");
    string copy = Path.Combine(root, "copy.txt"); await File.WriteAllTextAsync(copy, "prior copy");
    await Reject<IOException>(() => edited.SaveAsync(copy, TextEncodingKind.Utf8, "LF", false));
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel(); await Reject<OperationCanceledException>(() => edited.SaveAsync(copy, TextEncodingKind.Utf8, "LF", true, canceled.Token));
        Check(await File.ReadAllTextAsync(copy) == "prior copy", "Canceled save modified existing destination");
    }
    await edited.SaveAsync(copy, TextEncodingKind.Utf8Bom, "LF", true);
    Check(TextDocument.Open(copy).Text == "our edit" && await File.ReadAllTextAsync(path) == "external edit", "Save as lost source or did not publish reviewed copy");
    await File.WriteAllBytesAsync(path, [0xFF, 0xFE, 0x00]); await Reject<InvalidDataException>(() => { TextDocument.Open(path); return Task.CompletedTask; });
    await File.WriteAllBytesAsync(path, [0xC3, 0x28]); await Reject<InvalidDataException>(() => { TextDocument.Open(path); return Task.CompletedTask; });
    await File.WriteAllBytesAsync(path, [0x41, 0x00]); await Reject<InvalidDataException>(() => { TextDocument.Open(path); return Task.CompletedTask; });
    await File.WriteAllBytesAsync(path, new byte[TextDocument.MaximumBytes + 1]); await Reject<InvalidDataException>(() => { TextDocument.Open(path); return Task.CompletedTask; });
    await Reject<InvalidDataException>(() => { edited.Edit("nul\0text"); return Task.CompletedTask; });
    var large = new TextDocument(); large.Edit(new string('Ω', TextDocument.MaximumBytes));
    await Reject<InvalidDataException>(() => large.SaveAsync(Path.Combine(root, "large.txt"), TextEncodingKind.Utf8, "LF", false));
    Check(!File.Exists(Path.Combine(root, "large.txt")), "Oversized encoded output created a destination");
    string raced = Path.Combine(root, "raced.txt");
    await Reject<IOException>(() => TextFileWriter.WriteAsync(raced, [65], false, () => File.WriteAllText(raced, "concurrent file"), default));
    Check(await File.ReadAllTextAsync(raced) == "concurrent file", "New-file race overwrote another writer");
    Check(Directory.GetFiles(root, ".Aquarium-text-*.tmp").Length == 0, "Temporary plaintext files leaked");
    string validationFailure = Path.Combine(root, "validation-failure.txt"); await File.WriteAllTextAsync(validationFailure, "retained");
    await Reject<IOException>(() => TextFileWriter.WriteAsync(validationFailure, [66], true, () => throw new IOException("generated late validation failure"), default));
    Check(await File.ReadAllTextAsync(validationFailure) == "retained", "Late validation failure changed prior bytes");
    if (OperatingSystem.IsWindows())
    {
        var security = new FileInfo(validationFailure).GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(validationFailure).SetAccessControl(security);
        string priorAcl = new FileInfo(validationFailure).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var restricted = TextDocument.Open(validationFailure); restricted.Edit("restricted edit"); await restricted.SaveAsync(validationFailure, TextEncodingKind.Utf8, "LF", false);
        Check(new FileInfo(validationFailure).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access) == priorAcl, "Save weakened/changed the fixture DACL");
        string recoveryPath = Path.Combine(root, "recovery");
        using var recovery = new RecoveryStore(recoveryPath);
        await Reject<IOException>(() => { using var second = new RecoveryStore(recoveryPath); return Task.CompletedTask; });
        var draft = new TextDocument(); draft.Edit("private password/configuration fixture Ω");
        await recovery.SaveAsync(draft.Snapshot());
        var encrypted = await File.ReadAllBytesAsync(Path.Combine(recoveryPath, draft.Id.ToString("N") + ".dtp"));
        Check(!Encoding.UTF8.GetString(encrypted).Contains("private password"), "Recovery stored cleartext");
        Check(recovery.Pending().Length == 1 && recovery.Read(draft.Id).Text == draft.Text, "Windows user-scoped recovery roundtrip failed");
        var recovered = TextDocument.Recover(recovery.Read(draft.Id)); Check(recovered.Dirty && recovered.Id == draft.Id, "Recovered draft became clean or changed identity");
        encrypted[encrypted.Length / 2] ^= 1; await File.WriteAllBytesAsync(Path.Combine(recoveryPath, draft.Id.ToString("N") + ".dtp"), encrypted);
        await Reject<CryptographicException>(() => { recovery.Read(draft.Id); return Task.CompletedTask; });
        Check((await File.ReadAllBytesAsync(Path.Combine(recoveryPath, draft.Id.ToString("N") + ".dtp"))).SequenceEqual(encrypted), "Tampered recovery record was changed");
        await Reject<CryptographicException>(() => recovery.SaveAsync(draft.Snapshot()));
        await Reject<CryptographicException>(() => recovery.RemoveAsync(draft.Id));
        await recovery.RemoveAsync(draft.Id, default, true); Check(recovery.Pending().Length == 0, "Explicit recovery discard failed");
        var future = WindowsRecoveryProtection.Protect(JsonSerializer.SerializeToUtf8Bytes(new RecoveryEnvelope(2, draft.Snapshot())));
        var recordPath = Path.Combine(recoveryPath, draft.Id.ToString("N") + ".dtp"); await File.WriteAllBytesAsync(recordPath, future);
        await Reject<InvalidDataException>(() => recovery.SaveAsync(draft.Snapshot()));
        Check((await File.ReadAllBytesAsync(recordPath)).SequenceEqual(future), "Future recovery schema was overwritten");
        await recovery.RemoveAsync(draft.Id, default, true);
    }
    else
    {
        File.SetUnixFileMode(validationFailure, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var restricted = TextDocument.Open(validationFailure); restricted.Edit("restricted edit"); await restricted.SaveAsync(validationFailure, TextEncodingKind.Utf8, "LF", false);
        Check(File.GetUnixFileMode(validationFailure) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Save changed Unix permission bits");
        await Reject<PlatformNotSupportedException>(() => { using var unsupported = new RecoveryStore(Path.Combine(root, "recovery")); return Task.CompletedTask; });
    }
    Console.WriteLine($"Text editor: {checks} encoding/conflict/cancellation/publication/recovery checks passed. Only generated text was accessed.");
}
finally
{
    string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-text-tests-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup.");
    Directory.Delete(full, true);
}
