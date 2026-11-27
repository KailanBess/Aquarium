namespace Aquarium.SystemMonitor;

public sealed record MonitorHistoryEntry(long Sequence, DateTimeOffset RecordedAtUtc, string State,
    double? CpuPercent, ulong? PhysicalBytes, ulong? AvailableBytes, int? ReadableProcesses,
    int? UnavailableProcesses, bool? ProcessListTruncated);
public sealed record MonitorHistoryReport(int SchemaVersion, int Capacity, long DroppedEntries,
    MonitorHistoryEntry[] Entries,
    string Coverage = "In-memory entries while this window is open; at most 900 entries. No process names or IDs. CPU covers the Windows primary processor group. Pause and failed collections create explicit markers. No interpolation or automatic disk persistence. Sequence is collection order; wall-clock timestamps can change.");

public sealed class MonitorSessionHistory
{
    private readonly Queue<MonitorHistoryEntry> entries = new();
    private readonly int capacity;
    private long sequence, dropped;
    public MonitorSessionHistory(int capacity = 900)
    {
        if (capacity is < 1 or > 900) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }
    public void Observe(MonitorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.CpuPercent is { } cpu && (!double.IsFinite(cpu) || cpu < 0 || cpu > 100) ||
            report.AvailableBytes > report.PhysicalBytes || report.UnavailableProcesses < 0 || report.Processes.Length > 5000)
            throw new ArgumentException("Invalid history sample.", nameof(report));
        Add(new(0, report.ObservedAtUtc, "Sample", report.CpuPercent, report.PhysicalBytes, report.AvailableBytes,
            report.Processes.Length, report.UnavailableProcesses, report.Truncated));
    }
    public void Mark(string state, DateTimeOffset recordedAtUtc)
    {
        if (state is not ("Paused" or "Resumed" or "Unavailable")) throw new ArgumentException("Unknown history marker.", nameof(state));
        Add(new(0, recordedAtUtc, state, null, null, null, null, null, null));
    }
    private void Add(MonitorHistoryEntry entry)
    {
        if (entries.Count == capacity) { entries.Dequeue(); dropped++; }
        entries.Enqueue(entry with { Sequence = ++sequence, RecordedAtUtc = entry.RecordedAtUtc.ToUniversalTime() });
    }
    public MonitorHistoryReport Snapshot() => new(1, capacity, dropped, entries.ToArray());
}
