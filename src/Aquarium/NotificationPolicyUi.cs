using System.Text.Json;
using AquariumFamily;
using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private void AddNotificationPolicyControls()
    {
        var path = Path.Combine(ProfileRoot, "Aquarium", "notification-policy.json");
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text("Shared quiet hours", 22));
        body.Children.Add(Text("Quiet hours apply to FishBowl release/save-review balloons and FishTank release announcements in versions supporting this preference. In-app review items remain available. Security warnings, errors and requested operation results are preserved. Aquarium, FishNet and the smaller tools currently have no background desktop notices to silence.", 14, true));
        var policy = new NoticeHours { Start = 22, End = 8, Apps = ["FishBowl", "FishTank"] };
        bool writable = true;
        try
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 4096) throw new InvalidDataException();
                policy = JsonSerializer.Deserialize<NoticeHours>(File.ReadAllText(path)) ?? throw new InvalidDataException();
                if (policy.Apps is null || policy.Apps.Any(a => a is not ("FishBowl" or "FishTank")) || policy.Start is < 0 or > 23 || policy.End is < 0 or > 23) throw new InvalidDataException();
            }
        }
        catch { writable = false; body.Children.Add(Text("The current policy could not be read and has been preserved.", 14, true)); }
        var enabled = new CheckBox { Content = "Enable shared quiet hours", IsChecked = policy.Enabled };
        var start = new ComboBox { Header = "Start hour (local time)", ItemsSource = Enumerable.Range(0, 24).ToArray(), SelectedItem = policy.Start };
        var end = new ComboBox { Header = "End hour (local time)", ItemsSource = Enumerable.Range(0, 24).ToArray(), SelectedItem = policy.End };
        var bowl = new CheckBox { Content = "FishBowl informational notices", IsChecked = policy.Apps?.Contains("FishBowl") == true };
        var tank = new CheckBox { Content = "FishTank release announcements", IsChecked = policy.Apps?.Contains("FishTank") == true };
        var save = new Button { Content = "Save shared quiet hours", IsEnabled = writable };
        save.Click += (_, _) =>
        {
            try
            {
                var next = new NoticeHours { Enabled = enabled.IsChecked == true, Start = (int)start.SelectedItem, End = (int)end.SelectedItem,
                    Apps = new[] { bowl.IsChecked == true ? "FishBowl" : "", tank.IsChecked == true ? "FishTank" : "" }.Where(a => a.Length > 0).ToArray() };
                PackageInstaller.RefuseLinks(path); PackageInstaller.RefuseLinks(path + ".tmp");
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next)); File.Move(path + ".tmp", path, true);
                ShowNotice("Shared quiet hours saved. Equal start and end hours leave notices enabled all day.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowNotice("Quiet hours were not saved. " + ex.Message, InfoBarSeverity.Error); }
        };
        foreach (var control in new Microsoft.UI.Xaml.UIElement[] { enabled, start, end, bowl, tank, save }) body.Children.Add(control);
        PageBody.Children.Add(Panel(body));
    }
}
