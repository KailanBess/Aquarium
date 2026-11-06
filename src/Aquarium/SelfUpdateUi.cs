using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private bool shellUpdateRunning;
    private string shellUpdateStatus = "";

    private void RenderSelfUpdate()
    {
        var check = new Button { Content = "Check for Aquarium update", IsEnabled = !shellUpdateRunning };
        var repair = new Button { Content = "Repair Aquarium shortcuts", IsEnabled = !shellUpdateRunning };
        var status = Text(shellUpdateStatus, 14, true);
        PageBody.Children.Add(Text($"Aquarium {AquariumSelfUpdate.CurrentVersion}", 20));
        PageBody.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Children = { check, repair }
        });
        PageBody.Children.Add(status);
        repair.Click += (_, _) =>
        {
            try
            {
                AquariumSelfUpdate.CreateShortcuts(Path.GetDirectoryName(Environment.GetEnvironmentVariable("AQUARIUM_LAUNCHER")) ?? AppContext.BaseDirectory);
                ShowNotice("Aquarium shortcuts refreshed on the desktop and Start menu.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
        };
        check.Click += async (_, _) =>
        {
            if (shellUpdateRunning) return;
            shellUpdateRunning = true;
            check.IsEnabled = repair.IsEnabled = false;
            void Report(string message)
            {
                shellUpdateStatus = message;
                status.Text = message;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try
            {
                Report("Checking GitHub releases…");
                var package = await new GitHubPackages().FindAsync(GitHubPackages.Shell, timeout.Token);
                if (!AquariumSelfUpdate.IsNewer(package.Version, AquariumSelfUpdate.CurrentVersion))
                {
                    Report("No newer Aquarium release is available.");
                    return;
                }
                if (isClosed || page != "Updates") return;
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    Title = $"Update Aquarium to {package.Version}?",
                    Content = $"Aquarium will download and verify {package.Size / 1048576d:F0} MB, then update your desktop and Start menu shortcuts. Your settings and app library stay in your profile.",
                    PrimaryButtonText = "Install update",
                    CloseButtonText = "Cancel"
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) { Report("Update canceled."); return; }
                var progress = new Progress<string>(Report);
                var directory = await installer.DownloadAsync(package, progress, timeout.Token,
                    file => AquariumSelfUpdate.StageAsync(package, file, timeout.Token));
                if (isClosed) return;
                AquariumSelfUpdate.CreateShortcuts(directory);
                Report("Update installed. Close Aquarium and reopen it from the desktop or Start menu.");
            }
            catch (Exception ex) { Report($"Aquarium update stopped. {ex.Message}"); }
            finally
            {
                shellUpdateRunning = false;
                if (!isClosed && page == "Updates") Render();
            }
        };
    }
}
