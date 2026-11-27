using FileOrganizer;
using System.Diagnostics;

internal static class FolderUiChecks
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Complete(Task task)
    {
        var watch = Stopwatch.StartNew(); while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Folder UI check timed out."); task.GetAwaiter().GetResult();
    }
    [STAThread] private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize(); string root = Path.Combine(Path.GetTempPath(), "Aquarium-folder-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string folder = Path.Combine(root, "fixture"), child = Path.Combine(folder, "child"); Directory.CreateDirectory(child);
            string first = Path.Combine(folder, "first.txt"); File.WriteAllText(first, "Generated preview fixture"); File.WriteAllText(Path.Combine(folder, "second.json"), "{}");
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                string profile = Path.Combine(root, "profile-" + scale);
                using var window = new OrganizerWindow(Path.Combine(root, "absent-appearance.json"), profile);
                window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-4000, -4000);
                window.Show(); Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var fonts = Descendants(window).Prepend(window).Select(c => (Control: c, Family: c.Font.FontFamily, Size: c.Font.Size)).ToArray();
                foreach (var font in fonts) font.Control.Font = new Font(font.Family, font.Size * scale);
                Check(!Directory.Exists(profile), "Startup created bookmark records without an edit");
                var main = Descendants(window).OfType<TabControl>().Single(c => c.TabPages.Cast<TabPage>().Any(p => p.Text == "File operations"));
                Check(main.TabCount == 2 && main.SelectedTab!.Text == "File operations", "Existing operations page/default was replaced");
                Complete(window.OpenFolderAsync(folder)); Application.DoEvents();
                var grid = Descendants(main.SelectedTab!).OfType<DataGridView>().Single();
                Check(grid.VirtualMode && grid.ReadOnly && grid.RowCount == 3, "Bounded folder listing/grid state incorrect");
                var status = Descendants(window).OfType<TextBox>().Single(c => c.AccessibleName == "Observed folder coverage and bookmark status");
                Check(status.Text.Contains(folder) && status.Text.Contains("listing-complete"), "Observed path/coverage was not presented");
                var filter = Descendants(window).OfType<TextBox>().Single(c => c.AccessibleName == "Filter names within the observed listing");
                filter.Text = "FIRST"; Application.DoEvents();
                Check(grid.RowCount == 1 && grid.Rows[0].Cells[0].Value?.ToString() == "first.txt", "Filename filter lost case-insensitive match");
                grid.Rows[0].Selected = true;
                Descendants(window).OfType<Button>().Single(b => b.Text.StartsWith("Replace operation selection")).PerformClick(); Application.DoEvents();
                var selected = Descendants(window).OfType<ListBox>().Single();
                Check(main.SelectedTab!.Text == "File operations" && selected.Items.Count == 1 && selected.Items[0]?.ToString() == first, "Explicit operation selection transfer failed");
                Descendants(window).OfType<TextBox>().Single(c => c.AccessibleName == "Rename prefix").Text = "reviewed-";
                Descendants(window).OfType<Button>().Single(b => b.Text == "Preview rename").PerformClick();
                Check(Descendants(window).OfType<Button>().Single(b => b.Text == "Apply preview").Enabled && File.Exists(first) && !File.Exists(Path.Combine(folder, "reviewed-first.txt")), "Preview mutated files or lost the existing workflow");
                main.SelectedIndex = 1; filter.Text = ""; Application.DoEvents();
                Check(grid.ClientSize.Height >= grid.Font.Height * 3 && status.ScrollBars == ScrollBars.Vertical, "Folder/status area became unreadable at enlarged fonts");
                if (args.Length == 1)
                { Directory.CreateDirectory(args[0]); using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(args[0], "folders-" + (int)(scale * 100) + ".png")); }
                Complete(window.OpenFolderAsync(Path.Combine(root, "missing"))); Application.DoEvents();
                Check(status.Text.Contains("partial/unavailable") && !status.Text.Contains("listing-complete"), "Missing folder inferred a completed empty view");
                Check(File.ReadAllText(first) == "Generated preview fixture" && !Directory.Exists(profile), "Browsing changed source files or wrote default bookmarks");
                window.Close(); Application.DoEvents();
            }
            Console.WriteLine($"Folder browser: {checks} generated-data native UI checks passed at 100/150/200% fonts. No file operation, external app, picker or user profile was used.");
        }
        finally
        {
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-folder-ui-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup.");
            Directory.Delete(full, true);
        }
    }
}
