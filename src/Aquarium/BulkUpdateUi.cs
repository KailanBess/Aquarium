using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private string bulkUpdateStatus = "";
    private TextBlock? bulkUpdateLabel;

    private void RenderBulkUpdate()
    {
        var updateAll = new Button
        {
            Content = "Update all apps",
            IsEnabled = store.CanSave && installCancellation is null && !shellUpdateRunning && updateCancellation is null
        };
        updateAll.Click += async (_, _) => await UpdateAllAppsAsync();
        PageBody.Children.Add(updateAll);
        bulkUpdateLabel = Text(bulkUpdateStatus, 14, true);
        PageBody.Children.Add(bulkUpdateLabel);
    }

    private void ReportBulkUpdate(string message)
    {
        bulkUpdateStatus = message;
        ReportInstall(message);
        if (!isClosed && bulkUpdateLabel is not null) bulkUpdateLabel.Text = message;
    }

    private async Task UpdateAllAppsAsync()
    {
        if (!store.CanSave || installCancellation is not null || shellUpdateRunning || updateCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        installCancellation = cancellation;
        var results = new List<string>();
        var queue = new List<AppPackage>();
        ReportBulkUpdate("Checking installed apps for updates…");
        Render();
        try
        {
            // Include the hub last so its replacement is staged after companion updates.
            var sources = GitHubPackages.Sources
                .Where(source => store.Registry.Installations.Any(app => app.AppId == source.AppId))
                .Append(GitHubPackages.Shell);
            foreach (var source in sources)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var name = source == GitHubPackages.Shell ? "Aquarium" : store.Catalog.Single(app => app.Id == source.AppId).Name;
                if (store.GetVersionPin(source.AppId) is string pin)
                {
                    results.Add($"{name} · Pinned to {pin}");
                    continue;
                }
                try
                {
                    using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    checkTimeout.CancelAfter(TimeSpan.FromMinutes(3));
                    var package = await feeds.FindAsync(source, checkTimeout.Token, store.GetReleaseChannel(source.AppId));
                    var current = source == GitHubPackages.Shell ? AquariumSelfUpdate.CurrentVersion :
                        store.Registry.Installations.Single(app => app.AppId == source.AppId).Version;
                    if (source != GitHubPackages.Shell)
                    {
                        available[source.AppId] = package;
                        availability[source.AppId] = $"Published {package.Version} · {package.Size / 1048576d:F1} MB · SHA-256 available";
                    }
                    if (AquariumSelfUpdate.IsNewer(package.Version, current)) queue.Add(package);
                    else results.Add($"{name} · Up to date ({current})");
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    var message = ex is OperationCanceledException ? "Release check timed out" : ex.Message;
                    results.Add($"{name} · Check failed · {message}");
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (isClosed || page != "Updates") return;
            var review = new StackPanel { Spacing = 12 };
            foreach (var package in queue)
                review.Children.Add(Text($"{package.Source.Repository.Split('/')[1]} → {package.Version} · {package.Size / 1048576d:F1} MB", 14));
            foreach (var result in results) review.Children.Add(Text(result, 13, true));
            var outside = new CheckBox
            {
                Content = "Also update Windows apps with WinGet",
                IsEnabled = UpdateService.IsAvailable
            };
            review.Children.Add(outside);
            review.Children.Add(Text(UpdateService.IsAvailable
                ? "WinGet opens separately to update supported Windows apps. Review installer prompts and agreements there. Recheck Windows apps after it finishes."
                : "WinGet is unavailable. Install Microsoft App Installer to enable Windows app updates.", 13, true));
            var scroll = new ScrollViewer { Content = review, MaxHeight = 360 };
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Update all apps",
                Content = scroll,
                PrimaryButtonText = "Update",
                CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = queue.Count > 0,
                DefaultButton = ContentDialogButton.Close
            };
            outside.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            outside.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = queue.Count > 0;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                ReportBulkUpdate(string.Join("\n", results.Append(queue.Count > 0 ? "Updates canceled." : "Release check finished.")));
                return;
            }
            int index = 0;
            foreach (var package in queue)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var name = package.Source.Repository.Split('/')[1];
                ReportBulkUpdate($"Updating {++index} of {queue.Count} · {name} {package.Version}…");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                var progress = new Progress<string>(ReportInstall);
                try
                {
                    if (package.Source == GitHubPackages.Shell)
                    {
                        var directory = await installer.DownloadAsync(package, progress, timeout.Token,
                            file => AquariumSelfUpdate.StageAsync(package, file, timeout.Token));
                        AquariumSelfUpdate.CreateShortcuts(directory);
                        results.Add($"Aquarium · Updated to {package.Version}. Close and reopen Aquarium from the desktop or Start menu.");
                    }
                    else
                    {
                        if (!store.AllowsPublishedVersion(package.Source.AppId, package.Version))
                        {
                            results.Add($"{name} · Skipped because its version is pinned");
                            continue;
                        }
                        await installer.DownloadAndInstallAsync(package, progress, timeout.Token);
                        results.Add($"{name} · Updated to {package.Version}");
                    }
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    results.Add($"{name} · Update failed · {(ex is OperationCanceledException ? "Timed out" : ex.Message)}");
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (outside.IsChecked == true)
            {
                try
                {
                    updates.StartOutsideUpdateAll();
                    results.Add("Windows apps · Updates started in WinGet. Recheck after its installers finish.");
                }
                catch (Exception ex) { results.Add("Windows apps · Could not start · " + ex.Message); }
            }
            ReportBulkUpdate(string.Join("\n", results));
        }
        catch (OperationCanceledException)
        {
            ReportBulkUpdate(string.Join("\n", results.Append("Update all canceled. Completed updates are retained. Remaining apps can be retried.")));
        }
        catch (Exception ex)
        {
            ReportBulkUpdate(string.Join("\n", results.Append("Update all stopped · " + ex.Message)));
        }
        finally
        {
            downloadHistory.Add($"{DateTimeOffset.Now:g} · {bulkUpdateStatus}");
            installCancellation = null;
            if (!isClosed) Render();
        }
    }
}
