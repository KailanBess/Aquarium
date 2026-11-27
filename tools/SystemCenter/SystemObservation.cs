using Aquarium.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Aquarium.SystemCenter;

public sealed record MemoryObservation(DiagnosticState State, string Code, ulong? TotalBytes, ulong? AvailableBytes);
public sealed record ServiceObservation(string Name, string DisplayName, string State);
public sealed record ServiceInventory(DiagnosticState State, string Code, bool Truncated, ServiceObservation[] Services,
    string Coverage = "Local Windows user-mode services visible to this account. Windows may silently omit services without query permission. Drivers are excluded. Enumeration uses a 256 KiB buffer and at most 2,048 rows; limits are flagged. This is not a complete inventory or a health/security verdict.");
public sealed record SystemCenterReport(int SchemaVersion, DateTimeOffset RecordedAtUtc, string OsDescription,
    string OsArchitecture, string ProcessArchitecture, string Runtime, int VisibleLogicalProcessors,
    MemoryObservation Memory, ServiceInventory Services);

public interface ISystemObservationProvider
{
    MemoryObservation ReadMemory(CancellationToken token);
    ServiceInventory ReadServices(CancellationToken token);
}

public static class SystemObservations
{
    public static SystemCenterReport Capture(ISystemObservationProvider provider, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(provider); token.ThrowIfCancellationRequested();
        MemoryObservation memory;
        ServiceInventory services;
        try { memory = provider.ReadMemory(token); }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { memory = new(ErrorState(ex), ErrorCode(ex), null, null); }
        token.ThrowIfCancellationRequested();
        try { services = provider.ReadServices(token); }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { services = new(ErrorState(ex), ErrorCode(ex), false, []); }
        token.ThrowIfCancellationRequested();
        if (memory is null || !Enum.IsDefined(memory.State) || !Code(memory.Code) || memory.TotalBytes is 0 || memory.AvailableBytes > memory.TotalBytes ||
            memory.State != DiagnosticState.Available && (memory.TotalBytes is not null || memory.AvailableBytes is not null) ||
            memory.State == DiagnosticState.Available && (memory.TotalBytes is null || memory.AvailableBytes is null))
            memory = new(DiagnosticState.Unknown, "invalid-memory-result", null, null);
        if (services is null || !Enum.IsDefined(services.State) || !Code(services.Code) || services.Services is null || services.Services.Length > 2048 ||
            services.State is not (DiagnosticState.Available or DiagnosticState.Warning) && services.Services.Length != 0 ||
            services.Truncated && services.State != DiagnosticState.Warning ||
            services.Services.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 256 || s.DisplayName is null || s.DisplayName.Length > 256 ||
                s.State is not ("Stopped" or "Start pending" or "Stop pending" or "Running" or "Continue pending" or "Pause pending" or "Paused" or "Unknown")) ||
            services.Services.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != services.Services.Length)
            services = new(DiagnosticState.Unknown, "invalid-service-result", false, []);
        return new(1, DateTimeOffset.UtcNow, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.FrameworkDescription, Environment.ProcessorCount, memory, services);
    }
    private static bool Code(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "\\A[a-z][a-z0-9-]{0,63}\\z");
    private static DiagnosticState ErrorState(Exception ex) => ex is NotSupportedException ? DiagnosticState.Unsupported :
        ex is InvalidDataException ? DiagnosticState.Unknown : DiagnosticState.Unavailable;
    private static string ErrorCode(Exception ex) => ex switch
    {
        NotSupportedException => "platform-unsupported",
        InvalidDataException => "invalid-observation",
        UnauthorizedAccessException => "permission-denied",
        Win32Exception native when native.NativeErrorCode == 5 => "permission-denied",
        _ => "observation-unavailable"
    };
}

public sealed class WindowsSystemObservationProvider : ISystemObservationProvider
{
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeService
    {
        public nint Name, DisplayName;
        public uint Type, State, AcceptedControls, Win32Exit, ServiceExit, Checkpoint, WaitHint, ProcessId, Flags;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(nint manager, int level, uint type, uint state, nint buffer,
        uint size, out uint needed, out uint returned, ref uint resume, string? group);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint handle);

    public MemoryObservation ReadMemory(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var data = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref data)) throw new Win32Exception(Marshal.GetLastWin32Error());
        token.ThrowIfCancellationRequested();
        return new(DiagnosticState.Available, "windows-physical-memory", data.TotalPhysical, data.AvailablePhysical);
    }
    public ServiceInventory ReadServices(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // Enumeration only: no service-control, configuration or remote-machine rights.
        nint manager = OpenSCManager(null, null, 4);
        if (manager == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        const int bytes = 256 * 1024;
        nint buffer = 0;
        try
        {
            buffer = Marshal.AllocHGlobal(bytes); uint resume = 0;
            bool success = EnumServicesStatusEx(manager, 0, 0x30, 3, buffer, bytes, out _, out var returned, ref resume, null);
            int error = success ? 0 : Marshal.GetLastWin32Error();
            if (!success && error != 234) throw new Win32Exception(error);
            token.ThrowIfCancellationRequested();
            return Decode(buffer, bytes, returned, !success || resume != 0, token);
        }
        finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); CloseServiceHandle(manager); }
    }
    internal static ServiceInventory Decode(nint buffer, int bytes, uint count, bool truncated, CancellationToken token = default)
    {
        int stride = Marshal.SizeOf<NativeService>();
        if (buffer == 0 || bytes < 0 || bytes > 256 * 1024 || count > bytes / stride) throw new InvalidDataException("Invalid service buffer.");
        var rows = new List<ServiceObservation>();
        string Text(nint pointer)
        {
            long offset = pointer.ToInt64() - buffer.ToInt64();
            if (offset < (long)count * stride || offset < 0 || offset >= bytes || offset % 2 != 0) throw new InvalidDataException("Invalid service text pointer.");
            int remaining = Math.Min(257, (bytes - (int)offset) / 2);
            for (int length = 0; length < remaining; length++)
                if (Marshal.ReadInt16(pointer, length * 2) == 0) return Marshal.PtrToStringUni(pointer, length)!;
            throw new InvalidDataException("Invalid service text length.");
        }
        for (int i = 0; i < Math.Min(count, 2048); i++)
        {
            token.ThrowIfCancellationRequested();
            var data = Marshal.PtrToStructure<NativeService>(buffer + i * stride);
            var name = Text(data.Name); var display = Text(data.DisplayName);
            if (name.Length == 0) throw new InvalidDataException("Missing service name.");
            rows.Add(new(name, display, data.State switch { 1 => "Stopped", 2 => "Start pending", 3 => "Stop pending", 4 => "Running",
                5 => "Continue pending", 6 => "Pause pending", 7 => "Paused", _ => "Unknown" }));
        }
        if (rows.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count) throw new InvalidDataException("Duplicate service identity.");
        bool limited = truncated || count > 2048;
        return new(limited ? DiagnosticState.Warning : DiagnosticState.Available, limited ? "service-buffer-limited" : "account-visible-services",
            limited, rows.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
