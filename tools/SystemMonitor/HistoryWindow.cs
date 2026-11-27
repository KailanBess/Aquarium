using Aquarium.Core;
using System.Text.Json;

namespace Aquarium.SystemMonitor;

internal sealed class HistoryWindow : Form
{
    private readonly Func<MonitorHistoryReport> snapshot;
    private readonly CancellationTokenSource lifetime = new();
    private bool exporting;
    private readonly Label summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
    private readonly DataGridView table = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    internal HistoryWindow(Func<MonitorHistoryReport> snapshot)
    {
        this.snapshot = snapshot;
        Text = "System Monitor history"; AccessibleName = Text; Size = new(1100, 650); MinimumSize = new(720, 480);
        AutoScroll = true;
        ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
        table.AccessibleName = "Retained history entries";
        foreach (var title in new[] { "Sequence", "Recorded UTC", "State", "CPU %", "RAM in use MiB", "Readable processes", "Unavailable processes", "Truncated" })
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });
        var export = new Button { Text = "Export retained history…", AutoSize = true };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        actions.Controls.Add(export);
        var note = new Label { Dock = DockStyle.Top, AutoSize = true, Text = "Last 900 entries in memory, roughly 30 minutes at 2-second sampling; pause/resume and failures also use entries. No process names or IDs. Closing System Monitor discards history. Missing readings stay unknown; sequence preserves order when the clock changes." };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 300)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(actions, 0, 0); content.Controls.Add(summary, 0, 1); content.Controls.Add(table, 0, 2); content.Controls.Add(note, 0, 3);
        Controls.Add(content);
        Aquarium.Tools.FamilyWindow.Apply(this);
        table.EnableHeadersVisualStyles = false; table.BackgroundColor = BackColor;
        table.DefaultCellStyle.BackColor = table.ColumnHeadersDefaultCellStyle.BackColor = BackColor;
        table.DefaultCellStyle.ForeColor = table.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        table.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight; table.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        bool layingOut = false;
        void LayoutText()
        {
            if (layingOut) return;
            layingOut = true;
            try
            {
                // Reserve space before the outer scrollbar appears. The table keeps
                // a usable height; smaller windows scroll the complete vertical layout.
                int width = Math.Max(100, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
                summary.MaximumSize = note.MaximumSize = new(width, 0);
                int textHeight = summary.GetPreferredSize(new Size(width, 0)).Height +
                    note.GetPreferredSize(new Size(width, 0)).Height + actions.GetPreferredSize(new Size(width, 0)).Height;
                content.RowStyles[2].Height = Math.Max(200, ClientSize.Height - Padding.Vertical - textHeight - 24);
                AutoScrollMinSize = new Size(0, content.GetPreferredSize(new Size(width, 0)).Height + Padding.Vertical);
                table.RowTemplate.Height = table.Font.Height + 8;
                foreach (DataGridViewRow row in table.Rows) row.Height = table.RowTemplate.Height;
            }
            finally { layingOut = false; }
        }
        Layout += (_, _) => LayoutText();
        SizeChanged += (_, _) => LayoutText(); FontChanged += (_, _) => LayoutText(); summary.TextChanged += (_, _) => LayoutText(); LayoutText();
        export.Click += async (_, _) => await Export();
        FormClosed += (_, _) => lifetime.Cancel();
        RefreshHistory();
    }
    internal void RefreshHistory()
    {
        if (IsDisposed) return;
        var report = snapshot();
        summary.Text = $"{report.Entries.Length:N0} retained entries · {report.DroppedEntries:N0} older entries dropped. Latest first; UTC timestamps are observations, not continuous coverage.";
        long? selected = table.SelectedRows.Count == 0 ? null : table.SelectedRows[0].Tag as long?;
        table.SuspendLayout(); table.Rows.Clear();
        foreach (var entry in report.Entries.Reverse())
        {
            string memory = entry.PhysicalBytes is { } total && entry.AvailableBytes is { } available ? ((total - available) / 1048576.0).ToString("F1") : "Unknown";
            int row = table.Rows.Add(entry.Sequence, entry.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), entry.State,
                entry.CpuPercent?.ToString("F1") ?? "Unknown", memory, entry.ReadableProcesses?.ToString() ?? "Unknown",
                entry.UnavailableProcesses?.ToString() ?? "Unknown", entry.ProcessListTruncated?.ToString() ?? "Unknown");
            table.Rows[row].Tag = entry.Sequence;
            if (selected == entry.Sequence) table.Rows[row].Selected = true;
        }
        table.ResumeLayout();
    }
    private async Task Export()
    {
        if (exporting || IsDisposed) return;
        // Capture before the save dialog so sampling during review cannot change the export.
        var report = snapshot();
        using var picker = new SaveFileDialog { Filter = "JSON history|*.json", FileName = "system-monitor-history.json", OverwritePrompt = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        exporting = true;
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
            await AtomicFile.WriteAsync(picker.FileName, (output, token) => output.WriteAsync(data, token).AsTask(), lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "History could not be saved"); }
        finally { exporting = false; }
    }
}
