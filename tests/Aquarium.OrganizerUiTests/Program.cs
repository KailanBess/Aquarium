using FileOrganizer;
using System.Diagnostics;

internal static class OrganizerUiChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Complete(Task task)
    {
        var watch = Stopwatch.StartNew(); while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Organizer UI check timed out."); task.GetAwaiter().GetResult();
    }
    [STAThread] private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string root = Path.Combine(Path.GetTempPath(), "Aquarium-organizer-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "first.txt"), second = Path.Combine(root, "second.txt"), missing = Path.Combine(root, "missing.txt");
            File.WriteAllText(first, "generated duplicate fixture"); File.WriteAllText(second, "generated duplicate fixture");
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using var window = new OrganizerWindow(Path.Combine(root, "absent-appearance.json"));
                window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-4000, -4000);
                window.Show(); Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var fonts = Descendants(window).Prepend(window).Select(c => (Control: c, Family: c.Font.FontFamily, Size: c.Font.Size)).ToArray();
                foreach (var font in fonts) font.Control.Font = new Font(font.Family, font.Size * scale);
                var export = Descendants(window).OfType<Button>().Single(c => c.Text.StartsWith("Save new comparison"));
                var cancel = Descendants(window).OfType<Button>().Single(c => c.Text.StartsWith("Cancel comparison"));
                var review = Descendants(window).OfType<TextBox>().Single(c => c.ReadOnly);
                Check(!export.Enabled && !cancel.Enabled, "Idle UI offered a nonexistent report or operation");
                Complete(window.ComparePathsAsync([first, second, missing])); Application.DoEvents();
                Check(export.Enabled && !cancel.Enabled && review.Text.Contains("1 matching-content groups"), "Comparison result/export state was lost");
                Check(review.Text.Contains("Partial comparison") && review.Text.Contains("missing-or-unavailable"), "Skipped paths inferred complete coverage");
                Check(review.Text.Contains(first) && review.Text.Contains(second) && review.Text.Contains("never overwritten"), "Reviewed paths or export disclosure missing");
                Check(review.ClientSize.Height >= review.Font.Height * 2 && review.ScrollBars == ScrollBars.Both, "Review became unreadable at enlarged fonts");
                if (args.Length == 1)
                { Directory.CreateDirectory(args[0]); using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(args[0], "organizer-" + (int)(scale * 100) + ".png")); }
                Descendants(window).OfType<TextBox>().Single(c => !c.ReadOnly).Text = "reviewed-prefix";
                Check(!export.Enabled, "Changing the operation left a stale report export enabled");
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                Complete(window.ComparePathsAsync([first, second], canceled.Token));
                Check(review.Text.StartsWith("Canceled") && !export.Enabled && !cancel.Enabled, "Canceled UI presented a completed report");
                Check(File.ReadAllText(first) == "generated duplicate fixture" && File.ReadAllText(second) == "generated duplicate fixture", "UI comparison changed generated files");
                window.Close(); Application.DoEvents();
            }
            Console.WriteLine($"File Organizer: {checks} generated-data native comparison UI checks passed at 100/150/200% fonts. No rename/move or user profile was accessed.");
        }
        finally
        {
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-organizer-ui-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup.");
            Directory.Delete(full, true);
        }
    }
}
