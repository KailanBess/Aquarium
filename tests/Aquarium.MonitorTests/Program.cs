using Aquarium.SystemMonitor;
using System.Diagnostics;

int checks = 0;
void Check(bool valid, string label) { if (!valid) throw new Exception(label); checks++; }
var history = new MeasurementHistory();
MachineObservation Observation(long time, CpuCounters? cpu, params ProcessCounters[] processes) => new(DateTimeOffset.UtcNow,
    cpu, 1000, 400, processes, 0, false, time);
ProcessCounters FixtureProcess(long start, long cpu) => new(42, start, "Generated fixture", cpu, 128);
MonitorReport Sample(MachineObservation observation) => history.Observe(observation, 1000, 2);
var first = Sample(Observation(1000, new(100, 200, 100), FixtureProcess(1, 0)));
Check(first.CpuPercent is null && first.Processes[0].CpuPercent is null, "First sample invented CPU usage");
var next = Sample(Observation(2000, new(150, 300, 200), FixtureProcess(1, TimeSpan.TicksPerSecond)));
Check(next.CpuPercent == 75 && next.Processes[0].CpuPercent == 50, "CPU deltas/normalization incorrect");
Check(next.PhysicalBytes == 1000 && next.AvailableBytes == 400 && next.Processes[0].WorkingSetBytes == 128, "Memory counters changed");
Check(Sample(Observation(3000, new(200, 400, 300), FixtureProcess(2, TimeSpan.TicksPerSecond * 2))).Processes[0].CpuPercent is null, "Reused PID inherited another process rate");
Check(Sample(Observation(4000, null, FixtureProcess(2, 0))).CpuPercent is null, "Missing system counter reported zero");
Check(Sample(Observation(5000, new(1, 2, 3), FixtureProcess(2, 0))).CpuPercent is null, "Missing prior counters invented a system rate");
Check(Sample(Observation(6000, new(0, 0, 0), FixtureProcess(2, 0))).CpuPercent is null, "Reset counters produced a CPU rate");
Check(Sample(Observation(7000, new(100, 10, 10))).CpuPercent is null, "Impossible idle time produced a rate");
history.Reset();
Check(Sample(Observation(8000, new(200, 300, 300), FixtureProcess(2, 0))).Processes[0].CpuPercent is null, "Resume inherited a pre-pause rate");
foreach (var invalid in new[] { Observation(9000, null, FixtureProcess(2, -1)), Observation(9000, null, FixtureProcess(2, 0), FixtureProcess(3, 0)),
    Observation(9000, null) with { AvailableBytes = 1001 }, Observation(9000, null) with { UnavailableProcesses = -1 } })
{
    try { Sample(invalid); throw new Exception("Invalid sample accepted"); } catch (ArgumentException) { checks++; }
}
if (OperatingSystem.IsWindows())
{
    using var own = System.Diagnostics.Process.GetCurrentProcess();
    var observed = WindowsMeasurements.ReadProcess(own);
    Check(observed.Pid == own.Id && observed.StartedUtcTicks == own.StartTime.ToUniversalTime().Ticks && observed.WorkingSetBytes > 0,
        "Own fixture process identity/memory measurement failed");
    var live = WindowsMeasurements.Capture(default);
    Check(live.PhysicalBytes is > 0 && live.AvailableBytes <= live.PhysicalBytes, "Native physical memory observation unavailable or invalid");
    Check(live.Cpu is not null && live.Cpu.Idle <= live.Cpu.Kernel, "Native system timing observation unavailable or invalid");
    Check(live.Truncated || live.Processes.Any(p => p.Pid == own.Id && p.StartedUtcTicks == observed.StartedUtcTicks),
        "Native process inventory omitted its own readable fixture identity");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { WindowsMeasurements.Capture(canceled.Token); throw new Exception("Canceled sampling proceeded"); } catch (OperationCanceledException) { checks++; }
}
var session = new MonitorSessionHistory(3);
var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
var point = next with { ObservedAtUtc = at };
session.Observe(point);
var captured = session.Snapshot();
Check(captured.Entries.Single().RecordedAtUtc.Offset == TimeSpan.Zero && captured.Entries[0].RecordedAtUtc == at, "History changed the instant or retained non-UTC timestamps");
Check(captured.Entries[0].CpuPercent == 75 && captured.Entries[0].ReadableProcesses == 1, "History changed observed summaries");
session.Mark("Paused", at.AddSeconds(1)); session.Mark("Resumed", at.AddSeconds(2));
session.Mark("Unavailable", at.AddSeconds(-20));
var bounded = session.Snapshot();
Check(bounded.Entries.Length == 3 && bounded.DroppedEntries == 1 && bounded.Entries.Select(e => e.Sequence).SequenceEqual(new long[] { 2, 3, 4 }), "History retention/order/drop counts incorrect");
Check(bounded.Entries.All(e => e.CpuPercent is null && e.PhysicalBytes is null && e.ReadableProcesses is null), "History markers invented measurements");
Check(bounded.Entries[^1].RecordedAtUtc < bounded.Entries[0].RecordedAtUtc, "History reordered entries by a changed wall clock");
Check(captured.Entries.Length == 1 && captured.DroppedEntries == 0, "A captured export changed as new samples arrived");
bounded.Entries[0] = captured.Entries[0];
Check(session.Snapshot().Entries[0].State == "Paused", "Export array mutated retained history");
var serialized = System.Text.Json.JsonSerializer.Serialize(captured);
Check(!serialized.Contains("Generated fixture") && !serialized.Contains("\"Pid\"") && !serialized.Contains("WorkingSetBytes"), "History retained process identities or process-level records");
var decoded = System.Text.Json.JsonSerializer.Deserialize<MonitorHistoryReport>(serialized)!;
Check(decoded.Entries[0] == captured.Entries[0] && decoded.SchemaVersion == 1, "History JSON round trip lost unknown/observation data");
session.Observe(point with { CpuPercent = null, PhysicalBytes = null, AvailableBytes = null });
Check(session.Snapshot().Entries[^1].CpuPercent is null && session.Snapshot().Entries[^1].PhysicalBytes is null, "Missing history readings became zero");
foreach (int capacity in new[] { 0, 901 }) { try { _ = new MonitorSessionHistory(capacity); throw new Exception("Unbounded history capacity accepted"); } catch (ArgumentOutOfRangeException) { checks++; } }
try { session.Mark("Healthy", at); throw new Exception("Unknown history marker accepted"); } catch (ArgumentException) { checks++; }
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 101 }) { try { session.Observe(point with { CpuPercent = invalid }); throw new Exception("Invalid history measurement accepted"); } catch (ArgumentException) { checks++; } }
var fullSession = new MonitorSessionHistory();
for (int i = 0; i < 1000; i++) fullSession.Observe(point);
Check(fullSession.Snapshot().Entries.Length == 900 && fullSession.Snapshot().DroppedEntries == 100 && fullSession.Snapshot().Entries[0].Sequence == 101, "Default retention failed at full capacity");
Console.WriteLine($"System Monitor: {checks} measurement/history checks passed. No live process names or system snapshots were exported.");
