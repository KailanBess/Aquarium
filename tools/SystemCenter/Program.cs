using Aquarium.Core;

namespace Aquarium.SystemCenter;

internal static class Program
{
    [STAThread] private static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new CenterWindow()); }
}

internal sealed class CenterWindow : Form
{
    private readonly Func<CancellationToken, SystemCenterReport> collect;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Button refresh = new() { Text = "Refresh observations", AutoSize = true };
    private readonly Button export = new() { Text = "Export JSON…", AutoSize = true, Enabled = false };
    private readonly TextBox filter = new() { Width = 220, PlaceholderText = "Filter service names" };
    private readonly TextBox overview = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label summary = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly DataGridView table = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoGenerateColumns = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private SystemCenterReport? report;
    private bool busy, exporting;
    internal bool IsCollecting => busy;
    internal CenterWindow(Func<CancellationToken, SystemCenterReport>? collector = null)
    {
        collect = collector ?? (token => SystemObservations.Capture(new WindowsSystemObservationProvider(), token));
        Text = "Aquarium System Center"; AccessibleName = Text; Size = new(1100, 750); MinimumSize = new(740, 560);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        actions.Controls.AddRange([refresh, export]);
        var pages = new TabControl { Dock = DockStyle.Fill };
        var infoPage = new TabPage("OS and memory"); infoPage.Controls.Add(overview);
        var servicePage = new TabPage("Services");
        var serviceActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        serviceActions.Controls.Add(new Label { Text = "Service filter", AutoSize = true, Padding = new Padding(0, 4, 4, 0) }); serviceActions.Controls.Add(filter);
        table.AccessibleName = "Observed account-visible Windows services"; overview.AccessibleName = "OS and memory observations"; filter.AccessibleName = "Filter service names";
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Service", Width = 220, SortMode = DataGridViewColumnSortMode.NotSortable });
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Display name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180, SortMode = DataGridViewColumnSortMode.NotSortable });
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Observed state", Width = 180, SortMode = DataGridViewColumnSortMode.NotSortable });
        servicePage.Controls.Add(table); servicePage.Controls.Add(serviceActions);
        pages.TabPages.AddRange([infoPage, servicePage]);
        var coverage = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 110,
            Text = "Read-only local observations; refresh manually. Service state is not a health or security verdict. Only user-mode services visible to this account are returned; Windows may omit services without query permission. Drivers are excluded. Does not query user/computer names, service accounts or command lines. Native process IDs are discarded. JSON includes OS details and service names; save/share only after review. No settings or services are changed and nothing uploads automatically." };
        coverage.AccessibleName = "Observation coverage and export details";
        Controls.Add(pages); Controls.Add(summary); Controls.Add(actions); Controls.Add(coverage);
        Aquarium.Tools.FamilyWindow.Apply(this);
        pages.DrawMode = TabDrawMode.OwnerDrawFixed;
        pages.DrawItem += (_, args) =>
        {
            using var background = new SolidBrush(BackColor); args.Graphics.FillRectangle(background, args.Bounds);
            TextRenderer.DrawText(args.Graphics, pages.TabPages[args.Index].Text, pages.Font, args.Bounds, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        table.EnableHeadersVisualStyles = false; table.BackgroundColor = BackColor;
        table.DefaultCellStyle.BackColor = table.ColumnHeadersDefaultCellStyle.BackColor = BackColor;
        table.DefaultCellStyle.ForeColor = table.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        table.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight; table.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        void SizeText()
        {
            summary.MaximumSize = new(Math.Max(100, ClientSize.Width - Padding.Horizontal), 0);
            coverage.Height = Math.Min(180, Math.Max(110, coverage.Font.Height * 4 + 12));
            table.RowTemplate.Height = table.Font.Height + 8;
            foreach (DataGridViewRow row in table.Rows) row.Height = table.RowTemplate.Height;
            table.Columns[0].Width = Math.Max(180, TextRenderer.MeasureText("Service identifier", table.Font).Width + 24);
            table.Columns[2].Width = Math.Max(180, TextRenderer.MeasureText("Continue pending", table.Font).Width + 24);
        }
        SizeChanged += (_, _) => SizeText(); table.FontChanged += (_, _) => SizeText(); SizeText();
        filter.TextChanged += (_, _) => RenderServices();
        refresh.Click += async (_, _) => await RefreshAsync(); export.Click += async (_, _) => await ExportAsync();
        Shown += async (_, _) => await RefreshAsync(); FormClosed += (_, _) => lifetime.Cancel();
    }
    internal async Task RefreshAsync()
    {
        if (busy || exporting || IsDisposed) return;
        busy = true; refresh.Enabled = export.Enabled = false; summary.Text = "Reading local observations…";
        try
        {
            var next = await Task.Run(() => collect(lifetime.Token), lifetime.Token);
            if (IsDisposed) return;
            report = next; export.Enabled = true;
            summary.Text = $"Observed {next.RecordedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · Memory: {next.Memory.State} · Services: {next.Services.State} ({next.Services.Services.Length:N0} observed){(next.Services.Truncated ? " · buffer limited" : "")}";
            overview.Lines = [$"OS: {next.OsDescription}", $"OS architecture: {next.OsArchitecture}", $"Process architecture: {next.ProcessArchitecture}",
                $"Runtime: {next.Runtime}", $"Logical processors visible to this app: {next.VisibleLogicalProcessors}", "",
                $"Physical memory observation: {next.Memory.State} ({next.Memory.Code})",
                next.Memory.TotalBytes is { } total && next.Memory.AvailableBytes is { } available ? $"Physical RAM in use: {(total - available) / 1073741824.0:F2} / {total / 1073741824.0:F2} GiB" : "Physical RAM: Unknown",
                "", $"Service observation: {next.Services.State} ({next.Services.Code})", next.Services.Coverage];
            RenderServices();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed) { report = null; table.Rows.Clear(); overview.Text = "Observations unavailable. No prior reading is presented as current."; summary.Text = "Observations unavailable; no healthy result is inferred."; } }
        finally { busy = false; if (!IsDisposed) refresh.Enabled = true; }
    }
    private void RenderServices()
    {
        table.Rows.Clear();
        foreach (var row in report?.Services.Services ?? [])
            if ((row.Name + " " + row.DisplayName).Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)) table.Rows.Add(row.Name, row.DisplayName, row.State);
    }
    private async Task ExportAsync()
    {
        if (busy || exporting || report is not { } snapshot) return;
        using var picker = new SaveFileDialog { Filter = "JSON observations|*.json", FileName = "system-center.json", DefaultExt = "json", OverwritePrompt = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { ReportExport.ValidateDestination(picker.FileName, AppContext.BaseDirectory); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException) { MessageBox.Show(this, ex.Message, "Choose a report destination"); return; }
        if (MessageBox.Show(this, "This exports the full snapshot, including services hidden by the filter, OS/runtime details and account-visible service names. Save this reviewed snapshot?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        exporting = true; refresh.Enabled = export.Enabled = false;
        try
        {
            await ReportExport.SaveAsync(picker.FileName, AppContext.BaseDirectory, snapshot, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Snapshot could not be saved"); }
        finally { exporting = false; if (!IsDisposed) refresh.Enabled = export.Enabled = true; }
    }
}
