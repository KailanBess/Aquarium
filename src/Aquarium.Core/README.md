# Aquarium.Core

Portable .NET 10 application infrastructure. The package has no WinUI, running-hub, online-account or network-service dependency. Applications own their schemas and native platform adapters.

- `AtomicFile.WriteAsync` and `WriteJsonAsync` publish complete files through same-directory renames. Hold the appropriate application/record writer lease; this is not a filesystem sandbox or a universal power-loss guarantee.
- `ProfileLock.Acquire(directory)` returns an exclusive process-lifetime profile lease. Dispose it on normal shutdown.
- `PathSafety.RequireWithin` checks platform-aware lexical boundaries; `RefuseLinks` rejects existing linked paths. These checks do not prevent a malicious process racing filesystem changes.
- `JsonSettingsStore<T>.Load`, `SaveAsync` and `RestorePreviousAsync` support explicit schema migrations, unknown-field preservation, prior-record backups and stale-revision rejection. Keep the returned snapshot after each successful save. Unsupported/corrupt records are refused, not overwritten with defaults.
- `Diagnostics.CollectAsync(applicationId, numericVersion, probes, token)` creates schema-versioned reports using read-only `IDiagnosticProbe` implementations. IDs and result codes must be stable lowercase labels without private values. Each result is attributed and timestamped; missing/error/unsupported results never become an overall protection claim. Exceptions are represented by fixed codes, not exception text.

Diagnostics permits up to 64 uniquely named probes and a configurable per-probe wait of 10 milliseconds to 30 seconds (default 2 seconds). Probes run off the caller's UI thread and must honor cancellation. Timing out does not forcibly stop uncooperative code. Completion timestamps do not authenticate providers or certify input freshness. Nothing is uploaded automatically. Native collectors and structured logging are separate components.

Run `dotnet run --project tests/Aquarium.CoreTests -c Release` from the repository root. CI covers Windows and Linux; platform-independent tests do not certify real device or service compatibility.
