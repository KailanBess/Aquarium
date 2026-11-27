using Aquarium.Core;
using System.Diagnostics;
using System.Text.Json;

namespace Aquarium.SystemMonitor;

internal static class Program
{
    [STAThread] private static void Main()
    { ApplicationConfiguration.Initialize(); Application.Run(new MonitorWindow()); }
}

internal sealed class MonitorWindow : Form
{
    private readonly DataGridView table = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Label summary = new() { AutoSize = true, MaximumSize = new Size(1300, 0), Padding = new Padding(4) };
    private readonly TextBox filter = new() { Width = 240, PlaceholderText = "Filter process names" };
    private readonly ComboBox sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button pause = new() { AutoSize = true, Text = "Pause" };
    private readonly Button export = new() { AutoSize = true, Text = "Export snapshot…", Enabled = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly CancellationTokenSource lifetime = new();
    private readonly MeasurementHistory history = new();
    private readonly MonitorSessionHistory session = new();
    private HistoryWindow? historyWindow;
    private readonly Func<CancellationToken, MachineObservation> collect;
    private MonitorReport? report;
    private bool busy, paused;
    private int samplingEpoch;
    internal bool IsSampling => busy;
    public MonitorWindow(Func<CancellationToken, MachineObservation>? collector = null)
    {
        collect = collector ?? WindowsMeasurements.Capture;
        Text = "Aquarium System Monitor"; Size = new Size(1000, 700); MinimumSize = new Size(720, 520);
        AccessibleName = Text; table.AccessibleName = "Measured processes"; filter.AccessibleName = "Filter process names"; sort.AccessibleName = "Process sort order";
        sort.Items.AddRange(["Working set", "CPU", "Name", "PID"]); sort.SelectedIndex = 0;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        var showHistory = new Button { AutoSize = true, Text = "History…" };
        actions.Controls.AddRange([filter, sort, pause, export, showHistory]);
        showHistory.Click += (_, _) =>
        {
            if (historyWindow is null || historyWindow.IsDisposed) historyWindow = new HistoryWindow(session.Snapshot);
            if (!historyWindow.Visible) historyWindow.Show(this);
            historyWindow.Activate();
        };
        foreach (var (property, title, width) in new[] { ("Pid", "PID", 70), ("Name", "Process", 270), ("Cpu", "CPU %", 110), ("Memory", "Working set MiB", 180) })
            table.Columns.Add(new DataGridViewTextBoxColumn { Name = property, HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        var coverage = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(1300, 0), Text =
            "Read-only Windows observations every 2 seconds while open. CPU needs consecutive samples; unavailable readings stay unknown. System CPU covers the primary processor group. Process working sets share pages. GPU, disk/network rates, temperatures and power consumption are not measured. No accounts, uploads or process controls." };
        var header = new Panel { Dock = DockStyle.Top, AutoSize = true }; summary.Dock = DockStyle.Top;
        header.Controls.Add(summary); header.Controls.Add(actions);
        Controls.Add(table); Controls.Add(header); Controls.Add(coverage);
        filter.TextChanged += (_, _) => RenderRows(); sort.SelectedIndexChanged += (_, _) => RenderRows();
        pause.Click += (_, _) => { samplingEpoch++; paused = !paused; pause.Text = paused ? "Resume" : "Pause"; history.Reset(); session.Mark(paused ? "Paused" : "Resumed", DateTimeOffset.UtcNow); historyWindow?.RefreshHistory(); RenderSummary(); };
        export.Click += async (_, _) => await Export();
        timer.Tick += async (_, _) => await RefreshMeasurements();
        Shown += async (_, _) => { timer.Start(); await RefreshMeasurements(); };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); lifetime.Cancel(); };
        Aquarium.Tools.FamilyWindow.Apply(this);
        table.EnableHeadersVisualStyles = false;
        table.BackgroundColor = BackColor;
        table.DefaultCellStyle.BackColor = BackColor; table.DefaultCellStyle.ForeColor = ForeColor;
        table.ColumnHeadersDefaultCellStyle.BackColor = BackColor; table.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        table.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
        table.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        table.GridColor = SystemInformation.HighContrast ? ForeColor : Color.FromArgb(55, 81, 96);
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        var pidColumn = table.Columns[0]; var nameColumn = table.Columns[1];
        var cpuColumn = table.Columns[2]; var memoryColumn = table.Columns[3];
        nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        nameColumn.MinimumWidth = 160;
        void SizeRows()
        {
            if (table.IsDisposed) return;
            int height = table.Font.Height + 8; table.RowTemplate.Height = height;
            foreach (DataGridViewRow row in table.Rows) row.Height = height;
            int WidthOf(string text) => TextRenderer.MeasureText(text, table.Font).Width + 20;
            pidColumn.Width = WidthOf("2147483647");
            cpuColumn.Width = WidthOf("Unknown");
            memoryColumn.Width = Math.Max(WidthOf("Working set MiB"), WidthOf("999999999.9"));
            sort.Width = Math.Max(160, WidthOf("Working set") + 16);
        }
        table.FontChanged += (_, _) => SizeRows(); SizeRows();
        void WrapLabels()
        {
            int width = Math.Max(100, ClientSize.Width - Padding.Horizontal);
            summary.MaximumSize = coverage.MaximumSize = new Size(width, 0);
        }
        SizeChanged += (_, _) => WrapLabels(); WrapLabels();
    }
    internal async Task RefreshMeasurements()
    {
        if (busy || paused || IsDisposed) return;
        busy = true;
        int epoch = samplingEpoch;
        try
        {
            var token = lifetime.Token;
            var observed = await Task.Run(() => collect(token), token);
            if (IsDisposed || paused || epoch != samplingEpoch) return;
            report = history.Observe(observed, Stopwatch.Frequency, Environment.ProcessorCount);
            session.Observe(report); historyWindow?.RefreshHistory();
            export.Enabled = true; RenderSummary(); RenderRows();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed && !paused && epoch == samplingEpoch) { history.Reset(); report = null; session.Mark("Unavailable", DateTimeOffset.UtcNow); historyWindow?.RefreshHistory(); export.Enabled = false; summary.Text = "Measurements unavailable; no zero-load or healthy result is inferred."; table.Rows.Clear(); } }
        finally { busy = false; }
    }
    private void RenderSummary()
    {
        if (report is null) { summary.Text = paused ? "Paused; no snapshot available." : "Waiting for measurements…"; return; }
        string cpu = report.CpuPercent is { } value ? value.ToString("F1") + "%" : "Unknown";
        string memory = report.PhysicalBytes is { } total && report.AvailableBytes is { } available ? $"{(total - available) / 1073741824.0:F2} / {total / 1073741824.0:F2} GiB in use" : "Unknown";
        summary.Text = $"{(paused ? "PAUSED · " : "")}Observed {report.ObservedAtUtc.ToLocalTime():HH:mm:ss} · CPU {cpu} · RAM {memory}\n{report.Processes.Length} readable processes · {report.UnavailableProcesses} unavailable{(report.Truncated ? " · process list truncated at 5,000" : "")}";
    }
    private void RenderRows()
    {
        if (IsDisposed) return;
        var selected = table.SelectedRows.Count == 0 ? null : table.SelectedRows[0].Tag as ProcessReading;
        var rows = (report?.Processes ?? []).Where(p => p.Name.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        rows = sort.SelectedIndex switch { 1 => rows.OrderByDescending(p => p.CpuPercent ?? -1), 2 => rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase), 3 => rows.OrderBy(p => p.Pid), _ => rows.OrderByDescending(p => p.WorkingSetBytes) };
        table.SuspendLayout(); table.Rows.Clear();
        foreach (var row in rows)
        {
            int index = table.Rows.Add(row.Pid, row.Name, row.CpuPercent?.ToString("F1") ?? "Unknown", (row.WorkingSetBytes / 1048576.0).ToString("F1"));
            table.Rows[index].Tag = row;
            if (selected?.Pid == row.Pid && selected.StartedUtcTicks == row.StartedUtcTicks) table.Rows[index].Selected = true;
        }
        table.ResumeLayout();
    }
    private async Task Export()
    {
        if (report is not { } snapshot) return;
        using var picker = new SaveFileDialog { Filter = "JSON snapshot|*.json", FileName = "system-monitor.json", OverwritePrompt = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "This snapshot includes local process names and IDs. Save it to the selected file?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await AtomicFile.WriteAsync(picker.FileName, (output, token) => output.WriteAsync(data, token).AsTask(), lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Snapshot could not be saved"); }
    }
}
