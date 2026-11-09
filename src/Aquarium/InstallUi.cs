using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private readonly GitHubPackages feeds = new();
    private PackageInstaller installer = null!;
    private readonly Dictionary<string, AppPackage> available = new();
    private readonly Dictionary<string, string> availability = new();
    private CancellationTokenSource? installCancellation;
    private string downloadState = "No downloads or installations have run in this session.";
    private readonly List<string> downloadHistory = [];
    private TextBlock? downloadLabel;

    private void RenderInstallApps()
    {
        Empty("Choose what belongs in your Aquarium", "FishTank and FishNet are included in the complete Aquarium bundle. FishBowl is optional. Additional installs come only from the public releases of these approved GitHub repositories.");
        var check = new Button { Content = "Look for published apps", IsEnabled = installCancellation is null };
        check.Click += async (_, _) => await CheckPackagesAsync();
        PageBody.Children.Add(check);
        foreach (var app in store.Catalog)
        {
            var source = GitHubPackages.Sources.Single(s => s.AppId == app.Id);
            var installed = store.Registry.Installations.FirstOrDefault(i => i.AppId == app.Id);
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Text(app.Name, 24));
            panel.Children.Add(Text(app.Summary, 14, true));
            panel.Children.Add(new HyperlinkButton { Content = source.Repository + " · Public releases", NavigateUri = new Uri($"https://github.com/{source.Repository}/releases") });
            panel.Children.Add(Text(installed is null ? source.IncludedByDefault ? "Included in the complete bundle; not installed in this copy yet." : "Optional · Not installed" : $"Installed · {installed.Version}", 13, true));
            panel.Children.Add(ChannelPicker(app, source));
            panel.Children.Add(Text(availability.GetValueOrDefault(app.Id, "Published packages have not been checked in this session."), 13, true));
            if (available.TryGetValue(app.Id, out var package))
            {
                var same = installed?.Version == package.Version && installed.PackageSha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase);
                var button = new Button { Content = same ? "Installed" : installed is null ? $"Install {package.Version}" : $"Install published {package.Version}", IsEnabled = !same && store.CanSave && installCancellation is null };
                button.Click += async (_, _) => await InstallPublishedAsync(app, package);
                panel.Children.Add(button);
            }
            if (installed is null && source.IncludedByDefault && File.Exists(Path.Combine(AppContext.BaseDirectory, "Bundled", "bundled.json")))
            {
                var bundledInstall = new Button { Content = "Install included version", IsEnabled = store.CanSave && installCancellation is null };
                bundledInstall.Click += async (_, _) => await InstallBundledAsync(app.Id);
                panel.Children.Add(bundledInstall);
            }
            if (installed is not null)
            {
                var launch = new Button { Content = "Open " + app.Name };
                launch.Click += (_, _) => LaunchInstalled(installed);
                panel.Children.Add(launch);
                panel.Children.Add(UninstallButton(app, installed));
                panel.Children.Add(PackageCheckButton(app, installed));
                AddRollbackControls(panel, app);
            }
            PageBody.Children.Add(Panel(panel));
        }
        PageBody.Children.Add(Text("Only apps from the approved Aquarium repositories are listed. FishBowl does not include games, firmware, or emulators.", 13, true));
    }
    private ComboBox ChannelPicker(CatalogApp app, AppSource source)
    {
        var channels = new[] { ReleaseChannel.Default, ReleaseChannel.Stable, ReleaseChannel.Preview };
        var picker = new ComboBox
        {
            Header = "Release channel",
            ItemsSource = new[] { source.IncludePreview ? "Default · includes previews" : "Default · stable", "Stable", "Preview only" },
            SelectedIndex = Array.IndexOf(channels, store.GetReleaseChannel(app.Id)),
            IsEnabled = store.CanSave && installCancellation is null
        };
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedIndex < 0 || installCancellation is not null) return;
            var channel = channels[picker.SelectedIndex];
            using var operation = new CancellationTokenSource();
            installCancellation = operation;
            try
            {
                await store.SetReleaseChannelAsync(app.Id, channel);
                available.Remove(app.Id);
                availability[app.Id] = "Channel saved. Look for published apps to refresh.";
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Could not save the release channel: " + ex.Message, InfoBarSeverity.Error); }
            finally { installCancellation = null; if (!isClosed) Render(); }
        };
        return picker;
    }

    private Button UninstallButton(CatalogApp app, InstalledApp installed)
    {
        var button = new Button { Content = "Uninstall", IsEnabled = store.CanSave && installCancellation is null };
        button.Click += async (_, _) =>
        {
            if (installCancellation is not null) return;
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Uninstall " + app.Name + "?",
                Content = "Close the app first. Aquarium will remove its managed program files and keep your settings, personal files, and library bookmark.",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || installCancellation is not null) return;
            using var operation = new CancellationTokenSource();
            installCancellation = operation;
            Render();
            try
            {
                var cleaned = await installer.UninstallAsync(installed);
                if (!isClosed)
                    ShowNotice(cleaned ? app.Name + " was uninstalled." : app.Name + " was unregistered, but some program files could not be removed.",
                        cleaned ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Uninstall failed: " + ex.Message, InfoBarSeverity.Error); }
            finally { installCancellation = null; if (!isClosed) Render(); }
        };
        return button;
    }

    private async Task CheckPackagesAsync()
    {
        if (installCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        installCancellation = cancellation; available.Clear(); availability.Clear();
        ReportInstall("Checking public GitHub releases…"); Render();
        try
        {
            foreach (var source in GitHubPackages.Sources)
            {
                try
                {
                    var package = await feeds.FindAsync(source, cancellation.Token, store.GetReleaseChannel(source.AppId));
                    available[source.AppId] = package;
                    availability[source.AppId] = $"Published {package.Version} · {package.Size / 1048576d:F1} MB · SHA-256 available";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { availability[source.AppId] = "Unavailable: " + ex.Message; }
            }
            ReportInstall("Public release check finished. Review packages on Updates or App install.");
        }
        catch (OperationCanceledException) { ReportInstall("Release check canceled or timed out."); }
        finally { installCancellation = null; if (!isClosed) Render(); }
    }
    private async Task InstallPublishedAsync(CatalogApp app, AppPackage package)
    {
        if (installCancellation is not null) return;
        var confirmation = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Install " + app.Name + "?",
            Content = $"Download {package.Version} from {package.Source.Repository} ({package.Size / 1048576d:F1} MB), verify its SHA-256, and install a separate app folder managed by Aquarium. A published version can differ from a bundled development build. Previous managed versions and user data are retained. The app will not start automatically.",
            PrimaryButtonText = "Install", CloseButtonText = "Cancel"
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary || installCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        installCancellation = cancellation; ReportInstall("Starting " + app.Name + " installation…"); Render();
        try { await installer.DownloadAndInstallAsync(package, new Progress<string>(ReportInstall), cancellation.Token); }
        catch (Exception ex) { ReportInstall(ex is OperationCanceledException ? "Installation canceled. Previous installed version retained." : "Installation failed: " + ex.Message); }
        finally { downloadHistory.Add($"{DateTimeOffset.Now:g} · {downloadState}"); installCancellation = null; if (!isClosed) Render(); }
    }
    private void ReportInstall(string status)
    {
        downloadState = status;
        if (!isClosed && downloadLabel is not null) downloadLabel.Text = status;
    }
    private void RenderDownloads()
    {
        downloadLabel = Text(downloadState, 16);
        PageBody.Children.Add(downloadLabel);
        if (installCancellation is not null)
        {
            PageBody.Children.Add(new ProgressBar { IsIndeterminate = true });
            var cancel = new Button { Content = "Cancel current check / installation" };
            cancel.Click += (_, _) => installCancellation?.Cancel(); PageBody.Children.Add(cancel);
        }
        foreach (var item in downloadHistory.TakeLast(20).Reverse()) PageBody.Children.Add(Text(item, 13, true));
        PageBody.Children.Add(Text("Only one app install runs at a time. Downloads are verified before extraction. Failed attempts keep the previous installation registered.", 13, true));
    }
    private void LaunchInstalled(InstalledApp app)
    {
        try { installer.Launch(app); }
        catch (Exception ex) { ShowNotice("Could not open the app: " + ex.Message, InfoBarSeverity.Error); }
    }
    private async Task InstallBundledAsync(string? requestedAppId = null)
    {
        if (installCancellation is not null || !store.CanSave) return;
        var manifest = Path.Combine(AppContext.BaseDirectory, "Bundled", "bundled.json");
        if (!File.Exists(manifest)) return; // A source-only build has no default app bundle.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        installCancellation = cancellation; ReportInstall("Finishing setup of included apps…"); Render();
        try
        {
            var bundled = JsonSerializer.Deserialize<List<BundledPackage>>(await File.ReadAllTextAsync(manifest, cancellation.Token), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Invalid included-app manifest.");
            foreach (var item in bundled)
            {
                var source = GitHubPackages.Sources.Single(s => s.AppId == item.AppId && s.IncludedByDefault);
                if (store.Registry.Installations.Any(a => a.AppId == item.AppId)) continue;
                if (requestedAppId is null ? store.Registry.UninstalledAppIds.Contains(item.AppId) : requestedAppId != item.AppId) continue;
                if (Path.GetFileName(item.File) != item.File || Path.GetExtension(item.File) != ".zip") throw new InvalidDataException("Invalid included package filename.");
                await installer.InstallZipAsync(source, item.Version, Path.Combine(AppContext.BaseDirectory, "Bundled", item.File), item.Sha256, new Progress<string>(ReportInstall), cancellation.Token);
                if (source.AppId == "aquarium.security") ApplyInitialFishTankAppearance();
            }
        }
        catch (Exception ex) { ReportInstall("Included-app setup did not finish: " + ex.Message); if (!isClosed) ShowNotice(downloadState, InfoBarSeverity.Warning); }
        finally { downloadHistory.Add($"{DateTimeOffset.Now:g} · {downloadState}"); installCancellation = null; if (!isClosed) Render(); }
    }
    private static void ApplyInitialFishTankAppearance()
    {
        // The released FishTank supports custom colors. Seed only a brand-new profile;
        // never replace the user's saved appearance or any security preferences.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishTank");
        var file = Path.Combine(folder, "appearance.json");
        if (File.Exists(file)) return;
        Directory.CreateDirectory(folder);
        var settings = new
        {
            Version = 1, Theme = "Ocean", Accent = "Mint", CustomPalette = true,
            Shape = "Rounded", CornerRadius = 16, BackgroundStyle = "Gradient",
            TextColor = "#F1F7FF", MutedColor = "#A0B8C3", TopColor = "#071219",
            BottomColor = "#09151D", SurfaceColor = "#10242F", AccentColor = "#67E8D3", SecondaryColor = "#1B203B"
        };
        using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, settings, new JsonSerializerOptions { WriteIndented = true });
    }
}
