using Aquarium;
using Aquarium.Core;
using System.Text.Json;

internal static class SupportDiagnosticChecks
{
    internal static async Task Run()
    {
        var empty = await SupportDiagnostics.CollectAsync(true, false, [], "0.6.0");
        if (empty.Observations.Single(o => o.Id == "packages.recorded-hashes").State != DiagnosticState.Unknown)
            throw new Exception("An empty app list claimed verified packages");
        var fixture = new InstalledApp("aquarium.media", "v0.5.0", "C:\\private-profile", DateTimeOffset.UtcNow,
            "FishNet.exe", new string('a', 64), "KailanBess/FishNet");
        var report = await SupportDiagnostics.CollectAsync(true, true, [fixture], "0.6.0");
        if (report.Observations.Single(o => o.Id == "packages.recorded-hashes").Code != "hashes-recorded-not-verified")
            throw new Exception("Recorded hashes were treated as live verification");
        if (report.Observations.Single(o => o.Id == "operations.snapshot").State != DiagnosticState.Warning)
            throw new Exception("Active operation was hidden");
        var failed = await SupportDiagnostics.CollectAsync(false, false, [fixture], "0.6.0");
        if (failed.Observations.Single(o => o.Id == "registry.loaded").State != DiagnosticState.Unavailable ||
            failed.Observations.Single(o => o.Id == "packages.recorded-hashes").State != DiagnosticState.Unknown)
            throw new Exception("Unreadable registry produced healthy app observations");
        if (JsonSerializer.Serialize(report).Contains("private-profile")) throw new Exception("Private path leaked");
        if (SupportDiagnostics.SafeVersion("preview-v0.17.0") != "0.17.0" || SupportDiagnostics.SafeVersion("v0.5.0") != "0.5.0" ||
            SupportDiagnostics.SafeVersion("C:\\private-profile") != "unrecognized") throw new Exception("Version redaction failed");
        Console.WriteLine("Support diagnostics: empty/unavailable observations, metadata boundaries and version redaction passed.");
    }
}
