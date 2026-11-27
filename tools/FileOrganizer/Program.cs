using System.Diagnostics;

namespace FileOrganizer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new OrganizerWindow());
    }
}

internal sealed partial class OrganizerWindow : Form
{
    private readonly ListBox files = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox prefix = new() { Width = 180, PlaceholderText = "Rename prefix" };
    private readonly TextBox review = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both };
    private readonly Button apply = new() { AutoSize = true, Text = "Apply preview", Enabled = false };
    private readonly Button undo = new() { AutoSize = true, Text = "Undo last change", Enabled = false };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Stack<FileMove[]> history = new();
    private FileMove[] preview = [];
    private readonly Button cancelComparison = new() { AutoSize = true, Text = "Cancel comparison/export", Enabled = false };
    private readonly Button exportComparison = new() { AutoSize = true, Text = "Save new comparison JSON…", Enabled = false };
    private CancellationTokenSource? comparisonCancellation, exportCancellation;
    private FileComparisonReport? comparison;
    private bool exporting;

    internal OrganizerWindow(string? appearancePath = null, string? browserDataRoot = null)
    {
        Text = "Aquarium File Organizer";
        Size = new Size(1000, 680);
        MinimumSize = new Size(640, 420);
        var workspace = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 540), SplitterDistance = 130 };
        workspace.Panel1.Controls.Add(files);
        workspace.Panel2.Controls.Add(review);
        Controls.Add(workspace);
        Controls.Add(actions);
        var comparisonActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        comparisonActions.Controls.AddRange([cancelComparison, exportComparison]); Controls.Add(comparisonActions);
        cancelComparison.Click += (_, _) => { comparisonCancellation?.Cancel(); exportCancellation?.Cancel(); };
        exportComparison.Click += async (_, _) => await ExportComparisonAsync();
        AddAction("Choose files…", ChooseFiles);
        actions.Controls.Add(prefix);
        AddAction("Preview rename", PreviewRename);
        AddAction("Preview move…", PreviewMove);
        var compare = new Button { AutoSize = true, Text = "Compare duplicate contents" };
        compare.Click += async (_, _) => { try { await ComparePathsAsync(SelectedPaths()); } catch (Exception ex) { review.Text = ex.Message; } }; actions.Controls.Add(compare);
        AddAction("Review recovery records", () => review.Text = MoveJournal.Review());
        actions.Controls.Add(apply);
        actions.Controls.Add(undo);
        apply.Click += async (_, _) => await ApplyAsync();
        undo.Click += async (_, _) => await UndoAsync();
        files.AccessibleName = "Selected operation file paths"; prefix.AccessibleName = "Rename prefix"; review.AccessibleName = "Reviewed operations and comparison";
        prefix.TextChanged += (_, _) => ClearPreview();
        files.SelectedIndexChanged += (_, _) => ClearPreview();
        review.Text = "Choose files, then preview a rename/move or compare selected file contents. Comparison is read-only: up to 10,000 paths, 256 MiB per file and 4 GiB total reads. Partial/skipped paths remain unknown; no files are deleted. Saved JSON includes full local paths and hashes.";
        InitializeBrowser(workspace, comparisonActions, browserDataRoot, appearancePath);
        Aquarium.Tools.FamilyWindow.Apply(this, appearancePath);
    }

    private void AddAction(string name, Action action)
    {
        var button = new Button { Text = name, AutoSize = true };
        button.Click += (_, _) => Run(action);
        actions.Controls.Add(button);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ChooseFiles()
    {
        using var picker = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        SetOperationSelection(picker.FileNames);
    }
    internal void SetOperationSelection(IEnumerable<string> paths)
    {
        if (moving || exporting || comparisonCancellation is not null) throw new InvalidOperationException("Finish the current operation first.");
        var selected = paths.Distinct(StringComparer.Ordinal).Take(10001).ToArray();
        if (selected.Length > 10000 || selected.Sum(p => (long)p.Length) > 2 * 1024 * 1024) throw new InvalidOperationException("Choose up to 10,000 files and 2 MiB of path text.");
        ClearPreview(); files.BeginUpdate();
        try { files.Items.Clear(); files.Items.AddRange(selected); }
        finally { files.EndUpdate(); }
    }

    private string[] SelectedPaths()
    {
        var selected = files.SelectedItems.Count > 0 ? files.SelectedItems.Cast<string>() : files.Items.Cast<string>();
        var paths = selected.ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("Choose at least one file.");
        return paths;
    }

    private void PreviewRename()
    {
        var value = prefix.Text.Trim();
        if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Enter a valid filename prefix.");
        SetPreview(SelectedPaths().Select(path => new FileMove(path,
            Path.Combine(Path.GetDirectoryName(path)!, value + Path.GetFileName(path)))));
    }

    private void PreviewMove()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose the destination folder" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        SetPreview(SelectedPaths().Select(path => new FileMove(path, Path.Combine(picker.SelectedPath, Path.GetFileName(path)))));
    }

    private void SetPreview(IEnumerable<FileMove> proposed)
    {
        ClearPreview();
        var moves = proposed.ToArray();
        Validate(moves);
        preview = moves;
        apply.Enabled = true;
        review.Lines = moves.Select(move => $"{move.Source}  →  {move.Target}").ToArray();
    }

    private void ClearPreview()
    {
        comparison = null; exportComparison.Enabled = false;
        if (preview.Length > 0) review.Clear();
        preview = [];
        apply.Enabled = false;
    }

    internal async Task ComparePathsAsync(string[] paths, CancellationToken token = default)
    {
        if (moving || exporting || comparisonCancellation is not null) return;
        ClearPreview();
        actions.Enabled = false;
        files.Enabled = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); comparisonCancellation = cancellation; cancelComparison.Enabled = true;
        review.Text = "Comparing selected files read-only. Up to 10,000 paths, 256 MiB per file and 4 GiB total reads. Cancel stops without presenting a completed comparison.";
        try
        {
            var progress = new ComparisonUiProgress(value =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(() =>
                {
                    if (!IsDisposed && comparisonCancellation is not null) review.Text = $"Processed {value.CompletedPaths:N0} of {value.TotalPaths:N0} paths · {value.BytesRead / 1048576.0:F1} MiB read. Files are never changed.";
                });
            });
            var report = await Task.Run(() => FileComparison.CompareAsync(paths, cancellation.Token, progress));
            cancellation.Token.ThrowIfCancellationRequested(); if (IsDisposed) return; comparison = report;
            var lines = new List<string> { $"{report.Groups.Length:N0} matching-content groups · {report.SkippedPaths:N0} skipped paths · {report.BytesRead:N0} bytes read.",
                report.Complete ? "Selected paths were compared by length, with SHA-256 for matching lengths." : "Partial comparison: skipped paths remain unknown. No-match observations do not cover skipped paths.", report.Coverage, "" };
            foreach (var group in report.Groups.Take(200))
            {
                lines.Add($"Matching content · {group.Bytes:N0} bytes each"); lines.AddRange(group.Paths.Take(20));
                if (group.Paths.Length > 20) lines.Add("More paths are available in the full JSON export."); lines.Add("");
            }
            if (report.Groups.Length > 200) lines.Add("Showing the first 200 groups; save the full report or refine the selection.");
            foreach (var skipped in report.Files.Where(f => f.Code is not ("hashed" or "unique-length")).Take(100)) lines.Add(skipped.Code + " · " + skipped.Path);
            if (report.SkippedPaths > 100) lines.Add("More skipped paths are available in the full JSON export.");
            lines.Add("JSON export includes full paths and observed hashes. Save/share only after review; existing files are never overwritten."); review.Lines = lines.ToArray();
        }
        catch (OperationCanceledException) { if (!IsDisposed) review.Text = "Canceled. No completed comparison is presented; source files were not changed."; }
        catch (Exception ex) { if (!IsDisposed) review.Text = ex.Message; }
        finally { comparisonCancellation = null; if (!IsDisposed) { actions.Enabled = files.Enabled = true; cancelComparison.Enabled = false; exportComparison.Enabled = comparison is not null; } }
    }
    private sealed class ComparisonUiProgress(Action<ComparisonProgress> action) : IProgress<ComparisonProgress>
    {
        private readonly Stopwatch clock = Stopwatch.StartNew(); private long last = -100;
        public void Report(ComparisonProgress value) { if (clock.ElapsedMilliseconds - last < 100 && value.CompletedPaths != value.TotalPaths) return; last = clock.ElapsedMilliseconds; action(value); }
    }
    private async Task ExportComparisonAsync()
    {
        if (comparison is not { } report || moving || exporting || comparisonCancellation is not null) return;
        using var picker = new SaveFileDialog { Filter = "JSON report|*.json", DefaultExt = "json", OverwritePrompt = false, FileName = "Aquarium-file-comparison-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        using var cancellation = new CancellationTokenSource(); exportCancellation = cancellation; exporting = true;
        actions.Enabled = files.Enabled = exportComparison.Enabled = false; cancelComparison.Enabled = true;
        try
        {
            await Task.Run(() => FileComparison.ExportNewAsync(picker.FileName, report, [AppContext.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AquariumFileOrganizer")], cancellation.Token));
            if (!IsDisposed) review.AppendText(Environment.NewLine + "Saved the reviewed comparison as a new JSON file. Source files remain unchanged.");
        }
        catch (OperationCanceledException) { if (!IsDisposed) review.AppendText(Environment.NewLine + "Export canceled; existing files were preserved."); }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally { exportCancellation = null; exporting = false; if (!IsDisposed) { actions.Enabled = files.Enabled = true; cancelComparison.Enabled = false; exportComparison.Enabled = comparison is not null; } }
    }

    private async Task ApplyAsync()
    {
        var moves = preview;
        if (moves.Length == 0) return;
        if (!await ExecuteResponsive(moves)) return;
        history.Push(moves.Select(move => new FileMove(move.Target, move.Source)).Reverse().ToArray());
        UpdatePaths(moves);
    }

    private async Task UndoAsync()
    {
        if (!history.TryPeek(out var moves)) return;
        if (!await ExecuteResponsive(moves)) return;
        history.Pop();
        UpdatePaths(moves);
    }

    private void UpdatePaths(FileMove[] moves)
    {
        ClearPreview();
        var replacements = moves.ToDictionary(move => move.Source, move => move.Target, StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < files.Items.Count; index++)
        {
            if (files.Items[index] is string path && replacements.TryGetValue(path, out var replacement))
                files.Items[index] = replacement;
        }
        undo.Enabled = history.Count > 0;
        review.Text = $"Updated {moves.Length} file path(s). Undo history lasts until this window closes. Source/destination recovery records persist under Review recovery records; they do not copy file contents.";
    }

    private static void Validate(FileMove[] moves)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var move in moves)
        {
            RejectLinks(move.Source);
            RejectLinks(Path.GetDirectoryName(move.Target)!);
            if (!File.Exists(move.Source)) throw new IOException("File is missing: " + move.Source);
            if (!targets.Add(move.Target) || File.Exists(move.Target) || Directory.Exists(move.Target))
                throw new IOException("The destination already exists or appears twice: " + move.Target);
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files and folders are unavailable for this operation: " + current);
    }

    private static void Execute(FileMove[] moves)
    {
        Validate(moves);
        var journal = new MoveJournal(moves.Select(move => (move.Source, move.Target)));
        var completed = new Stack<FileMove>();
        try
        {
            foreach (var move in moves)
            {
                File.Move(move.Source, move.Target);
                completed.Push(move);
                journal.Record("In progress", completed.Count, "");
            }
            journal.Record("Completed", completed.Count, "");
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<string>();
            foreach (var move in completed)
            {
                try { File.Move(move.Target, move.Source); }
                catch (Exception ex) { rollbackFailures.Add(move.Target + ": " + ex.Message); }
            }
            if (rollbackFailures.Count > 0)
            {
                journal.Record("Manual recovery needed", completed.Count, string.Join(Environment.NewLine, rollbackFailures));
                throw new IOException(failure.Message + Environment.NewLine + "Restore these files manually:" +
                    Environment.NewLine + string.Join(Environment.NewLine, rollbackFailures), failure);
            }
            journal.Record("Rolled back", 0, failure.Message);
            throw;
        }
    }

    private bool moving;
    private async Task<bool> ExecuteResponsive(FileMove[] moves)
    {
        if (moving) return false;
        comparison = null; exportComparison.Enabled = false;
        moving = true; actions.Enabled = files.Enabled = false;
        review.Text = "Applying the reviewed file operation…";
        try { await Task.Run(() => Execute(moves)); return true; }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        finally { moving = false; if (!IsDisposed) actions.Enabled = files.Enabled = true; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (moving || exporting || browserSaving) { e.Cancel = true; review.Text = moving ? "Wait for the reviewed file operation to finish before closing." : "Wait for report/bookmark publication to finish, or cancel report export before closing."; }
        else { comparisonCancellation?.Cancel(); CancelFolderLoads(); }
        base.OnFormClosing(e);
    }

    private sealed record FileMove(string Source, string Target);
}
