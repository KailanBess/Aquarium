using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private void AddFamilyAppearanceControls()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text("Apply a family palette", 22));
        body.Children.Add(Text("Choose a common palette for initialized companion profiles. This changes colors only. Each app keeps its layout, accessibility, libraries and security settings. Close companion apps first. A before-change record is retained beside each changed file.", 14, true));
        var background = new ComboBox { Header = "Family background", ItemsSource = new[] { "Ocean", "Midnight", "Slate" }, SelectedIndex = 0 };
        var accent = new ComboBox { Header = "Family accent", ItemsSource = new[] { "Mint", "Blue", "Coral" }, SelectedIndex = 0 };
        body.Children.Add(background); body.Children.Add(accent);
        var choices = new[] { "FishBowl", "FishNet", "FishTank" }.Select(a => new CheckBox { Content = a, IsChecked = true }).ToArray();
        foreach (var choice in choices) body.Children.Add(choice);
        var preview = new Button { Content = "Review palette for selected apps" };
        preview.Click += async (_, _) =>
        {
            if (recoveryRunning || busy || installCancellation is not null || shellUpdateRunning) return;
            List<ProfileBackup>? changes = null;
            recoveryRunning = true; preview.IsEnabled = false;
            try
            {
                var selected = choices.Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToArray();
                RequireCompanionsClosed(selected);
                using var profileLeases = new CompanionLeases(ProfileRoot, selected);
                changes = FamilyAppearance.Preview(ProfileRoot, (string)background.SelectedItem, (string)accent.SelectedItem,
                    selected);
                if (changes.Count == 0) throw new InvalidOperationException("No initialized selected profiles were found. Open each app once before applying a family palette.");
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Apply reviewed family colors?",
                    Content = "Background: " + background.SelectedItem + "\nAccent: " + accent.SelectedItem + "\nApps: " + string.Join(", ", changes.Select(c => c.App)) + "\nCustom palettes will be disabled; their saved colors are retained. Layout and text preferences are preserved. A .family-before.bak copy will be kept before each change.",
                    PrimaryButtonText = "Apply family colors", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || isClosed) return;
                RequireCompanionsClosed(selected);
                foreach (var change in changes)
                {
                    var path = Path.Combine(ProfileRoot, change.App, change.File);
                    PackageInstaller.RefuseLinks(path + ".family-before.bak");
                    File.Copy(path, path + ".family-before.bak", overwrite: true);
                }
                SuiteBackup.Restore(ProfileRoot, changes, () => RequireCompanionsClosed(selected));
                ShowNotice("Family colors applied. Open the selected apps to see them. Layout, accessibility and security preferences were kept.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Family appearance was not applied. " + ex.Message, InfoBarSeverity.Error); }
            finally { if (changes is not null) SuiteBackup.Clear(changes); recoveryRunning = false; preview.IsEnabled = true; }
        };
        body.Children.Add(preview); PageBody.Children.Add(Panel(body));
    }
}
