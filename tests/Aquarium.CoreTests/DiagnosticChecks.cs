using Aquarium.Core;
using System.Text.Json;

internal static class DiagnosticChecks
{
    private sealed class Probe(string id, Func<CancellationToken, Task<DiagnosticResult>> run) : IDiagnosticProbe
    { public string Id => id; public Task<DiagnosticResult> ObserveAsync(CancellationToken token) => run(token); }
    internal static async Task Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        async Task Refuses<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
        IDiagnosticProbe Result(string id, DiagnosticState state, string code) => new Probe(id, _ => Task.FromResult(new DiagnosticResult(state, code)));
        var report = await Diagnostics.CollectAsync("fixture", "1.2.3", [Result("first", DiagnosticState.Available, "observed"),
            Result("second", DiagnosticState.Unsupported, "not-supported")]);
        Check(report.SchemaVersion == 1 && report.ApplicationId == "fixture" && report.ApplicationVersion == "1.2.3", "Report identity");
        Check(report.Observations.Select(o => o.Id).SequenceEqual(new[] { "first", "second" }), "Observation attribution/order");
        Check(report.Observations[1].State == DiagnosticState.Unsupported, "Unsupported capability reported as available");
        Check(report.Observations.All(o => o.ObservedAtUtc.Offset == TimeSpan.Zero && o.ObservedAtUtc <= report.GeneratedAtUtc), "Observation timestamps");
        var empty = await Diagnostics.CollectAsync("fixture", "1.2.3", []);
        Check(empty.Observations.Count == 0, "Missing probes fabricated results");
        foreach (var failure in new (Exception Error, DiagnosticState State, string Code)[]
        {
            (new PlatformNotSupportedException("secret-path"), DiagnosticState.Unsupported, "unsupported"),
            (new UnauthorizedAccessException("secret-path"), DiagnosticState.Unavailable, "permission-denied"),
            (new IOException("secret-path"), DiagnosticState.Unavailable, "observation-unavailable"),
            (new Exception("secret-path"), DiagnosticState.Unknown, "probe-failed"),
            (new OperationCanceledException("secret-path"), DiagnosticState.Unknown, "probe-cancelled")
        })
        {
            var observed = await Diagnostics.CollectAsync("fixture", "1.2.3", [new Probe("failure", _ => throw failure.Error), Result("next", DiagnosticState.Available, "observed")]);
            Check(observed.Observations[0].State == failure.State && observed.Observations[0].Code == failure.Code, "Incorrect failure classification");
            Check(observed.Observations[1].State == DiagnosticState.Available, "One failed probe stopped later checks");
            Check(!JsonSerializer.Serialize(observed).Contains("secret-path", StringComparison.Ordinal), "Raw exception leaked into report");
        }
        foreach (var invalid in new[] { Result("bad", (DiagnosticState)999, "observed"), Result("bad", DiagnosticState.Available, "C:\\private\\file"), new Probe("bad", _ => Task.FromResult<DiagnosticResult>(null!)) })
        {
            var result = await Diagnostics.CollectAsync("fixture", "1.2.3", [invalid]);
            Check(result.Observations.Single().State == DiagnosticState.Unknown && result.Observations[0].Code == "invalid-result", "Invalid result was trusted");
        }
        var pending = new TaskCompletionSource<DiagnosticResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOut = await Diagnostics.CollectAsync("fixture", "1.2.3", [new Probe("slow", _ => pending.Task)], probeTimeout: TimeSpan.FromMilliseconds(50));
        Check(timedOut.Observations.Single().Code == "probe-timeout", "Uncooperative probe blocked the report");
        pending.SetException(new IOException("secret-path"));
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var collection = Diagnostics.CollectAsync("fixture", "1.2.3", [new Probe("cancel", async token =>
        { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(DiagnosticState.Available, "unexpected"); })], cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await Refuses<OperationCanceledException>(() => collection);
        int calls = 0;
        var duplicate = new Probe("same", _ => { calls++; return Task.FromResult(new DiagnosticResult(DiagnosticState.Available, "observed")); });
        await Refuses<ArgumentException>(() => Diagnostics.CollectAsync("fixture", "1.2.3", [duplicate, duplicate]));
        Check(calls == 0, "Invalid registration executed probes");
        await Refuses<ArgumentException>(() => Diagnostics.CollectAsync("fixture", "1.2.3", Enumerable.Range(0, 65).Select(i => Result("p" + i, DiagnosticState.Available, "observed"))));
        await Refuses<ArgumentException>(() => Diagnostics.CollectAsync("C:\\private", "1.2.3", []));
        await Refuses<ArgumentException>(() => Diagnostics.CollectAsync("fixture", "secret-version", []));
        await Refuses<ArgumentException>(() => Diagnostics.CollectAsync("fixture", "1.2.3", [Result("bad/id", DiagnosticState.Available, "observed")]));
        await Refuses<ArgumentOutOfRangeException>(() => Diagnostics.CollectAsync("fixture", "1.2.3", [], probeTimeout: TimeSpan.Zero));
        await Refuses<NotSupportedException>(() => { ((IList<DiagnosticObservation>)report.Observations).Add(report.Observations[0]); return Task.CompletedTask; });
        Check(JsonSerializer.Serialize(report).Contains("\"Unsupported\"", StringComparison.Ordinal), "States serialized without their stable names");
        Console.WriteLine($"Diagnostics SDK: {checks} checks passed.");
    }
}
