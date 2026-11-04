using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aquarium;

public sealed partial class MainWindow
{
    private bool railExpanded;
    private void InitializeRail() => RefreshRail();
    private void ToggleSidebar(object sender, RoutedEventArgs e)
    {
        railExpanded = !railExpanded;
        RailColumn.Width = new GridLength(railExpanded ? 208 : 88);
        RefreshRail(); workspaceBounds = null;
    }
    private void RefreshRail()
    {
        RailButtons.Children.Clear(); RailFooter.Children.Clear();
        foreach (var entry in new[] { ("Home", "My apps", Symbol.AllApps), ("Store", "Store", Symbol.Shop), ("Library", "Library", Symbol.Library), ("App install", "Install", Symbol.Add), ("Downloads", "Downloads", Symbol.Download), ("Updates", "Updates", Symbol.Refresh) })
            RailButtons.Children.Add(RailButton(entry.Item1, entry.Item2, entry.Item3));
        RailFooter.Children.Add(RailButton("Settings", "Settings", Symbol.Setting));
    }
    private Button RailButton(string tag, string title, Symbol icon)
    {
        bool selected = page == tag;
        var content = new StackPanel { Orientation = railExpanded ? Orientation.Horizontal : Orientation.Vertical, Spacing = railExpanded ? 14 : 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new SymbolIcon(icon) { Width = 22, Height = 22 });
        content.Children.Add(new TextBlock { Text = title, FontSize = railExpanded ? 14 : 11, HorizontalAlignment = HorizontalAlignment.Center });
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = railExpanded ? HorizontalAlignment.Left : HorizontalAlignment.Center, Height = railExpanded ? 52 : 72, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(railExpanded ? 18 : 4, 0, 4, 0), BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 0), CornerRadius = new CornerRadius(5), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 84, 215, 193)), Background = new SolidColorBrush(selected ? ColorHelper.FromArgb(255, 36, 36, 53) : Colors.Transparent) };
        AutomationPropertiesName(button, title);
        button.Click += (_, _) => { if (tag == "Settings") Navigation.SelectedItem = Navigation.SettingsItem; else SelectPage(tag); };
        return button;
    }
}
