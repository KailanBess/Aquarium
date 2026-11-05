using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aquarium;

// Hosts only an explicitly marked workspace from the exact process we started.
// Authentication, permission and other unmarked windows are never adopted.
public sealed class HostedAppSession : IDisposable
{
    public const string WorkspaceMarker = "Aquarium.Workspace.v1";
    public Process Process { get; }
    public nint Window { get; private set; }
    private nint parent;
    private nint originalParent;
    private long originalStyle;
    private long originalExStyle;
    private Native.Rect originalBounds;
    public bool IsAlive { get { try { return !Process.HasExited; } catch { return false; } } }
    public bool IsAttached => Window != 0 && Native.IsWindow(Window) && Native.GetParent(Window) == parent;
    public int ProcessId => Process.Id;
    public bool UsesManagedVisibility => Window != 0 && Native.GetProp(Window, "Aquarium.Workspace.ManagedVisibility.v1") == 1;

    public bool HasVisibleStartupWindow()
    {
        if (!IsAlive) return false;
        bool found = false;
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            if (pid != ProcessId || !Native.IsWindowVisible(window) || Native.GetProp(window, WorkspaceMarker) != 0) return true;
            found = true;
            return false;
        }, 0);
        return found;
    }
    public HostedAppSession(string executable, nint parent, string? fixtureArgument = null)
    {
        this.parent = parent;
        if (!WorkspaceCapability.SupportsHosting(executable)) throw new NotSupportedException("This installed version does not support Aquarium workspaces. Install a hosting-compatible build before opening it here. No separate app window was opened.");
        if (!Native.IsWindow(parent)) throw new ArgumentException("Aquarium's workspace window is unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.Environment["AQUARIUM_HOSTED"] = "1";
        if (fixtureArgument is not null) start.ArgumentList.Add(fixtureArgument);
        Process = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
    }
    public async Task AttachAsync(CancellationToken token)
    {
        if (IsAttached) return;
        nint candidate = 0;
        while (candidate == 0)
        {
            token.ThrowIfCancellationRequested();
            if (!IsAlive) throw new InvalidOperationException("The app closed before its workspace was ready.");
            Native.EnumWindows((window, _) =>
            {
                Native.GetWindowThreadProcessId(window, out var pid);
                if (pid == ProcessId && Native.GetProp(window, WorkspaceMarker) == 1) { candidate = window; return false; }
                return true;
            }, 0);
            if (candidate == 0) await Task.Delay(100, token);
        }
        Native.GetWindowThreadProcessId(candidate, out var owner);
        if (owner != ProcessId) throw new InvalidOperationException("Workspace ownership changed.");
        originalParent = Native.GetParent(candidate);
        originalStyle = Native.GetWindowLongPtr(candidate, -16).ToInt64();
        originalExStyle = Native.GetWindowLongPtr(candidate, -20).ToInt64();
        Native.GetWindowRect(candidate, out originalBounds);
        Window = candidate;
        try
        {
            if (!Native.AreDpiAwarenessContextsEqual(Native.GetWindowDpiAwarenessContext(parent), Native.GetWindowDpiAwarenessContext(candidate)))
                throw new InvalidOperationException("This app uses an incompatible display-scaling mode. Use an Aquarium-compatible build.");
            Native.ShowWindow(candidate, 0);
            // Remove top-level frame and taskbar membership before reparenting.
            Native.SetStyle(candidate, -16, (originalStyle & ~0xB1CF0000L) | 0x40000000L | 0x04000000L);
            Native.SetStyle(candidate, -20, originalExStyle & ~0x00040000L);
            Marshal.SetLastPInvokeError(0);
            Native.SetParent(candidate, parent);
            if (Native.GetParent(candidate) != parent) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows refused to host this workspace.");
        }
        catch { Detach(); throw; }
    }
    public void Place(int x, int y, int width, int height)
    {
        if (!IsAttached) return;
        if (width <= 0 || height <= 0) { Hide(); return; }
        bool managedVisibility = Native.GetProp(Window, "Aquarium.Workspace.ManagedVisibility.v1") == 1;
        if (!Native.SetWindowPos(Window, 0, x, y, width, height, (managedVisibility ? 0u : 0x0040u) | 0x0010u | 0x0020u))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not size the app workspace.");
        ShowWorkspace();
    }
    private static readonly uint VisibilityMessage = Native.RegisterWindowMessage("Aquarium.Workspace.Visibility.v1");
    private void ShowWorkspace()
    {
        if (Native.GetProp(Window, "Aquarium.Workspace.ManagedVisibility.v1") == 1)
            Native.PostMessage(Window, VisibilityMessage, 1, 0);
        else Native.ShowWindow(Window, 5);
    }
    public void Hide()
    {
        if (!IsAttached) return;
        Native.ShowWindow(Window, 0);
        if (Native.GetProp(Window, "Aquarium.Workspace.ManagedVisibility.v1") == 1)
            Native.PostMessage(Window, VisibilityMessage, 0, 0);
    }
    public void Focus()
    {
        if (!IsAttached) return;
        var current = Native.GetCurrentThreadId(); var target = Native.GetWindowThreadProcessId(Window, out _);
        var attached = current != target && Native.AttachThreadInput(current, target, true);
        try { Native.SetFocus(Window); }
        finally { if (attached) Native.AttachThreadInput(current, target, false); }
    }
    public async Task<bool> RequestCloseAsync(TimeSpan wait)
    {
        if (!IsAlive) return true;
        if (Window != 0 && !Native.IsWindow(Window)) return true;
        // An unmarked sign-in/startup dialog must be completed or closed by its user.
        nint closeWindow = IsAttached ? Window : 0;
        if (closeWindow == 0)
            Native.EnumWindows((window, _) => { Native.GetWindowThreadProcessId(window, out var pid); if (pid == ProcessId && Native.GetProp(window, WorkspaceMarker) == 1) { closeWindow = window; return false; } return true; }, 0);
        if (closeWindow == 0) return false;
        Native.PostMessage(closeWindow, 0x0010, 0, 0);
        var until = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < until)
        {
            if (!IsAlive || !Native.IsWindow(closeWindow)) return true;
            await Task.Delay(100);
        }
        return false; // No forced termination and no unsaved-work bypass.
    }
    public void Detach()
    {
        if (Window == 0 || !Native.IsWindow(Window)) { Window = 0; return; }
        bool wasVisible = Native.IsWindowVisible(Window);
        Native.ShowWindow(Window, 0); Native.SetParent(Window, originalParent);
        Native.SetStyle(Window, -16, originalStyle); Native.SetStyle(Window, -20, originalExStyle);
        Native.SetWindowPos(Window, 0, originalBounds.Left, originalBounds.Top, Math.Max(400, originalBounds.Right - originalBounds.Left), Math.Max(300, originalBounds.Bottom - originalBounds.Top), 0x0020 | 0x0010);
        if (wasVisible) Native.ShowWindow(Window, 5); Window = 0;
    }
    public void Dispose() { Detach(); Process.Dispose(); }

    public static class Native
    {
        public delegate bool EnumWindow(nint window, nint parameter);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindow callback, nint parameter);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint GetProp(nint window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(nint window, string name, nint data);
        [DllImport("user32.dll")] public static extern nint GetParent(nint window);
        [DllImport("user32.dll", SetLastError = true)] public static extern nint SetParent(nint window, nint parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
        public static void SetStyle(nint window, int index, long value) { Marshal.SetLastPInvokeError(0); var previous = SetWindowLongPtr(window, index, (nint)value); int error = Marshal.GetLastPInvokeError(); if (previous == 0 && error != 0 && (!IsWindow(window) || GetWindowLongPtr(window, index).ToInt64() != value)) throw new Win32Exception(error); }
        [DllImport("user32.dll")] public static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] public static extern bool ShowWindow(nint window, int show);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out Rect rect);
        [DllImport("user32.dll")] public static extern nint GetWindowDpiAwarenessContext(nint window);
        [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint first, uint second, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern nint SetFocus(nint window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    }
}

