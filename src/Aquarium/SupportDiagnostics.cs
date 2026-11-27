using Aquarium.Core;

namespace Aquarium;

internal static class SupportDiagnostics
{
    private sealed class Probe(string id, DiagnosticResult result) : IDiagnosticProbe
    {
        public string Id => id;
        public Task<DiagnosticResult> ObserveAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(result); }
    }

    internal static string SafeVersion(string? text)
    {
        var value = text?.StartsWith("preview-v", StringComparison.Ordinal) == true ? text[9..] :
            text?.StartsWith('v') == true ? text[1..] : text;
        return value is not null && value.Length <= 32 && Version.TryParse(value, out var version) ? version.ToString() : "unrecognized";
    }

    internal static Task<DiagnosticReport> CollectAsync(bool registryReadable, bool operationActive,
        IReadOnlyList<InstalledApp> installations, string applicationVersion, CancellationToken token = default)
    {
        var hashesRecorded = installations.All(app => GitHubPackages.IsHash(app.PackageSha256));
        IDiagnosticProbe[] probes =
        [
            new Probe("registry.loaded", new(registryReadable ? DiagnosticState.Available : DiagnosticState.Unavailable,
                registryReadable ? "registry-readable" : "registry-read-only")),
            new Probe("operations.snapshot", new(operationActive ? DiagnosticState.Warning : DiagnosticState.Available,
                operationActive ? "operation-active" : "no-package-operation")),
            new Probe("packages.recorded-hashes", new(!registryReadable || installations.Count == 0 ? DiagnosticState.Unknown :
                hashesRecorded ? DiagnosticState.Available : DiagnosticState.Warning,
                !registryReadable ? "registry-unavailable" : installations.Count == 0 ? "no-installed-records" :
                hashesRecorded ? "hashes-recorded-not-verified" : "missing-recorded-hashes"))
        ];
        return Diagnostics.CollectAsync("aquarium", applicationVersion, probes, token);
    }
}
