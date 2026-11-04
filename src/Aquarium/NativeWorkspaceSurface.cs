using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Aquarium;

// A real clipping parent separates the foreign HWND from WinUI's animated visuals.
public sealed class NativeWorkspaceSurface : IDisposable
{
    public nint Handle { get; private set; }
    private bool composited;
    public NativeWorkspaceSurface(nint owner)
    {
        Handle = CreateWindowEx(0, "STATIC", "Aquarium app surface", 0x40000000 | 0x02000000 | 0x04000000 | 4, 0, 0, 1, 1, owner, 0, 0, 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the app surface.");
    }
    // GDI/WinForms needs its own redirected surface above WinUI's composition tree.
    // WinUI child apps keep their existing composition path.
    public void SetLegacyRendering(bool enabled)
    {
        if (composited == enabled) return;
        var style = HostedAppSession.Native.GetWindowLongPtr(Handle, -20).ToInt64();
        HostedAppSession.Native.SetStyle(Handle, -20, enabled ? style | 0x00080000L : style & ~0x00080000L);
        if (enabled && !SetLayeredWindowAttributes(Handle, 0, 255, 2)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not prepare the app drawing surface.");
        composited = enabled;
    }
    public void Place(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) { Hide(); return; }
        if (!HostedAppSession.Native.SetWindowPos(Handle, 0, x, y, width, height, 0x0040 | 0x0010 | 0x0100)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void Hide() => HostedAppSession.Native.ShowWindow(Handle, 0);
    public void Dispose() { if (Handle != 0) { DestroyWindow(Handle); Handle = 0; } }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
