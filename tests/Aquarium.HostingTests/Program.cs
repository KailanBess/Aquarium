using Aquarium;
using System.Diagnostics;
using System.Runtime.InteropServices;

[assembly: System.Reflection.AssemblyMetadata("AquariumWorkspace", "1")]

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--check-builds")
        {
            foreach (var file in args.Skip(1)) { if (!WorkspaceCapability.SupportsHosting(file)) throw new Exception("Build lacks workspace protocol: " + file); Console.WriteLine("PASS: hosting capability · " + Path.GetFileName(file)); }
            return 0;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Length == 1 && args[0].StartsWith("--fixture"))
        {
            using var fixture = new FixtureForm { Text = "Generated hosting fixture", Width = 800, Height = 600 };
            fixture.HandleCreated += (_, _) => { if (args[0] != "--fixture-unmarked") HostedAppSession.Native.SetProp(fixture.Handle, HostedAppSession.WorkspaceMarker, 1); };
            Application.Run(fixture); return 0;
        }
        Application.EnableVisualStyles();
        using var host = new Form { Text = "Aquarium host fixture", Width = 1000, Height = 750, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -20000, Top = -20000 };
        int result = 1;
        host.Shown += async (_, _) =>
        {
            try
            {
                using var session = new HostedAppSession(Environment.ProcessPath!, host.Handle, "--fixture");
                Check(WorkspaceCapability.SupportsHosting(Environment.ProcessPath!), "Hosting capability attribute not read");
                Check(!WorkspaceCapability.SupportsHosting(Path.Combine(Path.GetTempPath(), "nonexistent-fixture.exe")), "Unknown executable accepted");
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await session.AttachAsync(timeout.Token);
                    Check(session.IsAttached && HostedAppSession.Native.GetParent(session.Window) == host.Handle, "Exact process workspace not attached");
                    Check((HostedAppSession.Native.GetWindowLongPtr(session.Window, -16).ToInt64() & 0x40000000) != 0, "Hosted window is not a native child");
                    session.Place(30, 80, 600, 420);
                    HostedAppSession.Native.GetWindowRect(session.Window, out var bounds);
                    var origin = host.PointToScreen(new System.Drawing.Point(30, 80));
                    Check(bounds.Left == origin.X && bounds.Top == origin.Y && bounds.Right - bounds.Left == 600 && bounds.Bottom - bounds.Top == 420, "Host rectangle not respected");
                    int pid = session.ProcessId; session.Hide();
                    Check(!HostedAppSession.Native.IsWindowVisible(session.Window) && session.IsAlive, "Home hide terminated the app or left it visible");
                    session.Place(40, 90, 650, 440); session.Focus();
                    Check(session.ProcessId == pid && HostedAppSession.Native.IsWindowVisible(session.Window), "Resume created a new session");
                    Check(!await session.RequestCloseAsync(TimeSpan.FromMilliseconds(500)) && session.IsAlive, "Close ignored the app's refusal");
                    HostedAppSession.Native.PostMessage(session.Window, 0x8001, 0, 0);
                    await Task.Delay(100);
                    Check(await session.RequestCloseAsync(TimeSpan.FromSeconds(3)), "Graceful close did not finish");
                    Console.WriteLine("PASS: process ownership, native child style, clipping rectangle/resize, Home hide, same-session resume and close refusal/graceful close");
                }
                finally { if (session.IsAlive) session.Process.Kill(); }
                using var unmarked = new HostedAppSession(Environment.ProcessPath!, host.Handle, "--fixture-unmarked");
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                    try { await unmarked.AttachAsync(timeout.Token); throw new Exception("Unmarked dialog was adopted"); }
                    catch (OperationCanceledException) { Check(unmarked.Window == 0, "Unexpected adopted window"); }
                    Console.WriteLine("PASS: unmarked/startup dialogs are never adopted");
                }
                finally { if (unmarked.IsAlive) { unmarked.Process.CloseMainWindow(); if (!unmarked.Process.WaitForExit(1000)) unmarked.Process.Kill(); } }
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { host.Close(); }
        };
        Application.Run(host); return result;
    }
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
    private sealed class FixtureForm : Form
    {
        private bool mayClose;
        public FixtureForm() { Controls.Add(new TextBox { Text = "Generated fixture; no user files", Dock = DockStyle.Top }); FormClosing += (_, args) => args.Cancel = !mayClose; }
        protected override void WndProc(ref Message message) { if (message.Msg == 0x8001) mayClose = true; base.WndProc(ref message); }
    }
}
