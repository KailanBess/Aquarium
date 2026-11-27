using Aquarium.Core;
using Aquarium.TextFiles;
using Aquarium.Tools;

namespace FileOrganizer;

internal sealed partial class OrganizerWindow
{
    private sealed class FolderPage : TabPage
    {
        internal readonly DataGridView Grid = new() { Dock = DockStyle.Fill, ReadOnly = true, VirtualMode = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        internal FolderSnapshot? Snapshot;
        internal FolderEntry[] VisibleEntries = [];
        internal CancellationTokenSource? Loading;
        internal string DirectoryPath = "";
        internal FolderPage() { Controls.Add(Grid); }
        internal FolderEntry[] SelectedEntries() => Grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).Where(i => i >= 0 && i < VisibleEntries.Length).Select(i => VisibleEntries[i]).ToArray();
    }
    private readonly TabControl primaryPages = new() { Dock = DockStyle.Fill };
    private readonly TabPage operationsPage = new("File operations");
    private readonly TabControl folders = new() { Dock = DockStyle.Fill };
    private readonly Panel browserBody = new() { Dock = DockStyle.Fill };
    private readonly TextBox address = new() { Width = 350, PlaceholderText = "Absolute folder path", MaxLength = 4096 };
    private readonly TextBox folderFilter = new() { Width = 230, PlaceholderText = "Filter observed filenames", MaxLength = 256 };
    private readonly TextBox folderStatus = new() { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 90 };
    private readonly ComboBox bookmarkChoice = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly List<FlowLayoutPanel> browserBars = [];
    private FolderBookmarks? bookmarks;
    private string bookmarkState = "Bookmarks are unavailable.";
    private bool browserSaving;
    private string? browserAppearance;
    private FolderPage? CurrentFolder => folders.SelectedTab as FolderPage;
    private bool OperationBusy => moving || exporting || comparisonCancellation is not null || browserSaving;

