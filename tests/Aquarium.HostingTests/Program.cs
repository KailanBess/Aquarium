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
            using var fixture = new FixtureForm() { Text = "Generated hosting fixture", Width = 800, Height = 600 };
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
                using var surface = new NativeWorkspaceSurface(host.Handle);
                surface.SetLegacyRendering(true);Check((HostedAppSession.Native.GetWindowLongPtr(surface.Handle,-20).ToInt64() & 0x00080000)!=0,"GDI surface not layered");
                surface.SetLegacyRendering(false);Check((HostedAppSession.Native.GetWindowLongPtr(surface.Handle,-20).ToInt64() & 0x00080000)==0,"WinUI surface still layered");
                Console.WriteLine("PASS: GDI composition can be enabled and cleared for WinUI workspaces");
                using var session = new HostedAppSession(args.Length==2 && args[0]=="--fishtank-fixture" ? args[1] : Environment.ProcessPath!, surface.Handle, args.Length==2 && args[0]=="--fishtank-fixture" ? "--workspace-fixture="+Path.Combine(Path.GetTempPath(),"fishtank-host-paint.txt") : "--fixture");
                Check(WorkspaceCapability.SupportsHosting(Environment.ProcessPath!), "Hosting capability attribute not read");
                Check(!WorkspaceCapability.SupportsHosting(Path.Combine(Path.GetTempPath(), "nonexistent-fixture.exe")), "Unknown executable accepted");
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await session.AttachAsync(timeout.Token);
                    Check(session.IsAttached && HostedAppSession.Native.GetParent(session.Window) == surface.Handle, "Exact process workspace not attached");
                    Check((HostedAppSession.Native.GetWindowLongPtr(session.Window, -16).ToInt64() & 0x40000000) != 0, "Hosted window is not a native child");
                    surface.SetLegacyRendering(session.UsesManagedVisibility);
                    surface.Place(30, 80, 600, 420); session.Place(0, 0, 600, 420);
                    HostedAppSession.Native.GetWindowRect(session.Window, out var bounds);
                    var origin = host.PointToScreen(new System.Drawing.Point(30, 80));
                    Check(bounds.Left == origin.X && bounds.Top == origin.Y && bounds.Right - bounds.Left == 600 && bounds.Bottom - bounds.Top == 420, "Host rectangle not respected");
                    if(args.Length==2 && args[0]=="--fishtank-fixture") {
                        var file=Path.Combine(Path.GetTempPath(),"fishtank-host-paint.txt");
                        await Task.Delay(600);var report=File.ReadAllText(file);
                        Check(report.StartsWith("Visible=True") && !report.Contains("Visible=False"), "FishTank controls remained hidden: "+report);
                        int originalPid=session.ProcessId;var originalWindow=session.Window;
                        session.Hide();await Task.Delay(600);Check(File.ReadAllText(file).StartsWith("Visible=False"),"Home did not hide the managed form");
                        session.Place(0,0,900,600);await Task.Delay(600);report=File.ReadAllText(file);
                        Check(report.StartsWith("Visible=True") && !report.Contains("Visible=False"),"Resume left FishTank controls hidden: "+report);
                        Check(session.ProcessId==originalPid && session.Window==originalWindow,"Resume replaced the FishTank workspace");
                        Check(await session.RequestCloseAsync(TimeSpan.FromSeconds(3)),"Fixture did not close gracefully");
                        Console.WriteLine("PASS: actual FishTank dashboard controls visible after parenting and Home/Resume, same HWND/process\n"+report);result=0;return;
                    }
                    int pid = session.ProcessId; session.Hide();
                    Check(!HostedAppSession.Native.IsWindowVisible(session.Window) && session.IsAlive, "Home hide terminated the app or left it visible");
                    surface.Place(40,90,650,440); session.Place(0,0,650,440); session.Focus();
                    for(int toggle=0;toggle<20;toggle++) { int x=toggle%2==0?40:180; surface.Place(x,90,650,440);session.Place(0,0,650,440);if(HostedAppSession.Native.GetParent(session.Window)!=surface.Handle || session.ProcessId!=pid)throw new Exception("Sidebar resize detached or replaced the session"); }
                    Console.WriteLine("PASS: 20 sidebar geometry changes retain one child/process in the native clipping surface");
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

