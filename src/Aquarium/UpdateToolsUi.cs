using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? scheduleTimer;
    private bool scheduledChecking;
    private void StartUpdateSchedule()
    {
        scheduleTimer = DispatcherQueue.CreateTimer(); scheduleTimer.Interval = TimeSpan.FromMinutes(1);
        scheduleTimer.Tick += async (_, _) =>
        {
            if (isClosed || scheduledChecking || installCancellation is not null || updateCancellation is not null) return;
            scheduledChecking = true;
            try
            {
                var schedule = UpdateSchedule.Load(); if (!schedule.IsDue(DateTimeOffset.UtcNow)) return;
                schedule.LastAttempt = DateTimeOffset.UtcNow; schedule.Save();
                await RunCheck(true);
                if (schedule.IncludeOutsideApps && !isClosed) await RunCheck(false);
            }
            catch (Exception ex) { if (!isClosed) { scheduleTimer?.Stop(); ShowNotice("Scheduled checks stopped: " + ex.Message, InfoBarSeverity.Warning); } }
            finally { scheduledChecking = false; }
        };
        scheduleTimer.Start();
    }
    private void RenderSchedule()
    {
        try
        {
            var schedule = UpdateSchedule.Load();
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Text("Scheduled update checks", 22));
            body.Children.Add(Text("Runs while Aquarium is open. Checks only; installations always need review. Missed checks run once. Outside-app checks use WinGet and do not accept source agreements.", 13, true));
            var interval = new ComboBox { Header = "Check frequency", ItemsSource = new[] { "Off", "Hourly", "Daily", "Weekly" }, SelectedIndex = Array.IndexOf(new[] { 0, 1, 24, 168 }, schedule.Hours) };
            var outside = new CheckBox { Content = "Also check supported outside apps", IsChecked = schedule.IncludeOutsideApps };
            var save = new Button { Content = "Save check schedule" };
            save.Click += (_, _) => { try { new UpdateSchedule { Hours = new[] { 0, 1, 24, 168 }[interval.SelectedIndex], IncludeOutsideApps = outside.IsChecked == true }.Save(); scheduleTimer?.Start(); ShowNotice("Update-check schedule saved.", InfoBarSeverity.Success); } catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); } };
            body.Children.Add(interval); body.Children.Add(outside); body.Children.Add(save);
            PageBody.Children.Add(Panel(body));
        }
        catch (Exception ex) { Empty("Schedule unavailable", "Saved data was preserved. " + ex.Message); }
    }
    private void AddRollbackOptions(StackPanel body, CatalogApp app)
    {
        var history = store.Registry.InstallationHistory.Where(i => i.AppId == app.Id).OrderByDescending(i => i.InstalledAt).ToList();
        if (history.Count == 0) return;
        var choice = new ComboBox { Header = "Retained program versions", ItemsSource = history, DisplayMemberPath = "Version", SelectedIndex = 0 };
        var rollback = new Button { Content = "Review rollback", IsEnabled = store.CanSave && installCancellation is null };
        rollback.Click += async (_, _) =>
        {
            if (choice.SelectedItem is not InstalledApp previous) return;
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Roll back " + app.Name + "?", Content = "Close the app first. Aquarium will select retained program version " + previous.Version + ". User data is kept and is not downgraded; older programs may not understand newer settings. Program files have not been re-downloaded or reverified. The current program folder will be retained too.", PrimaryButtonText = "Use retained version", CloseButtonText = "Cancel" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try { await installer.RollbackAsync(previous); Render(); ShowNotice("Retained version selected. User data was kept.", InfoBarSeverity.Success); }
            catch (Exception ex) { ShowNotice("Rollback failed: " + ex.Message, InfoBarSeverity.Error); }
        };
        body.Children.Add(choice); body.Children.Add(rollback);
    }
}
