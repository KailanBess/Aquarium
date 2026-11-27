using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aquarium.Core;

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticState>))]
public enum DiagnosticState { Available, Warning, Unavailable, Unsupported, Unknown }

public sealed record DiagnosticResult(DiagnosticState State, string Code);
public sealed record DiagnosticObservation(string Id, DiagnosticState State, string Code, DateTimeOffset ObservedAtUtc);
public sealed record DiagnosticReport(int SchemaVersion, string ApplicationId, string ApplicationVersion,
    DateTimeOffset GeneratedAtUtc, IReadOnlyList<DiagnosticObservation> Observations);

/// <summary>A read-only observation. Probes must honor cancellation and avoid side effects.</summary>
public interface IDiagnosticProbe
{
    string Id { get; }
    Task<DiagnosticResult> ObserveAsync(CancellationToken token);
}

/// <summary>Collects bounded, attributed observations without raw exceptions or automatic uploads.</summary>
public static class Diagnostics
{
    private static bool Identifier(string? value) => value is not null &&
        Regex.IsMatch(value, @"\A[a-z][a-z0-9._-]{0,63}\z");

    public static async Task<DiagnosticReport> CollectAsync(string applicationId, string applicationVersion,
        IEnumerable<IDiagnosticProbe> probes, CancellationToken token = default, TimeSpan? probeTimeout = null)
    {
        if (!Identifier(applicationId)) throw new ArgumentException("Use a stable application identifier.", nameof(applicationId));
        if (applicationVersion is null || applicationVersion.Length > 32 || !Version.TryParse(applicationVersion, out _))
            throw new ArgumentException("Use a numeric application version.", nameof(applicationVersion));
        ArgumentNullException.ThrowIfNull(probes);
        var timeout = probeTimeout ?? TimeSpan.FromSeconds(2);
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(probeTimeout));
        token.ThrowIfCancellationRequested();
        var selected = probes.Take(65).ToArray();
        if (selected.Length > 64 || selected.Any(p => p is null))
            throw new ArgumentException("Use up to 64 uniquely named diagnostic probes.", nameof(probes));
        // Capture identifiers before execution; providers cannot change report attribution mid-run.
        var work = selected.Select(p => (Probe: p, Id: p.Id)).ToArray();
        if (work.Any(p => !Identifier(p.Id)) || work.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != work.Length)
            throw new ArgumentException("Use up to 64 uniquely named diagnostic probes.", nameof(probes));
        var observations = new List<DiagnosticObservation>();
        foreach (var item in work)
        {
            token.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            DiagnosticResult result;
            try
            {
                // Keep synchronous provider work off the caller's UI thread too.
                var task = Task.Run(() => item.Probe.ObserveAsync(deadline.Token), CancellationToken.None);
                // Observe eventual errors even if an uncooperative provider outlives the timeout.
                _ = task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                result = await task.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (result is null || !Enum.IsDefined(result.State) || !Identifier(result.Code))
                    result = new(DiagnosticState.Unknown, "invalid-result");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            { result = new(DiagnosticState.Unknown, deadline.IsCancellationRequested ? "probe-timeout" : "probe-cancelled"); }
            catch (NotSupportedException) { result = new(DiagnosticState.Unsupported, "unsupported"); }
            catch (UnauthorizedAccessException) { result = new(DiagnosticState.Unavailable, "permission-denied"); }
            catch (IOException) { result = new(DiagnosticState.Unavailable, "observation-unavailable"); }
            catch (Exception) { result = new(DiagnosticState.Unknown, "probe-failed"); }
            token.ThrowIfCancellationRequested();
            observations.Add(new(item.Id, result.State, result.Code, DateTimeOffset.UtcNow));
        }
        return new(1, applicationId, applicationVersion, DateTimeOffset.UtcNow, observations.AsReadOnly());
    }
}
