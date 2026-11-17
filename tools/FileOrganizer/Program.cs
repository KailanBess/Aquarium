using System.Security.Cryptography;

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

internal sealed class OrganizerWindow : Form
{
    private readonly ListBox files = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox prefix = new() { Width = 180, PlaceholderText = "Rename prefix" };
    private readonly TextBox review = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both };
    private readonly Button apply = new() { AutoSize = true, Text = "Apply preview", Enabled = false };
    private readonly Button undo = new() { AutoSize = true, Text = "Undo last change", Enabled = false };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Stack<FileMove[]> history = new();
    private FileMove[] preview = [];

    internal OrganizerWindow()
    {
        Text = "Aquarium File Organizer";
        Size = new Size(1000, 680);
        MinimumSize = new Size(640, 420);
        var workspace = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 540), SplitterDistance = 130 };
        workspace.Panel1.Controls.Add(files);
        workspace.Panel2.Controls.Add(review);
        Controls.Add(workspace);
        Controls.Add(actions);
        AddAction("Choose files…", ChooseFiles);
        actions.Controls.Add(prefix);
        AddAction("Preview rename", PreviewRename);
        AddAction("Preview move…", PreviewMove);
        AddAction("Find duplicates", FindDuplicates);
        actions.Controls.Add(apply);
        actions.Controls.Add(undo);
        apply.Click += (_, _) => Run(Apply);
        undo.Click += (_, _) => Run(Undo);
        prefix.TextChanged += (_, _) => ClearPreview();
        files.SelectedIndexChanged += (_, _) => ClearPreview();
        review.Text = "Choose files, then preview a rename or move. Select individual files to limit the operation.";
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
        files.Items.Clear();
        files.Items.AddRange(picker.FileNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        ClearPreview();
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
        if (preview.Length > 0) review.Clear();
        preview = [];
        apply.Enabled = false;
    }

    private async void FindDuplicates()
    {
        ClearPreview();
        actions.Enabled = false;
        files.Enabled = false;
        review.Text = "Comparing selected files…";
        try
        {
            var paths = SelectedPaths();
            var lines = await Task.Run(() => paths.Select(path => new FileInfo(path))
                .GroupBy(file => file.Length).Where(group => group.Count() > 1)
                .SelectMany(group => group.GroupBy(file =>
                {
                    RejectLinks(file.FullName);
                    using var stream = File.OpenRead(file.FullName);
                    return Convert.ToHexString(SHA256.HashData(stream));
                })).Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(file => file.FullName).Prepend("Matching content")).ToArray());
            if (!IsDisposed) review.Lines = lines.Length == 0 ? ["No duplicates found."] : lines;
        }
        catch (Exception ex) { if (!IsDisposed) review.Text = ex.Message; }
        finally { if (!IsDisposed) actions.Enabled = files.Enabled = true; }
    }

    private void Apply()
    {
        var moves = preview;
        if (moves.Length == 0) return;
        Execute(moves);
        history.Push(moves.Select(move => new FileMove(move.Target, move.Source)).Reverse().ToArray());
        UpdatePaths(moves);
    }

    private void Undo()
    {
        if (!history.TryPeek(out var moves)) return;
        Execute(moves);
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
        review.Text = $"Updated {moves.Length} file path(s). Undo history lasts until this window closes.";
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
        var completed = new Stack<FileMove>();
        try
        {
            foreach (var move in moves)
            {
                File.Move(move.Source, move.Target);
                completed.Push(move);
            }
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
                throw new IOException(failure.Message + Environment.NewLine + "Restore these files manually:" +
                    Environment.NewLine + string.Join(Environment.NewLine, rollbackFailures), failure);
            throw;
        }
    }

    private sealed record FileMove(string Source, string Target);
}
