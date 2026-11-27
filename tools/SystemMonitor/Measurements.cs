using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aquarium.SystemMonitor;

public sealed record CpuCounters(ulong Idle, ulong Kernel, ulong User);
public sealed record ProcessCounters(int Pid, long StartedUtcTicks, string Name, long CpuTicks, long WorkingSetBytes);
public sealed record ProcessReading(int Pid, long StartedUtcTicks, string Name, double? CpuPercent, long WorkingSetBytes);
public sealed record MachineObservation(DateTimeOffset ObservedAtUtc, CpuCounters? Cpu, ulong? PhysicalBytes,
    ulong? AvailableBytes, ProcessCounters[] Processes, int UnavailableProcesses, bool Truncated, long MonotonicTime);
public sealed record MonitorReport(int SchemaVersion, DateTimeOffset ObservedAtUtc, double? CpuPercent,
    ulong? PhysicalBytes, ulong? AvailableBytes, ProcessReading[] Processes, int UnavailableProcesses, bool Truncated,
    string CpuCoverage = "Windows primary processor group; process CPU is normalized to Environment.ProcessorCount",
    string MemoryCoverage = "Process working sets can share pages and do not sum to unique physical memory");

public sealed class MeasurementHistory
{
    private MachineObservation? previous;
    public MonitorReport Observe(MachineObservation current, long frequency, int processors)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (frequency <= 0 || processors <= 0 || current.UnavailableProcesses < 0 || current.Processes.Length > 5000 ||
            current.Processes.Any(p => p.Pid < 0 || p.StartedUtcTicks <= 0 || p.CpuTicks < 0 || p.WorkingSetBytes < 0) ||
            current.Processes.Select(p => p.Pid).Distinct().Count() != current.Processes.Length)
            throw new ArgumentException("Invalid measurement.");
        if (current.AvailableBytes > current.PhysicalBytes) throw new ArgumentException("Invalid physical memory counters.");
        double? cpu = null;
        if (current.Cpu is { } now && previous?.Cpu is { } before && current.MonotonicTime > previous.MonotonicTime &&
            now.Idle >= before.Idle && now.Kernel >= before.Kernel && now.User >= before.User)
        {
            double total = (double)(now.Kernel - before.Kernel) + (now.User - before.User);
            double idle = now.Idle - before.Idle;
            if (total > 0 && idle <= total) cpu = (total - idle) / total * 100;
        }
        var old = previous?.Processes.ToDictionary(p => (p.Pid, p.StartedUtcTicks)) ?? [];
        var elapsed = previous is null ? 0 : (current.MonotonicTime - previous.MonotonicTime) / (double)frequency;
        var processes = current.Processes.Select(p =>
        {
            double? percent = null;
            if (elapsed > 0 && old.TryGetValue((p.Pid, p.StartedUtcTicks), out var prior) && p.CpuTicks >= prior.CpuTicks)
            {
                double value = (p.CpuTicks - prior.CpuTicks) / (double)TimeSpan.TicksPerSecond / elapsed / processors * 100;
                if (value >= 0 && value <= 100.5) percent = Math.Min(100, value);
            }
            return new ProcessReading(p.Pid, p.StartedUtcTicks, p.Name, percent, p.WorkingSetBytes);
        }).ToArray();
        previous = current with { Processes = current.Processes.ToArray() };
        return new(1, current.ObservedAtUtc, cpu, current.PhysicalBytes, current.AvailableBytes, processes,
            current.UnavailableProcesses, current.Truncated);
    }
    public void Reset() => previous = null;
}

public static class WindowsMeasurements
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public static ProcessCounters ReadProcess(Process process)
    {
        process.Refresh();
        var started = process.StartTime.ToUniversalTime().Ticks;
        if (process.HasExited) throw new InvalidOperationException("The process exited.");
        return new(process.Id, started, process.ProcessName, process.TotalProcessorTime.Ticks, process.WorkingSet64);
    }
    public static MachineObservation Capture(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        token.ThrowIfCancellationRequested();
        CpuCounters? cpu = GetSystemTimes(out var idle, out var kernel, out var user) ? new(idle.Value, kernel.Value, user.Value) : null;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool memoryAvailable = GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 && memory.AvailablePhysical <= memory.TotalPhysical;
        var observations = new List<ProcessCounters>(); int unavailable = 0;
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes.Take(5000))
            {
                token.ThrowIfCancellationRequested();
                try { observations.Add(ReadProcess(process)); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                { unavailable++; }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return new(DateTimeOffset.UtcNow, cpu, memoryAvailable ? memory.TotalPhysical : null,
            memoryAvailable ? memory.AvailablePhysical : null, observations.ToArray(), unavailable, processes.Length > 5000, Stopwatch.GetTimestamp());
    }
}
