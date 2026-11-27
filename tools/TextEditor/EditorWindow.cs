using Aquarium.Tools;

namespace Aquarium.TextEditor;

internal sealed class EditorWindow : Form
{
    private sealed class DocumentPage : TabPage
    {
        internal readonly TextDocument Document;
        internal readonly RichTextBox Editor = new() { Dock = DockStyle.Fill, AcceptsTab = true, DetectUrls = false, WordWrap = false,
            MaxLength = TextDocument.MaximumBytes, Font = new Font("Consolas", 12), HideSelection = false };
        internal readonly RichTextBox Preview = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = false, Font = new Font("Consolas", 12) };
        internal readonly SplitContainer Split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel2Collapsed = true };
        internal DocumentPage(TextDocument document) { Document = document; Editor.Text = document.Text; Split.Panel1.Controls.Add(Editor); Split.Panel2.Controls.Add(Preview); Controls.Add(Split); }
    }
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    private readonly ComboBox encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox newline = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox find = new() { Width = 150, PlaceholderText = "Find literal text" };
    private readonly TextBox status = new() { Dock = DockStyle.Bottom, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 78 };
    private readonly System.Windows.Forms.Timer recoveryTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer highlightTimer = new() { Interval = 450 };
    private readonly Dictionary<Guid, DocumentSnapshot> recoveredSnapshots = new();
    private readonly CancellationTokenSource lifetime = new();
    private RecoveryStore? recovery;
    private Task recoveryWork = Task.CompletedTask;
    private bool busy, closing, allowClose, formatting;
    private string recoveryState = "Recovery not initialized.";
    private readonly string? appearancePath;
    private DocumentPage? Current => tabs.SelectedTab as DocumentPage;
    internal EditorWindow(string? recoveryDirectory = null, string? appearancePath = null)
    {
        this.appearancePath = appearancePath;
        Text = "Aquarium Text Editor"; AccessibleName = Text; Size = new(1100, 780); MinimumSize = new(760, 580); KeyPreview = true;
        encoding.Items.AddRange(Enum.GetNames<TextEncodingKind>()); newline.Items.AddRange(["CRLF", "LF", "CR", "Mixed"]);
        void Button(string caption, Func<Task> action)
        {
            var button = new Button { Text = caption, AutoSize = true };
            button.Click += async (_, _) => await RunAsync(action); actions.Controls.Add(button);
        }
        Button("New", () => { AddDocument(new TextDocument()); return Task.CompletedTask; });
        Button("Open…", OpenAsync); Button("Save", () => SaveAsync(false)); Button("Save as…", () => SaveAsync(true));
        Button("Close tab", CloseCurrentAsync); Button("Recovery…", ReviewRecoveryAsync);
        actions.Controls.Add(encoding); actions.Controls.Add(newline); actions.Controls.Add(find);
        Button("Find next", () => { FindNext(); return Task.CompletedTask; });
        var wrap = new CheckBox { Text = "Word wrap", AutoSize = true };
        wrap.CheckedChanged += (_, _) => { foreach (DocumentPage page in tabs.TabPages) page.Editor.WordWrap = wrap.Checked; }; actions.Controls.Add(wrap);
        var preview = new CheckBox { Text = "Syntax preview (JSON/XML/INI)", AutoSize = true };
        preview.CheckedChanged += (_, _) => { foreach (DocumentPage page in tabs.TabPages) { page.Split.Panel2Collapsed = !preview.Checked; if (preview.Checked && page.Split.Height > 120) page.Split.SplitterDistance = page.Split.Height / 2; } Highlight(); }; actions.Controls.Add(preview);
        encoding.AccessibleName = "Save encoding"; newline.AccessibleName = "Save newline format"; find.AccessibleName = "Find literal text";
        status.AccessibleName = "Editor and recovery status";
        Controls.Add(tabs); Controls.Add(actions); Controls.Add(status); FamilyWindow.Apply(this, appearancePath);
        tabs.SelectedIndexChanged += (_, _) => SelectDocument();
        recoveryTimer.Tick += (_, _) => StartRecovery(); highlightTimer.Tick += (_, _) => { highlightTimer.Stop(); Highlight(); };
        Shown += (_, _) =>
        {
            try
            {
                recovery = new RecoveryStore(recoveryDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium.TextEditor", "Recovery"));
                recoveryState = $"{recovery.Pending().Length} encrypted recovery records available. Recovery is user/computer scoped; it is not a portable backup.";
            }
            catch { recoveryState = "Recovery unavailable; existing records are preserved. Another editor may own its lease. Save files explicitly."; recovery?.Dispose(); recovery = null; }
            AddDocument(new TextDocument()); recoveryTimer.Start(); Report();
        };
        FormClosing += OnClosing;
        FormClosed += (_, _) => { lifetime.Cancel(); recovery?.Dispose(); recoveryTimer.Dispose(); highlightTimer.Dispose(); };
        KeyDown += async (_, e) =>
        {
            if (!e.Control || busy) return;
            Func<Task>? action = e.KeyCode switch
            {
                Keys.N => () => { AddDocument(new TextDocument()); return Task.CompletedTask; }, Keys.O => OpenAsync,
                Keys.S => () => SaveAsync(e.Shift), Keys.W => CloseCurrentAsync, _ => null
            };
            if (e.KeyCode == Keys.F) { e.SuppressKeyPress = true; find.Focus(); return; }
            if (action is null) return; e.SuppressKeyPress = true; await RunAsync(action);
        };
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy || closing) return; busy = true; actions.Enabled = tabs.Enabled = false;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally { busy = false; if (!IsDisposed) { actions.Enabled = tabs.Enabled = true; highlightTimer.Start(); Report(); } }
    }
    private void AddDocument(TextDocument document)
    {
        if (tabs.TabCount >= 20) throw new InvalidOperationException("Close a tab before opening more than 20 documents.");
        var existing = tabs.TabPages.Cast<DocumentPage>().FirstOrDefault(p => p.Document.Id == document.Id || document.Path is not null && p.Document.Path?.Equals(document.Path, StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null) { tabs.SelectedTab = existing; return; }
        var page = new DocumentPage(document); page.Editor.AccessibleName = "Plain text document";
        page.Editor.BackColor = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(16, 36, 47);
        page.Editor.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(241, 247, 255);
        page.Preview.BackColor = page.Editor.BackColor; page.Preview.ForeColor = page.Editor.ForeColor; page.Preview.AccessibleName = "Read-only syntax preview";
        page.Split.Panel2Collapsed = !actions.Controls.OfType<CheckBox>().Last().Checked;
        page.Editor.WordWrap = actions.Controls.OfType<CheckBox>().First().Checked;
        page.Editor.TextChanged += (_, _) =>
        {
            if (formatting) return;
            try { document.Edit(page.Editor.Text); }
            catch (InvalidDataException ex) { MessageBox.Show(this, ex.Message, Text); formatting = true; page.Editor.Text = document.Text; formatting = false; }
            page.Text = document.DisplayName + (document.Dirty ? " *" : ""); highlightTimer.Stop(); highlightTimer.Start(); Report();
        };
        tabs.TabPages.Add(page); tabs.SelectedTab = page; page.Text = document.DisplayName + (document.Dirty ? " *" : ""); SelectDocument();
        BeginInvoke(() => { if (!page.IsDisposed && !page.Split.Panel2Collapsed && page.Split.Height > 120) page.Split.SplitterDistance = page.Split.Height / 2; });
    }
    private void SelectDocument()
    {
        if (Current is not { } page) return;
        encoding.SelectedItem = page.Document.Encoding.ToString(); newline.SelectedItem = page.Document.Newline;
        highlightTimer.Stop(); highlightTimer.Start(); Report();
    }
    private void Report()
    {
        status.Text = (Current is { } page ? $"{page.Document.DisplayName} · {page.Document.Text.Length:N0} characters · {(page.Document.Dirty ? "Unsaved changes" : "No unsaved changes")}\r\n" : "No open document.\r\n") +
            recoveryState + "\r\nOpen up to 1 MiB of UTF-8 or BOM-marked UTF-16. Mixed newlines are preserved until edited; choose a newline format before saving edits. No files are executed or uploaded.";
    }
    private async Task OpenAsync()
    {
        using var picker = new OpenFileDialog { Filter = "Text/configuration|*.txt;*.json;*.xml;*.ini;*.cfg;*.md;*.cs;*.py;*.ps1;*.yaml;*.yml|All files|*.*", Multiselect = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.FileNames.Length + tabs.TabCount > 20) throw new InvalidOperationException("Open at most 20 tabs.");
        foreach (var path in picker.FileNames) await OpenDocumentAsync(path);
    }
    internal async Task OpenDocumentAsync(string path) => AddDocument(await Task.Run(() => TextDocument.Open(path)));
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (Current is not { } page) return true;
        string? path = saveAs ? null : page.Document.Path;
        bool reviewedReplacement = false;
        if (path is null)
        {
            using var picker = new SaveFileDialog { Filter = "Text document|*.txt|All files|*.*", FileName = page.Document.DisplayName == "Untitled" ? "Untitled.txt" : page.Document.DisplayName, OverwritePrompt = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return false;
            path = picker.FileName; reviewedReplacement = true;
        }
        string line = newline.SelectedItem?.ToString() ?? "CRLF";
        if (line == "Mixed")
        {
            if (page.Document.Dirty) throw new InvalidOperationException("Choose LF, CRLF or CR explicitly before saving an edited mixed-newline document.");
        }
        var kind = Enum.Parse<TextEncodingKind>(encoding.SelectedItem?.ToString() ?? "Utf8");
        await Task.Run(() => page.Document.SaveAsync(path, kind, line, reviewedReplacement, lifetime.Token));
        page.Text = page.Document.DisplayName + (page.Document.Dirty ? " *" : "");
        await recoveryWork;
        if (recovery is not null)
        {
            try { await recovery.RemoveAsync(page.Document.Id, lifetime.Token); recoveredSnapshots.Remove(page.Document.Id); }
            catch { recoveryState = "File saved, but its older recovery record could not be cleared. It remains preserved for explicit review/discard."; }
        }
        SelectDocument(); return true;
    }
    private async Task<bool> ConfirmCloseAsync(DocumentPage page)
    {
        tabs.SelectedTab = page;
        bool discard = false;
        if (page.Document.Dirty)
        {
            var answer = MessageBox.Show(this, $"Save changes to {page.Document.DisplayName}? No discards its encrypted recovery copy as well.", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel || answer == DialogResult.Yes && !await SaveAsync(false)) return false;
            discard = answer == DialogResult.No;
        }
        await recoveryWork;
        if (recovery is not null)
        {
            try { await recovery.RemoveAsync(page.Document.Id, lifetime.Token, discard); recoveredSnapshots.Remove(page.Document.Id); }
            catch { if (discard) throw; recoveryState = "An older recovery record remains preserved for explicit review/discard."; }
        }
        return true;
    }
    private async Task CloseCurrentAsync()
    { if (Current is { } page && await ConfirmCloseAsync(page)) { tabs.TabPages.Remove(page); page.Dispose(); } }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return; e.Cancel = true; if (closing || busy) return;
        closing = true; recoveryTimer.Stop(); highlightTimer.Stop();
        try
        {
            await recoveryWork;
            foreach (var page in tabs.TabPages.Cast<DocumentPage>().ToArray())
                if (!await ConfirmCloseAsync(page)) { closing = false; recoveryTimer.Start(); return; }
            await ShutDownAsync();
        }
        catch (Exception ex) { closing = false; recoveryTimer.Start(); MessageBox.Show(this, "Close stopped: " + ex.Message, Text); }
    }
    internal async Task ShutDownAsync()
    {
        closing = true; recoveryTimer.Stop(); highlightTimer.Stop(); lifetime.Cancel();
        await recoveryWork; allowClose = true; Close();
    }
    private void StartRecovery()
    {
        if (recovery is null || busy || closing || !recoveryWork.IsCompleted) return;
        var snapshots = tabs.TabPages.Cast<DocumentPage>().Where(p => p.Document.Dirty).Select(p => p.Document.Snapshot())
            .Where(s => !recoveredSnapshots.TryGetValue(s.Id, out var previous) || previous != s).ToArray();
        recoveryWork = SaveRecoveryAsync(snapshots);
    }
    private async Task SaveRecoveryAsync(DocumentSnapshot[] snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            try
            {
                await Task.Run(() => recovery!.SaveAsync(snapshot, lifetime.Token)); recoveredSnapshots[snapshot.Id] = snapshot;
                recoveryState = $"Last encrypted recovery write: {DateTime.UtcNow:HH:mm:ss} UTC. Recent edits may still be pending. Recovery is not a portable backup.";
            }
            catch { recoveryState = "Recovery write failed; recent edits may be missing. Save files explicitly. Existing recovery records were preserved."; }
        }
        if (!IsDisposed) Report();
    }
    private async Task ReviewRecoveryAsync()
    {
        if (recovery is null) throw new IOException("Recovery is unavailable; existing records remain preserved.");
        await recoveryWork;
        var ids = recovery.Pending(); if (ids.Length == 0) { MessageBox.Show(this, "No retained encrypted recovery records.", Text); return; }
        using var dialog = new Form { Text = "Review encrypted recovery", Size = new Size(650, 440), StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill }; var rows = new List<(Guid Id, DocumentSnapshot? Snapshot)>();
        foreach (var idText in ids)
        {
            var id = Guid.ParseExact(idText, "N"); DocumentSnapshot? snapshot = null;
            try { snapshot = await Task.Run(() => recovery.Read(id)); } catch { }
            rows.Add((id, snapshot)); list.Items.Add(snapshot is null ? idText + " · unreadable/unsupported (preserved)" : $"{Path.GetFileName(snapshot.Path ?? "Untitled")} · {snapshot.Text.Length:N0} characters");
        }
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var restore = new Button { Text = "Open selected draft", AutoSize = true }; var discard = new Button { Text = "Discard selected record", AutoSize = true };
        restore.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || rows[list.SelectedIndex].Snapshot is not { } snapshot) return;
            try
            {
                if (tabs.TabPages.Cast<DocumentPage>().Any(p => p.Document.Id == snapshot.Id || snapshot.Path is not null && p.Document.Path?.Equals(snapshot.Path, StringComparison.OrdinalIgnoreCase) == true))
                    throw new InvalidOperationException("Close the existing tab before opening this recovered draft. Its recovery record remains preserved.");
                AddDocument(TextDocument.Recover(snapshot)); dialog.Close();
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, Text); }
        };
        discard.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || MessageBox.Show(dialog, "Permanently discard this encrypted recovery record?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            discard.Enabled = restore.Enabled = false;
            try { await recovery.RemoveAsync(rows[list.SelectedIndex].Id, lifetime.Token, true); dialog.Close(); }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, Text); discard.Enabled = restore.Enabled = true; }
        };
        controls.Controls.AddRange([restore, discard]); dialog.Controls.Add(list); dialog.Controls.Add(controls); FamilyWindow.Apply(dialog, appearancePath); dialog.ShowDialog(this);
    }
    private void FindNext()
    {
        if (Current is not { } page || find.Text.Length == 0) return;
        int start = Math.Min(page.Editor.TextLength, page.Editor.SelectionStart + page.Editor.SelectionLength);
        int index = page.Editor.Text.IndexOf(find.Text, start, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) index = page.Editor.Text.IndexOf(find.Text, StringComparison.CurrentCultureIgnoreCase);
        if (index >= 0) { page.Editor.Select(index, find.Text.Length); page.Editor.ScrollToCaret(); page.Editor.Focus(); }
    }
    private void Highlight()
    {
        if (busy || closing || Current is not { } page || page.Split.Panel2Collapsed) return;
        page.Preview.Text = page.Editor.TextLength > 65536 ? "Syntax preview is limited to 65,536 characters. The complete document remains editable above." : page.Editor.Text;
        if (page.Editor.TextLength > 65536 || SystemInformation.HighContrast) return;
        SyntaxHighlight.Apply(page.Preview, Path.GetExtension(page.Document.Path ?? ""));
    }
}
