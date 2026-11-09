using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private readonly UpdateService updates = new();
    private readonly Appearance appearance = new();
    private CancellationTokenSource? updateCancellation;
    private string updateOutput = "No outside-app check has run in this session.";

    private void RenderUpdates()
    {
        RenderSelfUpdate();
        Empty("Aquarium app updates", "Check approved GitHub releases and review the installed and published packages before changing versions.");
        var own = new Button { Content = "Check Aquarium apps", IsEnabled = installCancellation is null };
        own.Click += async (_, _) => await CheckPackagesAsync();
        PageBody.Children.Add(own);
        if (installCancellation is not null)
        {
            var cancelPackages = new Button { Content = "Cancel package operation" };
            cancelPackages.Click += (_, _) => installCancellation?.Cancel();
            PageBody.Children.Add(cancelPackages);
        }
        foreach (var app in store.Catalog)
        {
            var source = GitHubPackages.Sources.Single(item => item.AppId == app.Id);
            var installed = store.Registry.Installations.FirstOrDefault(item => item.AppId == app.Id);
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Text(app.Name, 20));
            panel.Children.Add(Text(installed is null ? "Installed · No" : $"Installed · {installed.Version}", 13, true));
            panel.Children.Add(ChannelPicker(app, source));
            panel.Children.Add(Text(availability.GetValueOrDefault(app.Id, "Published package · Not checked"), 13, true));
            panel.Children.Add(new HyperlinkButton { Content = "Release notes · " + source.Repository, NavigateUri = new Uri($"https://github.com/{source.Repository}/releases") });
            if (available.TryGetValue(app.Id, out var package))
            {
                bool same = installed?.Version == package.Version && installed.PackageSha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase);
                var apply = new Button
                {
                    Content = same ? "Published package installed" : $"Review {package.Version}",
                    IsEnabled = !same && store.CanSave && installCancellation is null
                };
                apply.Click += async (_, _) => await InstallPublishedAsync(app, package);
                panel.Children.Add(apply);
            }
            PageBody.Children.Add(Panel(panel));
        }

        Empty("Windows app updates", "WinGet checks apps it can match to its community source. Other apps may still update themselves.");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var check = new Button { Content = "Check outside apps", IsEnabled = updateCancellation is null };
        var cancel = new Button { Content = "Cancel check", IsEnabled = updateCancellation is not null };
        check.Click += async (_, _) => await RunOutsideCheck();
        cancel.Click += (_, _) => updateCancellation?.Cancel();
        actions.Children.Add(check); actions.Children.Add(cancel); PageBody.Children.Add(actions);
        var report = new TextBox { Text = updateOutput, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 160, MaxHeight = 380, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        ScrollViewer.SetHorizontalScrollBarVisibility(report, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(report, ScrollBarVisibility.Auto);
        PageBody.Children.Add(report);
        PageBody.Children.Add(Text("Results are original WinGet output, including errors and unmatched-app limitations. If source agreements are required, run winget upgrade in a terminal to review them, then check again. No check accepts agreements automatically.", 13, true));
        var id = new TextBox { Header = "Exact package ID from the results", PlaceholderText = "e.g. Spotify.Spotify", MaxLength = 200 };
        var install = new Button { Content = "Review and start selected update", IsEnabled = updateCancellation is null };
        install.Click += async (_, _) =>
        {
            var packageId = id.Text.Trim();
            if (string.IsNullOrWhiteSpace(packageId)) { ShowNotice("Enter an exact package ID first.", InfoBarSeverity.Warning); return; }
            var confirmation = new ContentDialog
            {
                Title = "Update " + packageId + "?", XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Content = "WinGet will open its installer outside Aquarium. Review its publisher, agreements, and any Windows permission prompt. Close the target app first if needed. Aquarium does not track that installer's completion; run another check afterward.",
                PrimaryButtonText = "Start update", CloseButtonText = "Cancel"
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            try { updates.StartOutsideUpdate(packageId); ShowNotice("Update requested in WinGet. Recheck after the installer finishes to verify the result.", InfoBarSeverity.Informational); }
            catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
        };
        PageBody.Children.Add(id); PageBody.Children.Add(install);
    }
    private async Task RunOutsideCheck()
    {
        if (updateCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        updateCancellation = cancellation;
        updateOutput = "Checking WinGet…";
        Render();
        try
        {
            var report = await updates.CheckOutsideAppsAsync(cancellation.Token);
            updateOutput = $"Checked {report.CheckedAt:g} · WinGet exit code {report.ExitCode}\n{report.Output}";
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException ? "Check canceled or timed out. No updates were installed by this check." : "Check failed: " + ex.Message;
            updateOutput = message;
        }
        finally
        {
            updateCancellation = null;
            if (!isClosed && page == "Updates") Render();
        }
    }
}
