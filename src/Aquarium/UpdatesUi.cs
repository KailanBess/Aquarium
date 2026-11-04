using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private readonly UpdateService updates = new();
    private readonly Appearance appearance = new();
    private CancellationTokenSource? updateCancellation;
    private string updateOutput = "No outside-app check has run in this session.";
    private IReadOnlyList<ReleaseInfo> releases = [];
    private string releaseStatus = "Aquarium-family releases have not been checked.";

    private void RenderUpdates()
    {
        RenderSchedule();
        var manage = new Button { Content = "Manage Aquarium app installs and updates" };
        manage.Click += (_, _) => Navigation.SelectedItem = Navigation.MenuItems.Cast<NavigationViewItem>().Single(i => i.Tag?.ToString() == "App install");
        PageBody.Children.Add(manage);
        Empty("One home for updates", "Aquarium owns update management for the Aquarium family. FishNet has no updater. Outside-app checks use the WinGet community source; coverage is limited to apps it can match. Other apps may still update themselves.");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var check = new Button { Content = "Check outside apps", IsEnabled = updateCancellation is null };
        var own = new Button { Content = "Check Aquarium releases", IsEnabled = updateCancellation is null };
        var cancel = new Button { Content = "Cancel check", IsEnabled = updateCancellation is not null };
        check.Click += async (_, _) => await RunCheck(false);
        own.Click += async (_, _) => await RunCheck(true);
        cancel.Click += (_, _) => updateCancellation?.Cancel();
        actions.Children.Add(check); actions.Children.Add(own); actions.Children.Add(cancel); PageBody.Children.Add(actions);
        PageBody.Children.Add(Text(releaseStatus, 14, true));
        foreach (var release in releases)
        {
            var link = new HyperlinkButton { Content = $"{release.Name} · {release.Tag} · View releases", NavigateUri = new Uri(release.Url) };
            PageBody.Children.Add(link);
            PageBody.Children.Add(new Expander { Header = release.Name + " release notes", Content = new TextBox { Text = string.IsNullOrWhiteSpace(release.Notes) ? "No release notes provided." : release.Notes, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 260 } });
        }
        PageBody.Children.Add(Text("Release tags and notes are provided for review. Open App install for checksum-verified managed packages and installed-version details. No release is silently downloaded or applied.", 13, true));
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
    private async Task RunCheck(bool aquariumFamily)
    {
        if (updateCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        updateCancellation = cancellation;
        if (aquariumFamily) releaseStatus = "Checking published releases…"; else updateOutput = "Checking WinGet…";
        if (page == "Updates") Render();
        try
        {
            if (aquariumFamily)
            {
                releases = await updates.CheckAquariumReleasesAsync(cancellation.Token);
                releaseStatus = $"Release check finished {DateTimeOffset.Now:g}. Tags are available-release information, not proof an installed app needs an update.";
            }
            else
            {
                var report = await updates.CheckOutsideAppsAsync(cancellation.Token);
                updateOutput = $"Checked {report.CheckedAt:g} · WinGet exit code {report.ExitCode}\n{report.Output}";
            }
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException ? "Check canceled or timed out. No updates were installed by this check." : "Check failed: " + ex.Message;
            if (aquariumFamily) releaseStatus = message; else updateOutput = message;
        }
        finally
        {
            updateCancellation = null;
            if (!isClosed && page == "Updates") Render();
        }
    }
}

