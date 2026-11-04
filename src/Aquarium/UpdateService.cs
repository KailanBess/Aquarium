using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aquarium;

public sealed record UpdateReport(string Output, int ExitCode, DateTimeOffset CheckedAt);
public sealed record ReleaseInfo(string Name, string Tag, string Url);

public sealed class UpdateService
{
    private static string WingetPath
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("WinGet is unavailable. Install or repair Microsoft App Installer, then try again.");
            return path;
        }
    }
    public async Task<UpdateReport> CheckOutsideAppsAsync(CancellationToken token)
    {
        var start = new ProcessStartInfo(WingetPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // No install switches, no automatic acceptance of source agreements, no --all.
        foreach (var argument in new[] { "upgrade", "--source", "winget", "--disable-interactivity" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WinGet did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        return new UpdateReport((await output) + "\n" + (await error), process.ExitCode, DateTimeOffset.Now);
    }
    public void StartOutsideUpdate(string packageId)
    {
        if (!Regex.IsMatch(packageId, @"\A[A-Za-z0-9][A-Za-z0-9._+-]{0,199}\z"))
            throw new ArgumentException("Enter the exact package ID from the WinGet results, for example Spotify.Spotify.");
        // Visible interactive installer: the user can review agreements and elevation.
        var start = new ProcessStartInfo(WingetPath) { UseShellExecute = true };
        foreach (var argument in new[] { "upgrade", "--id", packageId, "--exact", "--source", "winget" }) start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
    }
    public async Task<IReadOnlyList<ReleaseInfo>> CheckAquariumReleasesAsync(CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aquarium/0.2");
        var releases = new List<ReleaseInfo>();
        foreach (var app in new[] { "Aquarium", "FishNet" })
        {
            using var response = await client.GetAsync($"https://api.github.com/repos/KailanBess/{app}/releases/latest", token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                releases.Add(new(app, "No stable release published", $"https://github.com/KailanBess/{app}/releases"));
                continue;
            }
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            releases.Add(new(app, json.RootElement.GetProperty("tag_name").GetString() ?? "Unknown tag", $"https://github.com/KailanBess/{app}/releases"));
        }
        return releases;
    }
}
