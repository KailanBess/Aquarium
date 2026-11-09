using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aquarium;

public enum ReleaseChannel { Default, Stable, Preview }

public sealed record AppSource(string AppId, string Repository, string Executable, bool IncludePreview, bool IncludedByDefault);
public sealed record AppPackage(AppSource Source, string Version, string AssetName, string DownloadUrl, string Sha256, long Size);
public sealed record BundledPackage(string AppId, string Version, string File, string Sha256);

public sealed class GitHubPackages
{
    internal static readonly AppSource Shell = new("aquarium.shell", "KailanBess/Aquarium", "Aquarium.exe", false, false);
    public static readonly AppSource[] Sources =
    [
        new("aquarium.security", "KailanBess/FishTank", "FishTank.exe", true, true),
        new("aquarium.media", "KailanBess/FishNet", "FishNet.exe", false, true),
        new("aquarium.emulation", "KailanBess/FishBowl", "FishBowl.exe", false, false)
    ];
    internal static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aquarium/0.4");
        return client;
    }
    public async Task<AppPackage> FindAsync(AppSource source, CancellationToken token, ReleaseChannel channel = ReleaseChannel.Default)
    {
        if (source != Shell && !Sources.Contains(source)) throw new InvalidOperationException("This repository is not in the Aquarium catalog.");
        using var client = Client();
        using var response = await client.GetAsync($"https://api.github.com/repos/{source.Repository}/releases?per_page=20", token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var release = SelectRelease(json.RootElement, source.IncludePreview, channel);
        if (release.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException(channel == ReleaseChannel.Default ? "No published release is available yet." : $"No published {channel.ToString().ToLowerInvariant()} release is available yet.");
        var version = release.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Missing release tag.");
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var wanted = source.AppId switch
        {
            "aquarium.shell" => "Aquarium-complete-windows-x64.zip",
            "aquarium.security" => "FishTank-desktop-preview.zip",
            "aquarium.emulation" => $"FishBowl-{version.TrimStart('v')}.zip",
            _ => "FishNet-windows-x64.zip"
        };
        var asset = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == wanted);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException($"This release has no supported Windows package ({wanted}).");
        var download = asset.GetProperty("browser_download_url").GetString()!;
        ValidateAssetUrl(source, download);
        string? hash = null;
        if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String && digest.GetString()!.StartsWith("sha256:")) hash = digest.GetString()![7..];
        if (!IsHash(hash))
        {
            var checksums = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
            if (checksums.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The release has no SHA-256 package checksum. Installation is unavailable.");
            var checksumUrl = checksums.GetProperty("browser_download_url").GetString()!;
            ValidateAssetUrl(source, checksumUrl);
            var manifest = await client.GetStringAsync(checksumUrl, token);
            foreach (var line in manifest.Split('\n'))
            {
                var match = Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$");
                if (match.Success && match.Groups[2].Value == wanted) hash = match.Groups[1].Value;
            }
        }
        if (!IsHash(hash)) throw new InvalidDataException("No valid checksum is published for the Windows package.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 512L * 1024 * 1024) throw new InvalidDataException("Unsupported package size.");
        return new(source, version, wanted, download, hash!.ToLowerInvariant(), size);
    }
    internal static JsonElement SelectRelease(JsonElement releases, bool includePreviewByDefault, ReleaseChannel channel)
    {
        return releases.EnumerateArray().FirstOrDefault(release =>
        {
            if (release.GetProperty("draft").GetBoolean()) return false;
            var preview = release.GetProperty("prerelease").GetBoolean();
            return channel switch
            {
                ReleaseChannel.Stable => !preview,
                ReleaseChannel.Preview => preview,
                _ => includePreviewByDefault || !preview
            };
        });
    }

    internal static bool IsHash(string? hash) => hash is not null && Regex.IsMatch(hash, @"\A[a-fA-F0-9]{64}\z");
    internal static void ValidateAssetUrl(AppSource source, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{source.Repository}/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Package URL is outside the approved public repository.");
    }
}
