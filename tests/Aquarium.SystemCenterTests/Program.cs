using Aquarium.Core;
using Aquarium.SystemCenter;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class Checks
{
    private sealed class Fixture : ISystemObservationProvider
    {
        internal MemoryObservation Memory = new(DiagnosticState.Available, "generated-memory", 1024, 512);
        internal ServiceInventory Services = new(DiagnosticState.Available, "generated-services", false, [new("GeneratedService", "Generated service", "Stopped")]);
        internal Exception? MemoryError, ServiceError;
        public MemoryObservation ReadMemory(CancellationToken token) { token.ThrowIfCancellationRequested(); if (MemoryError is not null) throw MemoryError; return Memory; }
        public ServiceInventory ReadServices(CancellationToken token) { token.ThrowIfCancellationRequested(); if (ServiceError is not null) throw ServiceError; return Services; }
    }
    private static async Task Main()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        var fixture = new Fixture(); var report = SystemObservations.Capture(fixture);
        Check(report.SchemaVersion == 1 && report.VisibleLogicalProcessors > 0 && report.RecordedAtUtc.Offset == TimeSpan.Zero, "Report identity/timing invalid");
        Check(report.Memory.AvailableBytes == 512 && report.Services.Services[0].State == "Stopped", "Stopped service or actual memory was rewritten");
        foreach (var error in new Exception[] { new Win32Exception(5, "Private exception details"), new UnauthorizedAccessException("Private details"), new IOException("Private path"), new PlatformNotSupportedException("Private OS") })
        {
            fixture.MemoryError = fixture.ServiceError = error; var missing = SystemObservations.Capture(fixture);
            Check(missing.Memory.TotalBytes is null && missing.Services.Services.Length == 0 && missing.Services.State == (error is NotSupportedException ? DiagnosticState.Unsupported : DiagnosticState.Unavailable), "Unavailable provider inferred measurements");
            Check(!System.Text.Json.JsonSerializer.Serialize(missing).Contains("Private"), "Raw provider exception escaped into report");
        }
        fixture.MemoryError = fixture.ServiceError = null;
        fixture.MemoryError = fixture.ServiceError = new InvalidDataException("Private malformed native data");
        var invalid = SystemObservations.Capture(fixture);
        Check(invalid.Memory.State == DiagnosticState.Unknown && invalid.Services.State == DiagnosticState.Unknown && invalid.Services.Code == "invalid-observation", "Malformed native data was presented as available");
        fixture.MemoryError = fixture.ServiceError = null;
        foreach (var bad in new[] { fixture.Memory with { AvailableBytes = 1025 }, fixture.Memory with { TotalBytes = 0 }, fixture.Memory with { State = DiagnosticState.Unsupported }, fixture.Memory with { Code = "private text with spaces" } })
        { fixture.Memory = bad; Check(SystemObservations.Capture(fixture).Memory.State == DiagnosticState.Unknown, "Invalid memory provider was accepted"); }
        fixture.Memory = new(DiagnosticState.Available, "generated", 100, 20);
        foreach (var bad in new[] { fixture.Services with { Truncated = true }, fixture.Services with { State = DiagnosticState.Unavailable }, fixture.Services with { Services = [new("Same", "A", "Running"), new("same", "B", "Stopped")] }, fixture.Services with { Services = [new("Name", "Name", "Healthy")] } })
        { fixture.Services = bad; Check(SystemObservations.Capture(fixture).Services.State == DiagnosticState.Unknown, "Invalid service provider was accepted"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { SystemObservations.Capture(fixture, canceled.Token); throw new Exception("Canceled observation ran"); } catch (OperationCanceledException) { checks++; }
        int stride = Marshal.SizeOf<WindowsSystemObservationProvider.NativeService>();
        var buffer = Marshal.AllocHGlobal(8192);
        try
        {
            for (int i = 0; i < 8; i++)
            {
                int text = stride * 8 + i * 600;
                byte[] name = Encoding.Unicode.GetBytes($"Fixture{i}\0"), display = Encoding.Unicode.GetBytes($"Display {i}\0");
                Marshal.Copy(name, 0, buffer + text, name.Length); Marshal.Copy(display, 0, buffer + text + 256, display.Length);
                Marshal.StructureToPtr(new WindowsSystemObservationProvider.NativeService { Name = buffer + text, DisplayName = buffer + text + 256, State = (uint)i + 1, ProcessId = 98765 }, buffer + i * stride, false);
            }
            var parsed = WindowsSystemObservationProvider.Decode(buffer, 8192, 8, false);
            Check(parsed.Services.Select(s => s.State).SequenceEqual(new[] { "Stopped", "Start pending", "Stop pending", "Running", "Continue pending", "Pause pending", "Paused", "Unknown" }), "Native service states decoded incorrectly");
            Check(!System.Text.Json.JsonSerializer.Serialize(parsed).Contains("98765"), "Native process ID was retained");
            var partial = WindowsSystemObservationProvider.Decode(buffer, 8192, 8, true);
            Check(partial.Truncated && partial.State == DiagnosticState.Warning && partial.Services.Length == 8, "Partial enumeration was hidden");
            foreach (var malformed in new[] { new WindowsSystemObservationProvider.NativeService { Name = buffer - 2, DisplayName = buffer + 1000 }, new() { Name = buffer + 1, DisplayName = buffer + 1000 } })
            {
                Marshal.StructureToPtr(malformed, buffer, false);
                try { WindowsSystemObservationProvider.Decode(buffer, 8192, 8, false); throw new Exception("Invalid native pointer accepted"); } catch (InvalidDataException) { checks++; }
            }
            try { WindowsSystemObservationProvider.Decode(buffer, 8192, 1000, false); throw new Exception("Invalid native count accepted"); } catch (InvalidDataException) { checks++; }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        var large = Marshal.AllocHGlobal(256 * 1024);
        try
        {
            for (int i = 0; i < 2049; i++)
            {
                int offset = stride * 2049 + i * 32;
                byte[] name = Encoding.Unicode.GetBytes($"Service{i:D4}\0"); Marshal.Copy(name, 0, large + offset, name.Length);
                Marshal.WriteInt16(large + offset + 28, 0);
                Marshal.StructureToPtr(new WindowsSystemObservationProvider.NativeService { Name = large + offset, DisplayName = large + offset + 28, State = 4 }, large + i * stride, false);
            }
            var limited = WindowsSystemObservationProvider.Decode(large, 256 * 1024, 2049, false);
            Check(limited.Services.Length == 2048 && limited.Truncated && limited.State == DiagnosticState.Warning, "Native entry cap or partial-coverage state failed");
        }
        finally { Marshal.FreeHGlobal(large); }
        var live = SystemObservations.Capture(new WindowsSystemObservationProvider());
        if (OperatingSystem.IsWindows())
        {
            Check(live.Memory.State == DiagnosticState.Available && live.Memory.TotalBytes > 0 && live.Memory.AvailableBytes <= live.Memory.TotalBytes, "Actual Windows physical memory observation failed");
            Console.WriteLine($"Native service adapter result: {live.Services.State} / {live.Services.Code}; service names are not printed.");
            Check(live.Services.State is DiagnosticState.Available or DiagnosticState.Warning && live.Services.Services.Length > 0 ||
                live.Services.State == DiagnosticState.Unavailable && live.Services.Code == "permission-denied" && live.Services.Services.Length == 0, "Actual service observation did not preserve available/permission-denied semantics");
        }
        else Check(live.Memory.State == DiagnosticState.Unsupported && live.Services.State == DiagnosticState.Unsupported, "Non-Windows measurements were fabricated");
        var json = System.Text.Json.JsonSerializer.Serialize(report);
        Check(System.Text.Json.JsonSerializer.Deserialize<SystemCenterReport>(json)!.Services.Services[0].Name == "GeneratedService", "Report JSON round trip failed");
        var root = Path.Combine(Path.GetTempPath(), "Aquarium-center-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var application = Path.Combine(root, "program"); Directory.CreateDirectory(application);
            var path = Path.Combine(root, "report.json"); await ReportExport.SaveAsync(path, application, report);
            Check(System.Text.Json.JsonSerializer.Deserialize<SystemCenterReport>(File.ReadAllText(path))!.Services.Services[0].Name == "GeneratedService", "Actual report file lost observations");
            var prior = File.ReadAllBytes(path);
            try { await ReportExport.SaveAsync(path, application, report, canceled.Token); throw new Exception("Canceled export replaced file"); } catch (OperationCanceledException) { checks++; }
            Check(File.ReadAllBytes(path).SequenceEqual(prior), "Canceled export changed prior destination");
            foreach (var bad in new[] { Path.Combine(application, "report.json"), Path.Combine(root, "prior.txt") })
            {
                File.WriteAllText(bad, "Preserved fixture");
                try { await ReportExport.SaveAsync(bad, application, report); throw new Exception("Unsafe export destination accepted"); } catch (InvalidDataException) { checks++; }
                Check(File.ReadAllText(bad) == "Preserved fixture", "Rejected export changed destination");
            }
        }
        finally
        {
            var full = Path.GetFullPath(root); var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "\\AAquarium-center-export-[a-f0-9]{32}\\z")) throw new IOException("Unsafe fixture cleanup path.");
            Directory.Delete(full, true);
        }
        Console.WriteLine($"System Center: {checks} snapshot/provider/native-buffer checks passed. No live service inventory exported.");
    }
}
