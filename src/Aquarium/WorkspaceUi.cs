using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, HostedAppSession> hostedApps = new();
    private HostedAppSession? activeWorkspace;
    private string appPage = "Home";
    private CancellationTokenSource? hostingCancellation;
    private bool closingWorkspaces;
    private bool allowShellClose;
    private string? activeAppId;
    private (int X, int Y, int Width, int Height)? workspaceBounds;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? workspaceMonitor;
    private NativeWorkspaceSurface? nativeSurface;
    private void InitializeWorkspaces()
    {
        AppSurface.LayoutUpdated += (_, _) => PositionWorkspace();
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += TrackWorkspaceFrame;
        nativeSurface = new NativeWorkspaceSurface(WinRT.Interop.WindowNative.GetWindowHandle(this));
        workspaceMonitor = DispatcherQueue.CreateTimer(); workspaceMonitor.Interval = TimeSpan.FromSeconds(1);
        workspaceMonitor.Tick += (_, _) =>
        {
            if (closingWorkspaces || activeWorkspace is null || activeWorkspace.IsAlive) return;
            var ended = activeWorkspace; var id = activeAppId;
            HideWorkspace(); if (id is not null) hostedApps.Remove(id); ended.Dispose(); Render();
            ShowNotice("The app workspace closed. You can open it again from the app page.", InfoBarSeverity.Informational);
        };
        workspaceMonitor.Start();
        AppWindow.Closing += async (_, args) =>
        {
            if (allowShellClose || hostedApps.Count == 0) return;
            args.Cancel = true;
            if (closingWorkspaces) return;
            closingWorkspaces = true; hostingCancellation?.Cancel();
            try
            {
                foreach (var pair in hostedApps.ToArray())
                {
                    if (pair.Value.IsAlive)
                    {
                        DisplayWorkspace(pair.Key, pair.Value);
                        WorkspaceStatus.Text = "Closing app. Finish any save or confirmation dialog to continue.";
                        if (!await pair.Value.RequestCloseAsync(TimeSpan.FromSeconds(5)))
                        { WorkspaceStatus.Text = "The app is still open, has a dialog, or is keeping itself in the tray. Close it normally in the app, then try again. Aquarium has stayed open to protect your work."; return; }
                    }
                    pair.Value.Dispose(); hostedApps.Remove(pair.Key);
                }
                activeWorkspace = null; allowShellClose = true; Close();
            }
            finally { closingWorkspaces = false; }
        };
    }
    private async Task OpenHostedApp(InstalledApp app)
    {
        if (hostingCancellation is not null || closingWorkspaces) return;
        var executable = installer.VerifiedExecutable(app);
        var window = nativeSurface!.Handle;
        if (hostedApps.TryGetValue(app.AppId, out var old) && !old.IsAlive) { old.Dispose(); hostedApps.Remove(app.AppId); }
        if (!hostedApps.TryGetValue(app.AppId, out var session))
        {
            // Do not steal or reparent a separately opened copy.
            using var running = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)).FirstOrDefault();
            if (running is not null) { ShowNotice("This app is already open separately. Close that copy before opening it inside Aquarium.", InfoBarSeverity.Warning); return; }
            session = new HostedAppSession(executable, window); hostedApps.Add(app.AppId, session);
        }
        if (page is "Home" or "Store" or "Library") appPage = page;
        DisplayWorkspace(app.AppId, session);
        WorkspaceStatus.Text = session.IsAttached ? "Home returns to your app page and keeps this session running." : "Starting workspace… Complete any app sign-in or startup dialog if requested.";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2)); hostingCancellation = cancellation;
        try
        {
            await session.AttachAsync(cancellation.Token);
            WorkspacePlaceholder.Visibility = Visibility.Collapsed;
            if (activeWorkspace == session && AppWorkspace.Visibility == Visibility.Visible)
            {
                workspaceBounds = null; PositionWorkspace(); session.Focus();
                WorkspaceStatus.Text = "Home returns to your app page and keeps this session running.";
            }
            else session.Hide();
        }
        catch (Exception ex)
        {
            if (!isClosed)
            {
                session.Detach();
                WorkspacePlaceholder.Text = "Workspace unavailable. Return Home to review the app.";
                WorkspaceStatus.Text = ex is OperationCanceledException ? "Workspace preparation stopped. You can return Home and retry. If an app sign-in is open, complete it first." : "Could not host this app: " + ex.Message;
            }
        }
        finally { hostingCancellation = null; }
    }
    private void DisplayWorkspace(string appId, HostedAppSession session)
    {
        activeWorkspace?.Hide(); activeWorkspace = session; activeAppId = appId; workspaceBounds = null;
        WorkspaceTitle.Text = store.Catalog.FirstOrDefault(a => a.Id == appId)?.Name ?? "App workspace";
        WorkspacePlaceholder.Text = "Preparing app workspace…";
        WorkspacePlaceholder.Visibility = session.IsAttached ? Visibility.Collapsed : Visibility.Visible;
        PageScroll.Visibility = Visibility.Collapsed; AppWorkspace.Visibility = Visibility.Visible;
        PositionWorkspace();
    }
    private void PositionWorkspace()
    {
        if (activeWorkspace is null || !activeWorkspace.IsAttached || AppWorkspace.Visibility != Visibility.Visible) return;
        try
        {
            var point = AppSurface.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            var bounds = ((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale), (int)Math.Round(AppSurface.ActualWidth * scale), (int)Math.Round(AppSurface.ActualHeight * scale));
            if (workspaceBounds == bounds) return;
            nativeSurface!.Place(bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4);
            activeWorkspace.Place(0, 0, bounds.Item3, bounds.Item4);
            workspaceBounds = bounds;
        }
        catch (Exception ex) { WorkspaceStatus.Text = "Workspace layout needs attention: " + ex.Message; }
    }
    private void HideWorkspace()
    {
        activeWorkspace?.Hide(); workspaceBounds = null; activeWorkspace = null; activeAppId = null;
        nativeSurface?.Hide();
        AppWorkspace.Visibility = Visibility.Collapsed; PageScroll.Visibility = Visibility.Visible;
    }
    private void TrackWorkspaceFrame(object? sender, object args) => PositionWorkspace();
    private void DisposeWorkspaces()
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= TrackWorkspaceFrame;
        foreach (var session in hostedApps.Values) session.Dispose(); hostedApps.Clear();
        nativeSurface?.Dispose(); nativeSurface = null;
    }
    private void WorkspaceHome(object sender, RoutedEventArgs e)
    {
        hostingCancellation?.Cancel(); HideWorkspace(); SelectPage(appPage); Render();
    }
    private async void WorkspaceClose(object sender, RoutedEventArgs e)
    {
        if (activeWorkspace is null || closingWorkspaces || hostingCancellation is not null) return;
        var session = activeWorkspace; var id = activeAppId;
        closingWorkspaces = true;
        WorkspaceCloseButton.IsEnabled = false;
        try
        {
            if (!await session.RequestCloseAsync(TimeSpan.FromSeconds(5))) { WorkspaceStatus.Text = "The app is still open, has a dialog, or is keeping itself in the tray. Close it normally in the app, then try again. It was not force-closed."; return; }
            session.Dispose(); if (id is not null) hostedApps.Remove(id);
            if (activeWorkspace == session) { HideWorkspace(); SelectPage(appPage); }
            Render();
        }
        finally { closingWorkspaces = false; WorkspaceCloseButton.IsEnabled = true; }
    }
}
