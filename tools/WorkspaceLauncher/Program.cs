using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WorkspaceLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            using var profile = WorkspaceWindow.OpenProfile();
            Application.Run(new WorkspaceWindow());
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Aquarium Workspace Launcher"); }
    }
}

internal sealed record WorkspaceApp(string Executable, int X, int Y, int Width, int Height);
internal sealed record Workspace(string Name, WorkspaceApp[] Apps);

internal sealed class WorkspaceWindow : Form
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AquariumWorkspaceLauncher", "workspaces.json");
    private readonly CheckedListBox windows = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly ListBox saved = new() { Dock = DockStyle.Fill };
    private readonly TextBox name = new() { Width = 180, MaxLength = 100, PlaceholderText = "Workspace name" };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly TextBox status = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Bottom, Height = 118, ScrollBars = ScrollBars.Vertical };
    private List<Workspace> workspaces;

    internal static FileStream OpenProfile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        try { return File.Open(StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("The workspace profile is in use or unavailable.", ex); }
    }

    internal WorkspaceWindow()
    {
        workspaces = File.Exists(StorePath)
            ? Aquarium.Tools.FamilyWindow.ReadRecords<List<Workspace>>(StorePath, records => records.Count <= 200 && records.All(w => w is not null && !string.IsNullOrWhiteSpace(w.Name) && w.Name.Length <= 100 && w.Apps is not null && w.Apps.Length <= 100 && w.Apps.All(a => a is not null && Path.IsPathFullyQualified(a.Executable) && Path.GetExtension(a.Executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) && a.Width > 0 && a.Height > 0)))
            : [];
        Text = "Aquarium Workspace Launcher";
        Size = new Size(900, 580);
        MinimumSize = new Size(660, 380);
        saved.AccessibleName = "Saved workspaces";
        windows.AccessibleName = "Open desktop apps";
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        content.Controls.Add(saved, 0, 0);
        content.Controls.Add(windows, 1, 0);
        Controls.Add(content);
        Controls.Add(status);
        Controls.Add(actions);
        AddAction("Refresh open apps", () => { RefreshWindows(); return Task.CompletedTask; });
        actions.Controls.Add(name);
        AddAction("Save selected apps", () => { SaveWorkspace(); return Task.CompletedTask; });
        AddAction("Open workspace", OpenWorkspace);
        AddAction("Remove workspace…", () => { RemoveWorkspace(); return Task.CompletedTask; });
        ReloadSaved(-1);
        RefreshWindows();
        Aquarium.Tools.FamilyWindow.Apply(this);
    }

    private void AddAction(string text, Func<Task> run)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) =>
        {
            actions.Enabled = saved.Enabled = windows.Enabled = false;
            try { await run(); }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            finally { if (!IsDisposed) actions.Enabled = saved.Enabled = windows.Enabled = true; }
        };
        actions.Controls.Add(button);
    }

    private void RefreshWindows()
    {
        windows.Items.Clear();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == 0 ||
                        IsIconic(process.MainWindowHandle) || IsZoomed(process.MainWindowHandle)) continue;
                    var executable = process.MainModule?.FileName;
                    if (executable is null || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        !GetWindowRect(process.MainWindowHandle, out var bounds) || !found.Add(executable)) continue;
                    var app = new WorkspaceApp(executable, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                    windows.Items.Add(new OpenApp(process.MainWindowTitle, app));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        status.Text = "Restore and arrange the windows you want, refresh the list, then select them and save a workspace.";
    }

    private void SaveWorkspace()
    {
        var label = name.Text.Trim();
        var apps = windows.CheckedItems.OfType<OpenApp>().Select(item => item.App).ToArray();
        if (label.Length == 0 || apps.Length == 0) throw new InvalidOperationException("Enter a name and select at least one open app.");
        if (workspaces.Any(item => item.Name.Equals(label, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose a unique workspace name.");
        Commit([.. workspaces, new Workspace(label, apps)]);
        ReloadSaved(workspaces.Count - 1);
        status.Text = $"Saved {apps.Length} app window(s) in {label}.";
    }

    private async Task OpenWorkspace()
    {
        if (saved.SelectedIndex < 0) throw new InvalidOperationException("Select a saved workspace.");
        var workspace = workspaces[saved.SelectedIndex];
        if (MessageBox.Show(this, string.Join(Environment.NewLine, workspace.Apps.Select(app => app.Executable)),
                "Open apps in " + workspace.Name + "?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        status.Text = "Opening " + workspace.Name + "…";
        var results = new List<string>();
        foreach (var app in workspace.Apps)
        {
            if (IsDisposed) return;
            try
            {
                var path = Path.GetFullPath(app.Executable);
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new FileNotFoundException("The saved executable is unavailable.", path);
                var handle = FindWindow(path);
                if (handle == 0)
                {
                    using var launch = Process.Start(new ProcessStartInfo(path)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(path)!
                    });
                    for (var attempt = 0; attempt < 25 && handle == 0 && !IsDisposed; attempt++)
                    {
                        await Task.Delay(200);
                        handle = FindWindow(path);
                    }
                }
                if (handle == 0) throw new InvalidOperationException("No matching window was available to position.");
                if (IsIconic(handle) || IsZoomed(handle)) ShowWindowAsync(handle, 9);
                var area = Screen.FromPoint(new Point(app.X, app.Y)).WorkingArea;
                int width = Math.Clamp(app.Width, 1, area.Width);
                int height = Math.Clamp(app.Height, 1, area.Height);
                int x = Math.Clamp(app.X, area.Left, area.Right - width);
                int y = Math.Clamp(app.Y, area.Top, area.Bottom - height);
                if (!SetWindowPos(handle, 0, x, y, width, height, 0x0014))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                results.Add(Path.GetFileName(path) + " · positioned");
            }
            catch (Exception ex) { results.Add(Path.GetFileName(app.Executable) + " · " + ex.Message); }
        }
        if (!IsDisposed) status.Lines = results.ToArray();
    }

    private static nint FindWindow(string executable)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase) &&
                        process.MainWindowHandle != 0) return process.MainWindowHandle;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
            return 0;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private void RemoveWorkspace()
    {
        int index = saved.SelectedIndex;
        if (index < 0 || MessageBox.Show(this, "Remove the saved workspace?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var next = workspaces.ToList();
        next.RemoveAt(index);
        Commit(next);
        ReloadSaved(-1);
    }

    private void ReloadSaved(int index)
    {
        saved.Items.Clear();
        saved.Items.AddRange(workspaces.Select(item => item.Name).ToArray());
        saved.SelectedIndex = index;
    }

    private void Commit(List<Workspace> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temporary = StorePath + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next));
            File.Move(temporary, StorePath, true);
            workspaces = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record OpenApp(string Title, WorkspaceApp App)
    {
        public override string ToString() => Title + " — " + Path.GetFileName(App.Executable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
