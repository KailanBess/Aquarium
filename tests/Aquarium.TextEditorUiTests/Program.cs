using Aquarium.TextEditor;
using System.Diagnostics;

internal static class UiChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Complete(Task task)
    {
        var watch = Stopwatch.StartNew(); while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Native editor check timed out."); task.GetAwaiter().GetResult();
    }
    [STAThread] private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string root = Path.Combine(Path.GetTempPath(), "Aquarium-editor-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using var window = new EditorWindow(Path.Combine(root, "recovery-" + scale), Path.Combine(root, "absent-appearance.json"));
                window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-4000, -4000);
                window.Show(); Application.DoEvents();
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                string fixture = Path.Combine(root, "fixture-" + scale + ".json"); File.WriteAllText(fixture, "{\"fixture\": 42, \"ok\": true}");
                Complete(window.OpenDocumentAsync(fixture));
                var fonts = Descendants(window).Prepend(window).Select(c => (Control: c, Family: c.Font.FontFamily, Size: c.Font.Size)).ToArray();
                foreach (var font in fonts) font.Control.Font = new Font(font.Family, font.Size * scale);
                Application.DoEvents();
                var tabs = Descendants(window).OfType<TabControl>().Single(); Check(tabs.TabCount == 2, "Startup/open did not retain both local tabs");
                var editor = Descendants(tabs.SelectedTab!).OfType<RichTextBox>().Single(c => !c.ReadOnly);
                editor.AppendText(" "); Application.DoEvents();
                Check(tabs.SelectedTab!.Text.EndsWith("*"), "Text edit was not marked unsaved");
                var previewToggle = Descendants(window).OfType<CheckBox>().Single(c => c.Text.StartsWith("Syntax preview")); previewToggle.Checked = true; Application.DoEvents();
                var preview = Descendants(tabs.SelectedTab).OfType<RichTextBox>().Single(c => c.ReadOnly);
                Check(preview.Text == editor.Text && !preview.Parent!.IsDisposed, "Preview lost generated text");
                editor.Select(editor.TextLength, 0); editor.SelectedText = " "; string edited = editor.Text;
                SyntaxHighlight.Apply(preview, ".json"); editor.Undo();
                Check(editor.Text != edited && preview.Text.Length > 0, "Syntax preview interfered with editable undo history");
                preview.Select(2, 1); Check(preview.SelectionColor != preview.ForeColor, "JSON string preview had no lexical highlighting");
                preview.Text = "\n\n[section]\nkey=value\n; comment\n"; var watch = Stopwatch.StartNew(); SyntaxHighlight.Apply(preview, ".ini");
                Check(watch.Elapsed < TimeSpan.FromSeconds(2) && preview.Text.Contains("key=value"), "INI preview blank lines hung or altered text");
                var wrap = Descendants(window).OfType<CheckBox>().Single(c => c.Text == "Word wrap"); wrap.Checked = true;
                Descendants(window).OfType<Button>().Single(b => b.Text == "New").PerformClick(); Application.DoEvents();
                Check(tabs.TabCount == 3 && Descendants(tabs.SelectedTab!).OfType<RichTextBox>().Single(c => !c.ReadOnly).WordWrap, "New tabs lost wrap preference");
                Check(Descendants(tabs.SelectedTab!).OfType<RichTextBox>().All(c => c.ClientSize.Height >= 100), "A new tab lost usable editing/preview height");
                Check(Descendants(window).OfType<FlowLayoutPanel>().First().Height < window.ClientSize.Height / 2, "Toolbar obscured the editor at enlarged fonts");
                var status = Descendants(window).OfType<TextBox>().Single(c => c.ReadOnly); Check(status.ClientSize.Height >= status.Font.Height + 8 && status.ScrollBars == ScrollBars.Vertical, "Status explanation became unreadable");
                if (args.Length == 1)
                { tabs.SelectedIndex = 1; Complete(Task.Delay(550)); Directory.CreateDirectory(args[0]); using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(args[0], "editor-" + (int)(scale * 100) + ".png")); }
                Complete(window.ShutDownAsync()); Application.DoEvents();
            }
            Console.WriteLine($"Text editor: {checks} generated-data native UI checks passed at 100/150/200% fonts. Pickers, physical displays and screen readers were not exercised.");
        }
        finally
        {
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-editor-ui-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup.");
            Directory.Delete(full, true);
        }
    }
}
