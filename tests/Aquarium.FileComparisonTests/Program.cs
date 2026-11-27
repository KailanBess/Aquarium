using FileOrganizer;

var root = Path.Combine(Path.GetTempPath(), "Aquarium-comparison-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Reject<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
try
{
    string first = Path.Combine(root, "first.bin"), second = Path.Combine(root, "second.bin"), different = Path.Combine(root, "different.bin"), unique = Path.Combine(root, "unique.bin");
    await File.WriteAllBytesAsync(first, [1, 2, 3]); await File.WriteAllBytesAsync(second, [1, 2, 3]);
    await File.WriteAllBytesAsync(different, [4, 5, 6]); await File.WriteAllBytesAsync(unique, [7]);
    byte[] original = await File.ReadAllBytesAsync(first);
    var exact = await FileComparison.CompareAsync([first, second, different, unique, first]);
    Check(exact.InputPaths == 5 && exact.Files.Length == 4, "Repeated path became a false duplicate");
    Check(exact.Complete && exact.Groups.Length == 1 && exact.Groups[0].Paths.SequenceEqual(new[] { first, second }), "Exact content grouping failed");
    Check(exact.Groups[0].Bytes == 3 && exact.Groups[0].Sha256.Length == 64 && exact.Files.Single(f => f.Path == unique).Code == "unique-length", "Length/hash attribution incorrect");
    Check(exact.BytesRead == 9 && exact.RecordedAtUtc.Offset == TimeSpan.Zero, "Read accounting or UTC attribution incorrect");
    Check((await File.ReadAllBytesAsync(first)).SequenceEqual(original), "Comparison modified source bytes");
    var relative = await FileComparison.CompareAsync(["relative-file.bin"]);
    Check(!relative.Complete && relative.BytesRead == 0, "Relative input accessed an implicit working directory");
    if (OperatingSystem.IsWindows())
    {
        string caseVariant = Path.Combine(root, Path.GetFileName(first).ToUpperInvariant());
        var caseReport = await FileComparison.CompareAsync([first, caseVariant]);
        Check(!caseReport.Complete && caseReport.Groups.Length == 0, "Case-variant paths silently inferred file identity/full coverage");
    }
    bool changedOnce = false;
    var changed = await FileComparison.CompareAsync([first, second], progress: new InlineProgress(p => { if (!changedOnce && p.BytesRead > 0) { changedOnce = true; File.WriteAllBytes(second, [9, 9, 9, 9]); } }));
    Check(!changed.Complete && changed.Files.Single(f => f.Path == second).Code == "source-changed", "A source change between stat/hash was hidden");
    await File.WriteAllBytesAsync(second, original);
    var missing = await FileComparison.CompareAsync([first, second, Path.Combine(root, "absent.bin")]);
    Check(!missing.Complete && missing.SkippedPaths == 1 && missing.Groups.Length == 1, "Missing source silently inferred full coverage");
    var capped = await FileComparison.CompareAsync([first, second], limits: new(MaximumFileBytes: 2));
    Check(!capped.Complete && capped.BytesRead == 0 && capped.Files.All(f => f.Code == "file-size-limit"), "Per-file cap read disallowed content");
    var budget = await FileComparison.CompareAsync([first, second, different], limits: new(MaximumReadBytes: 3));
    Check(!budget.Complete && budget.BytesRead == 3 && budget.SkippedPaths == 2 && budget.Groups.Length == 0, "Total read budget was exceeded or hidden");
    var zeroBudget = await FileComparison.CompareAsync([first, second], limits: new(MaximumReadBytes: 0));
    Check(!zeroBudget.Complete && zeroBudget.BytesRead == 0, "Zero budget still read content");
    string empty1 = Path.Combine(root, "empty1"), empty2 = Path.Combine(root, "empty2"); File.WriteAllBytes(empty1, []); File.WriteAllBytes(empty2, []);
    var empty = await FileComparison.CompareAsync([empty1, empty2]); Check(empty.Complete && empty.Groups.Length == 1 && empty.BytesRead == 0, "Empty-file duplicates were lost");
    using (var held = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var locked = await FileComparison.CompareAsync([first, second]);
        Check(!locked.Complete && locked.Files.Any(f => f.Code == "unavailable-or-linked"), "Locked file became clean/full coverage");
    }
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel(); await Reject<OperationCanceledException>(() => FileComparison.CompareAsync([first, second], canceled.Token));
    }
    string large1 = Path.Combine(root, "large1"), large2 = Path.Combine(root, "large2"); File.WriteAllBytes(large1, new byte[200000]); File.WriteAllBytes(large2, new byte[200000]);
    using (var active = new CancellationTokenSource())
    {
        var progress = new InlineProgress(p => { if (p.BytesRead > 0) active.Cancel(); });
        await Reject<OperationCanceledException>(() => FileComparison.CompareAsync([large1, large2], active.Token, progress));
        Check(new FileInfo(large1).Length == 200000 && new FileInfo(large2).Length == 200000, "Cancellation changed source files");
    }
    await Reject<InvalidDataException>(() => FileComparison.CompareAsync(Enumerable.Repeat(first, 10001)));
    await Reject<InvalidDataException>(() => FileComparison.CompareAsync(Enumerable.Repeat(new string('x', 4096), 1000)));
    await Reject<ArgumentException>(() => FileComparison.CompareAsync([first], limits: new(MaximumPaths: 10001)));
    await Reject<ArgumentException>(() => FileComparison.CompareAsync([]));
    var invalid = await FileComparison.CompareAsync(["bad\0path", first]); Check(!invalid.Complete && invalid.SkippedPaths == 1, "Invalid path was lost from coverage");
    string report = Path.Combine(root, "comparison.json"); await FileComparison.ExportNewAsync(report, exact, []);
    Check((await File.ReadAllTextAsync(report)).Contains(exact.Groups[0].Sha256), "Explicit JSON export omitted observed hashes");
    var saved = await File.ReadAllBytesAsync(report);
    await Reject<IOException>(() => FileComparison.ExportNewAsync(report, exact, []));
    Check((await File.ReadAllBytesAsync(report)).SequenceEqual(saved), "Report export replaced prior data");
    await Reject<InvalidDataException>(() => FileComparison.ExportNewAsync(Path.Combine(root, "inside.json"), exact, [root]));
    await Reject<InvalidDataException>(() => FileComparison.ExportNewAsync(Path.Combine(root, "wrong.txt"), exact, []));
    using (var cancelExport = new CancellationTokenSource())
    { cancelExport.Cancel(); await Reject<OperationCanceledException>(() => FileComparison.ExportNewAsync(Path.Combine(root, "canceled.json"), exact, [], cancelExport.Token)); }
    Check(!File.Exists(Path.Combine(root, "canceled.json")) && Directory.GetFiles(root, ".Aquarium-comparison-*.tmp").Length == 0, "Canceled export created/leaked output");
    string jsonSource = Path.Combine(root, "source.json"); await File.WriteAllTextAsync(jsonSource, "{\"generated\":true}");
    var jsonReport = await FileComparison.CompareAsync([jsonSource]);
    await Reject<InvalidDataException>(() => FileComparison.ExportNewAsync(jsonSource, jsonReport, []));
    Check(await File.ReadAllTextAsync(jsonSource) == "{\"generated\":true}", "Report export changed selected JSON source");
    if (!OperatingSystem.IsWindows())
    {
        string link = Path.Combine(root, "linked"); File.CreateSymbolicLink(link, first);
        var linked = await FileComparison.CompareAsync([link, first]); Check(!linked.Complete && linked.SkippedPaths == 1 && linked.Groups.Length == 0, "Linked input was followed");
        string targetFolder = Path.Combine(root, "target"), linkedFolder = Path.Combine(root, "linked-folder"); Directory.CreateDirectory(targetFolder); Directory.CreateSymbolicLink(linkedFolder, targetFolder);
        await Reject<IOException>(() => FileComparison.ExportNewAsync(Path.Combine(linkedFolder, "linked.json"), exact, []));
        Check(!File.Exists(Path.Combine(targetFolder, "linked.json")), "Export followed a linked destination folder");
    }
    Console.WriteLine($"File comparison: {checks} hash/coverage/budget/locking/cancellation/preservation checks passed. Only generated files were read.");
}
finally
{
    string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-comparison-tests-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup.");
    Directory.Delete(full, true);
}
internal sealed class InlineProgress(Action<ComparisonProgress> action) : IProgress<ComparisonProgress> { public void Report(ComparisonProgress value) => action(value); }
