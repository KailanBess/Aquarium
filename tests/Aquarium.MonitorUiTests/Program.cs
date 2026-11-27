using Aquarium.SystemMonitor;
using System.Diagnostics;

internal static class MonitorUiChecks
{
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static double Contrast(Color first, Color second)
    {
        double Linear(byte channel) { double value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        double Light(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        double a = Light(first), b = Light(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void PumpUntil(Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("UI refresh timed out");
        task.GetAwaiter().GetResult();
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!condition()) throw new Exception("Initial measured UI snapshot did not render within the bounded wait");
    }
    [STAThread] private static void Main(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Choose one absolute generated-fixture screenshot directory.");
        if (args.Length == 1) Directory.CreateDirectory(args[0]);
        ApplicationConfiguration.Initialize(); int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            long tick = 1000;
            using var form = new MonitorWindow(_ => new(DateTimeOffset.UtcNow, new(100, 300, 200), 1000, 400,
                [new(42, 1, "Generated fixture A", 100, 4096), new(43, 1, "Generated fixture B", 50, 2048)], 1, false, tick += 1000));
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
            form.Font = new Font("Segoe UI", 10 * scale);
            form.Show(); Application.DoEvents(); PumpUntil(form.RefreshMeasurements());
            var controls = Descendants(form).ToArray();
            var table = controls.OfType<DataGridView>().Single();
            // Shown starts the first background capture. A concurrent refresh can return
            // immediately while that capture still owns the guard; await the rendered state.
            PumpUntil(() => table.Rows.Count == 2);
            Check(table.Rows.Count == 2 && table.Rows[0].Cells[1].Value?.ToString() == "Generated fixture A", "Measured process table failed");
            table.Rows[1].Selected = true;
            var filter = controls.OfType<TextBox>().Single(); filter.Text = "fixture B";
            Check(table.Rows.Count == 1 && table.Rows[0].Cells[0].Value?.ToString() == "43", "Process filter failed"); filter.Clear();
            Check(table.SelectedRows.Count == 1 && table.SelectedRows[0].Cells[0].Value?.ToString() == "43", "Filter refresh changed or duplicated the selected process");
            controls.OfType<ComboBox>().Single().SelectedIndex = 3;
            Check(table.Rows.Count == 2 && table.Rows[0].Cells[0].Value?.ToString() == "42", "PID sorting failed");
            var pause = controls.OfType<Button>().Single(b => b.Text == "Pause"); pause.PerformClick();
            Check(pause.Text == "Resume" && controls.OfType<Label>().Any(l => l.Text.Contains("PAUSED")), "Paused snapshot was not labeled");
            pause.PerformClick(); PumpUntil(form.RefreshMeasurements());
            Check(table.Rows[0].Cells[2].Value?.ToString() == "Unknown", "Resume invented a CPU rate");
            var client = form.ClientRectangle;
            Check(table.Height > 150 && table.Width > 500 && client.Contains(table.Bounds), "Process table is clipped or overlaps the form edge");
            Check(controls.OfType<Label>().All(l => l.Parent is not null && l.Bottom <= l.Parent.ClientSize.Height + 1 && l.Right <= l.Parent.ClientSize.Width + 1), "Summary or coverage label is clipped");
            var style = table.Rows[0].Cells[1].InheritedStyle;
            Check(Contrast(style.ForeColor, style.BackColor) >= 4.5, "Process cell text contrast is too low");
            var heading = table.Columns[1].HeaderCell.InheritedStyle;
            Check(Contrast(heading.ForeColor, heading.BackColor) >= 4.5, "Process heading text contrast is too low");
            Check(table.Rows[0].Height >= table.Font.Height + 6 && table.ColumnHeadersHeight >= table.Font.Height + 6, "Scaled process rows/headings clip text");
            Check(table.Columns[2].Width >= TextRenderer.MeasureText("Unknown", table.Font).Width + 10, "Unknown CPU state is clipped at large text size");
            using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            if (args.Length == 1) image.Save(Path.Combine(args[0], $"monitor-fixture-{scale * 100:F0}.png"));
            form.Close(); Application.DoEvents();
        }
        Console.WriteLine($"System Monitor: {checks} generated-data native UI checks passed at 100/150/200% font size. No live process list was exported.");
    }
}
