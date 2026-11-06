using System.Text.Json;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private Button PackageCheckButton(CatalogApp app, InstalledApp installed)
    {
        var button = new Button { Content = "Verify or repair", IsEnabled = store.CanSave && installCancellation is null };
        button.Click += async (_, _) =>
        {
            if (installCancellation is not null) return;
            using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            installCancellation = operation;
            Render();
            try
            {
                var source = GitHubPackages.Sources.Single(item => item.AppId == app.Id);
                var progress = new Progress<string>(ReportInstall);
                async Task<bool> CheckAndRepair(string file)
                {
                    ReportInstall("Verifying installed program files…");
                    var result = await installer.VerifyInstalledFilesAsync(installed, file, operation.Token);
                    if (result.Damaged == 0)
                    {
                        ReportInstall($"{app.Name}: all {result.Checked} packaged files match.");
                        return true;
                    }
                    if (isClosed) return false;
                    var dialog = new ContentDialog
                    {
                        XamlRoot = Root.XamlRoot, Title = $"Repair {app.Name}?",
                        Content = $"{result.Damaged} of {result.Checked} packaged files are missing or changed. Repair installs the same verified package in a fresh folder and keeps your app profile.",
                        PrimaryButtonText = "Repair", CloseButtonText = "Keep current installation",
                        DefaultButton = ContentDialogButton.Close
                    };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        await installer.InstallZipAsync(source, installed.Version, file, installed.PackageSha256, progress, operation.Token);
                        ReportInstall($"{app.Name} was repaired.");
                    }
                    else ReportInstall($"{app.Name}: {result.Damaged} packaged files need repair.");
                    return true;
                }

                var bundled = Path.Combine(AppContext.BaseDirectory, "Bundled");
                var manifest = Path.Combine(bundled, "bundled.json");
                BundledPackage? included = null;
                if (File.Exists(manifest))
                {
                    var packages = JsonSerializer.Deserialize<List<BundledPackage>>(
                        await File.ReadAllTextAsync(manifest, operation.Token), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    included = packages?.SingleOrDefault(item => item.AppId == app.Id &&
                        item.Sha256.Equals(installed.PackageSha256, StringComparison.OrdinalIgnoreCase));
                }

                if (included is not null)
                {
                    if (Path.GetFileName(included.File) != included.File || Path.GetExtension(included.File) != ".zip")
                        throw new InvalidDataException("Invalid bundled package filename.");
                    await CheckAndRepair(Path.Combine(bundled, included.File));
                }
                else
                {
                    var package = await feeds.FindAsync(source, operation.Token);
                    if (!package.Sha256.Equals(installed.PackageSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The exact installed package is unavailable. App install can install the currently published version.");
                    await installer.DownloadAsync(package, progress, operation.Token, CheckAndRepair);
                }
                if (!isClosed) ShowNotice(downloadState, InfoBarSeverity.Informational);
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Verification did not finish. " + ex.Message, InfoBarSeverity.Error); }
            finally { installCancellation = null; if (!isClosed) Render(); }
        };
        return button;
    }

    private void AddRollbackControls(StackPanel panel, CatalogApp app)
    {
        var versions = store.Registry.InstallationHistory.Where(item => item.AppId == app.Id).ToArray();
        if (versions.Length == 0) return;
        var choose = new ComboBox { Header = "Retained versions", ItemsSource = versions.Select(item => $"{item.Version} · {item.InstalledAt:g}").ToArray(), SelectedIndex = 0 };
        var rollback = new Button { Content = "Roll back", IsEnabled = store.CanSave && installCancellation is null };
        rollback.Click += async (_, _) =>
        {
            if (installCancellation is not null || choose.SelectedIndex < 0) return;
            var previous = versions[choose.SelectedIndex];
            var confirm = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = $"Switch {app.Name} to {previous.Version}?",
                Content = "Close the app before switching. Aquarium will open the retained program version next time. Its saved profile is kept, and older versions may handle that profile differently.",
                PrimaryButtonText = "Switch version", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary || installCancellation is not null) return;
            using var operation = new CancellationTokenSource();
            installCancellation = operation;
            try
            {
                await installer.RollbackAsync(previous);
                if (!isClosed) ShowNotice($"{app.Name} now opens version {previous.Version}.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Rollback failed. " + ex.Message, InfoBarSeverity.Error); }
            finally { installCancellation = null; if (!isClosed) Render(); }
        };
        panel.Children.Add(choose);
        panel.Children.Add(rollback);
    }
}