    private void InitializeBrowser(SplitContainer workspace, FlowLayoutPanel comparisonActions, string? profile, string? appearance)
    {
        browserAppearance = appearance;
        Controls.Remove(workspace); Controls.Remove(actions); Controls.Remove(comparisonActions);
        operationsPage.Controls.Add(workspace); operationsPage.Controls.Add(actions); operationsPage.Controls.Add(comparisonActions);
        var browse = new TabPage("Folders"); browse.Controls.Add(browserBody);
        primaryPages.TabPages.AddRange([operationsPage, browse]); Controls.Add(primaryPages);
        primaryPages.Selecting += (_, e) => { if (OperationBusy) e.Cancel = true; };
        Size = new Size(1100, 850); MinimumSize = new Size(820, 620);
        browserBody.Controls.Add(folders); browserBody.Controls.Add(folderStatus);
        FlowLayoutPanel Bar()
        { var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true }; browserBars.Add(panel); return panel; }
        void Button(FlowLayoutPanel panel, string text, Func<Task> operation)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += async (_, _) => { try { await operation(); } catch (OperationCanceledException) { } catch (Exception ex) { folderStatus.Text = ex.Message + " Existing files and records were preserved."; } };
            panel.Controls.Add(button);
        }
        var bookmarksBar = Bar(); bookmarksBar.Controls.Add(bookmarkChoice);
        Button(bookmarksBar, "Open saved bookmark", async () => { if (bookmarkChoice.SelectedItem is FolderBookmark item) await OpenFolderAsync(item.Path); });
        Button(bookmarksBar, "Bookmark current folder…", PinFolderAsync);
        Button(bookmarksBar, "Remove bookmark", async () => { if (bookmarkChoice.SelectedItem is FolderBookmark item) await SaveBookmarksAsync(() => bookmarks!.RemoveAsync(item.Id)); });
        Button(bookmarksBar, "Undo bookmark edit", () => SaveBookmarksAsync(() => bookmarks!.UndoAsync()));
        Button(bookmarksBar, "Reload bookmarks", () => { LoadBookmarks(); return Task.CompletedTask; });
        var fileBar = Bar(); fileBar.Controls.Add(folderFilter);
        Button(fileBar, "Preview selected text", PreviewSelectedAsync);
        Button(fileBar, "Replace operation selection with selected files", () => { TransferSelection(); return Task.CompletedTask; });
        var locationBar = Bar();
        Button(locationBar, "Choose folder…", async () => { using var picker = new FolderBrowserDialog { Description = "Choose a folder to observe; no files are changed." }; if (picker.ShowDialog(this) == DialogResult.OK) await OpenFolderAsync(picker.SelectedPath); });
        locationBar.Controls.Add(address);
        Button(locationBar, "Open path in new tab", () => OpenFolderAsync(address.Text));
        Button(locationBar, "Refresh", async () => { if (CurrentFolder is { } page) await LoadFolderAsync(page, page.DirectoryPath); });
        Button(locationBar, "Up", async () => { if (CurrentFolder is { } page && Directory.GetParent(page.DirectoryPath) is { } parent) await LoadFolderAsync(page, parent.FullName); });
        Button(locationBar, "Stop folder load", () => { CurrentFolder?.Loading?.Cancel(); return Task.CompletedTask; });
        Button(locationBar, "Close folder tab", () => { if (CurrentFolder is { } page) { page.Loading?.Cancel(); folders.TabPages.Remove(page); page.Dispose(); } UpdateFolderStatus(); return Task.CompletedTask; });
        browserBody.Controls.Add(bookmarksBar); browserBody.Controls.Add(fileBar); browserBody.Controls.Add(locationBar);
        address.AccessibleName = "Folder path; press Open path to navigate"; folderFilter.AccessibleName = "Filter names within the observed listing";
        folderStatus.AccessibleName = "Observed folder coverage and bookmark status"; bookmarkChoice.AccessibleName = "Saved folder bookmarks; targets are opened only on request";
        folderFilter.TextChanged += (_, _) => { if (CurrentFolder is { } page) ShowListing(page); };
        folders.SelectedIndexChanged += (_, _) => { if (CurrentFolder is { } page) address.Text = page.DirectoryPath; UpdateFolderStatus(); };
        bookmarkChoice.SelectedIndexChanged += (_, _) => { if (bookmarkChoice.SelectedItem is FolderBookmark item) address.Text = item.Path; };
        actions.EnabledChanged += (_, _) => { foreach (var bar in browserBars) bar.Enabled = actions.Enabled; folders.Enabled = actions.Enabled; };
        try
        {
            if (profile is null)
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(local)) throw new IOException("Local application data is unavailable.");
                profile = Path.Combine(local, "AquariumFileOrganizer");
            }
            bookmarks = new FolderBookmarks(profile); LoadBookmarks();
        }
        catch { bookmarkState = "Bookmarks unavailable; original records are preserved. Folder browsing remains available."; }
        UpdateFolderStatus();
    }
    internal async Task OpenFolderAsync(string path)
    {
        if (OperationBusy) throw new InvalidOperationException("Finish the current file operation first.");
        string full = BrowserPaths.Normalize(path);
        if (folders.TabCount >= 8) throw new InvalidOperationException("Close a folder tab before opening more than eight.");
        var page = new FolderPage { DirectoryPath = full, Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(full)) };
        if (page.Text.Length == 0) page.Text = full;
        page.Grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        foreach (var (title, sample) in new[] { ("Name", "Folder entry name"), ("Kind", "Unknown"), ("Bytes", "9,999,999,999,999"), ("Modified UTC", "2026-11-27 23:59:59"), ("Observation", "metadata-unavailable") })
        {
            int width = Math.Max(TextRenderer.MeasureText(title, page.Grid.Font).Width,
                TextRenderer.MeasureText(sample, page.Grid.Font).Width) + 16;
            page.Grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                AutoSizeMode = title == "Name" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                Width = width,
                MinimumWidth = title == "Name" ? width : 24,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
        page.Grid.BackgroundColor = files.BackColor; page.Grid.EnableHeadersVisualStyles = false;
        page.Grid.DefaultCellStyle.BackColor = page.Grid.ColumnHeadersDefaultCellStyle.BackColor = files.BackColor;
        page.Grid.DefaultCellStyle.ForeColor = page.Grid.ColumnHeadersDefaultCellStyle.ForeColor = files.ForeColor;
        page.Grid.RowHeightInfoNeeded += (_, e) => { e.Height = Math.Max(24, page.Grid.Font.Height + 8); e.MinimumHeight = e.Height; };
        page.Grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= page.VisibleEntries.Length) return;
            var entry = page.VisibleEntries[e.RowIndex];
            e.Value = e.ColumnIndex switch { 0 => entry.Name, 1 => entry.Kind, 2 => entry.Bytes?.ToString("N0") ?? "Unknown", 3 => entry.ModifiedUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown", _ => entry.Code };
        };
        page.Grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= page.VisibleEntries.Length || !page.VisibleEntries[e.RowIndex].IsFolder) return;
            try { await LoadFolderAsync(page, page.VisibleEntries[e.RowIndex].Path); } catch (Exception ex) { folderStatus.Text = ex.Message; }
        };
        folders.TabPages.Add(page); folders.SelectedTab = page; primaryPages.SelectedIndex = 1;
        await LoadFolderAsync(page, full);
    }
    private async Task LoadFolderAsync(FolderPage page, string path)
    {
        if (OperationBusy) throw new InvalidOperationException("Finish the current file operation first.");
        string full = BrowserPaths.Normalize(path); page.Loading?.Cancel();
        using var cancellation = new CancellationTokenSource(); page.Loading = cancellation; page.DirectoryPath = full; page.Grid.Enabled = false;
        page.Snapshot = null; page.VisibleEntries = []; page.Grid.RowCount = 0; address.Text = full;
        folderStatus.Text = "Reading immediate folder names and metadata; no file contents are read. Stop folder load cancels between observations.";
        try
        {
            var snapshot = await Task.Run(() => FolderModel.Read(full, cancellation.Token)); cancellation.Token.ThrowIfCancellationRequested();
            if (page.IsDisposed || IsDisposed || page.Loading != cancellation) return;
            page.Snapshot = snapshot; page.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(full)); if (page.Text.Length == 0) page.Text = full;
            ShowListing(page);
        }
        catch (OperationCanceledException) { if (!page.IsDisposed && !IsDisposed && page.Loading == cancellation) folderStatus.Text = "Folder load canceled. No completed listing is presented."; }
        finally { if (!page.IsDisposed && page.Loading == cancellation) { page.Loading = null; page.Grid.Enabled = true; } }
    }
    private void ShowListing(FolderPage page)
    {
        if (page.Snapshot is null) return;
        page.VisibleEntries = FolderModel.Filter(page.Snapshot, folderFilter.Text); page.Grid.RowCount = page.VisibleEntries.Length;
        page.Grid.ClearSelection(); page.Grid.Invalidate(); if (CurrentFolder == page) UpdateFolderStatus();
    }
    private void UpdateFolderStatus()
    {
        if (CurrentFolder?.Snapshot is not { } snapshot) { folderStatus.Text = "No completed folder listing. Choose a folder explicitly; saved bookmarks do not automatically open target locations. " + bookmarkState; return; }
        folderStatus.Text = $"Observed folder: {snapshot.Path}\r\n{CurrentFolder.VisibleEntries.Length:N0} filename matches among {snapshot.Entries.Count:N0} observed entries · {snapshot.Code} · {snapshot.ObservedAtUtc:HH:mm:ss} UTC. " +
            (snapshot.Complete ? "Listing completed at observation time. " : "Coverage is partial/unavailable; unobserved entries remain unknown. ") + snapshot.Coverage + "\r\n" + bookmarkState;
    }
    private FolderEntry[] SelectedBrowserFiles()
    {
        if (CurrentFolder is not { Snapshot: not null, Loading: null } page) throw new InvalidOperationException("Finish a folder listing first.");
        var selected = page.SelectedEntries();
        if (selected.Length == 0 || selected.Any(e => !e.IsFile)) throw new InvalidOperationException("Select regular file rows only; folders, links and unavailable entries remain unchanged.");
        return selected;
    }
    private void TransferSelection()
    {
        var selected = SelectedBrowserFiles(); SetOperationSelection(selected.Select(e => e.Path));
        primaryPages.SelectedTab = operationsPage; review.Text = "Operation selection replaced with the selected file rows. Preview the exact rename/move plan before applying.";
    }
    private async Task PreviewSelectedAsync()
    {
        var selected = SelectedBrowserFiles(); if (selected.Length != 1) throw new InvalidOperationException("Select one text file to preview.");
        folderStatus.Text = "Reading a bounded text preview; no files execute or change.";
        var document = await Task.Run(() => TextDocument.Open(selected[0].Path)); if (IsDisposed) return;
        using var dialog = new Form { Text = "Text preview · " + document.DisplayName, Size = new Size(850, 650), MinimumSize = new Size(600, 400), StartPosition = FormStartPosition.CenterParent };
        var text = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, Text = document.Text, WordWrap = false, Font = new Font("Consolas", 12) };
        var note = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 80,
            Text = $"{document.Text.Length:N0} displayed characters · {document.Encoding} · {document.Newline}. Source may change after reading. This is a read-only text preview; no files execute, change or upload." };
        dialog.Controls.Add(text); dialog.Controls.Add(note); FamilyWindow.Apply(dialog, browserAppearance); dialog.ShowDialog(this); UpdateFolderStatus();
    }
    private void LoadBookmarks()
    {
        try
        {
            bookmarks?.Load(); bookmarkChoice.Items.Clear(); if (bookmarks is not null) bookmarkChoice.Items.AddRange(bookmarks.Items.Cast<object>().ToArray());
            bookmarkState = $"{bookmarks?.Items.Count ?? 0} saved folder bookmarks. Bookmark records contain paths; targets are not opened automatically.";
        }
        catch { bookmarkChoice.Items.Clear(); bookmarkState = "Bookmarks unavailable or unsupported; original records are preserved. Reload supported records before editing."; }
        UpdateFolderStatus();
    }
    private async Task PinFolderAsync()
    {
        if (CurrentFolder is not { Snapshot: not null } page) throw new InvalidOperationException("Open a folder before bookmarking it.");
        using var dialog = new Form { Text = "Bookmark folder", Size = new Size(560, 210), StartPosition = FormStartPosition.CenterParent };
        var label = new TextBox { Dock = DockStyle.Top, MaxLength = 80, Text = page.Text.Length <= 80 ? page.Text : "Folder bookmark" };
        var target = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = page.DirectoryPath };
        var save = new Button { Dock = DockStyle.Bottom, Text = "Save local bookmark", AutoSize = true, DialogResult = DialogResult.OK };
        dialog.Controls.Add(target); dialog.Controls.Add(label); dialog.Controls.Add(save); dialog.AcceptButton = save; FamilyWindow.Apply(dialog, browserAppearance);
        if (dialog.ShowDialog(this) == DialogResult.OK) await SaveBookmarksAsync(() => bookmarks!.AddAsync(label.Text, page.DirectoryPath));
    }
    private async Task SaveBookmarksAsync(Func<Task> operation)
    {
        if (bookmarks is null || !bookmarks.Writable) throw new InvalidOperationException("Reload supported bookmark records before editing. Existing files are preserved.");
        if (OperationBusy) return; browserSaving = true; actions.Enabled = false;
        try { await operation(); LoadBookmarks(); }
        finally { browserSaving = false; if (!IsDisposed) actions.Enabled = true; }
    }
    private void CancelFolderLoads() { foreach (FolderPage page in folders.TabPages) page.Loading?.Cancel(); }
}
