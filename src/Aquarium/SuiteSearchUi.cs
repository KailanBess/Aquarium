using Microsoft.UI.Xaml.Controls;

namespace Aquarium;

public sealed partial class MainWindow
{
    private void SearchSuite(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || !ready) return;
        var query = sender.Text.Trim();
        var options = new[] { "Updates · Update all apps", "Recovery · Repair, roll back, backup and support report", "Settings · Appearance", "Downloads · Installation results" }
            .Concat(store.Catalog.Select(a => a.Name + " · " + a.Category + " · " + a.Summary));
        sender.ItemsSource = query.Length == 0 ? Array.Empty<string>() : options.Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8).ToArray();
    }

    private void ChooseSuiteResult(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        var chosen = (string)args.SelectedItem;
        var app = store.Catalog.FirstOrDefault(a => chosen.StartsWith(a.Name + " · ", StringComparison.Ordinal));
        var destination = app is null ? chosen.Split('·')[0].Trim() : "Library";
        if (destination == "Settings") Navigation.SelectedItem = Navigation.SettingsItem;
        else Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == destination);
        sender.Text = "";
        if (app is not null) ShowNotice("Open " + app.Name + " from its Library card to use its tools.", InfoBarSeverity.Informational);
    }
}
