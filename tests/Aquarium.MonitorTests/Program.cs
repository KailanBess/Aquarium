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
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { WindowsMeasurements.Capture(canceled.Token); throw new Exception("Canceled sampling proceeded"); } catch (OperationCanceledException) { checks++; }
}
Console.WriteLine($"System Monitor: {checks} measurement checks passed. No process names or system snapshots were exported.");
