using Aquarium.Core;
using Aquarium.SystemCenter;
using System.Diagnostics;

internal static class CenterUiChecks
{
    private static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    private static void Until(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!condition()) throw new Exception("Generated UI observation timed out");
    }
    [STAThread] private static void Main(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Choose an absolute fixture directory.");
        if (args.Length == 1) Directory.CreateDirectory(args[0]);
        ApplicationConfiguration.Initialize(); int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            int calls = 0;
            var report = new SystemCenterReport(1, DateTimeOffset.UtcNow, "Generated OS", "X64", "X64", "Generated runtime", 4,
                new(DiagnosticState.Available, "generated-memory", 8UL * 1073741824, 4UL * 1073741824),
                new(DiagnosticState.Warning, "service-buffer-limited", true, [new("GeneratedA", "Generated service A", "Stopped"), new("GeneratedB", "Generated service B", "Running")]));
            using var form = new CenterWindow(_ => { if (Interlocked.Increment(ref calls) > 1) throw new IOException("Generated failure"); return report; });
            form.StartPosition = FormStartPosition.Manual; form.Location = new(-4000, -4000); form.ShowInTaskbar = false;
            form.Font = new Font("Segoe UI", 10 * scale); form.Show(); Application.DoEvents(); Until(() => !form.IsCollecting && calls == 1);
            var controls = Children(form).ToArray(); var table = controls.OfType<DataGridView>().Single();
            var pages = controls.OfType<TabControl>().Single();
            var overview = controls.OfType<TextBox>().Single(c => c.AccessibleName == "OS and memory observations");
            var coverage = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Observation coverage and export details");
            Check(overview.Text.Contains("Generated OS") && overview.Text.Contains("4.00 / 8.00"), "OS/memory summary did not show actual fixture values");
            Check(controls.OfType<Label>().Any(l => l.Text.Contains("Warning") && l.Text.Contains("buffer limited")), "Partial enumeration was hidden");
            Check(coverage.ReadOnly && coverage.ScrollBars == ScrollBars.Vertical && coverage.Text.Contains("not a health"), "Coverage explanation is missing or cannot scroll");
            pages.SelectedIndex = 1; Application.DoEvents();
            Check(table.Rows.Count == 2 && table.Rows[0].Cells[2].Value?.ToString() == "Stopped", "Stopped service was rewritten as failed/healthy");
            var filter = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Filter service names"); filter.Text = "generatedb";
            Check(table.Rows.Count == 1 && table.Rows[0].Cells[2].Value?.ToString() == "Running", "Service filtering failed"); filter.Clear();
            Check(table.Rows[0].Height >= table.Font.Height + 6 && table.Columns[2].Width >= TextRenderer.MeasureText("Continue pending", table.Font).Width + 10, "Scaled state/row text is clipped");
            Check(table.Parent!.ClientRectangle.Contains(table.Bounds) && table.Height >= 120, "Service table is clipped");
            if (args.Length == 1) { using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(args[0], $"center-services-{scale * 100:F0}.png")); }
            form.Size = new(740, 560); Application.DoEvents();
            Check(table.Height >= 80 && controls.OfType<Label>().All(l => l.Parent!.ClientRectangle.Contains(l.Bounds)), "Small-window enlarged-text layout is unusable");
            var task = form.RefreshAsync(); Until(() => task.IsCompleted); task.GetAwaiter().GetResult();
            Check(table.Rows.Count == 0 && overview.Text.Contains("unavailable") && !controls.OfType<Button>().Single(b => b.Text == "Export JSON…").Enabled, "Failed refresh retained current rows or allowed stale export");
            form.Close(); Application.DoEvents();
        }
        Console.WriteLine($"System Center: {checks} generated-data native UI checks passed at 100/150/200% font sizes. No live service data exported.");
    }
}
