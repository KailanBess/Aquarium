using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Aquarium;

public sealed partial class MainWindow
{
    private void RenderCatalogDashboard()
    {
        PageTitle.Text = "App catalog";
        PageSubtitle.Text = "Your Aquarium apps, installed and ready to explore.";
        var layout = new Grid { ColumnSpacing = 24 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(258) });
        var catalog = new StackPanel { Spacing = 20 };
        var search = new TextBox { PlaceholderText = "Search your apps", Header = "Collection" };
        AutomationPropertiesName(search, "Search app catalog");
        var tiles = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var matches = store.Catalog.ToList();
        void Arrange()
        {
            var columns = tiles.ActualWidth is > 0 and < 530 ? 1 : 2;
            if (tiles.ColumnDefinitions.Count == columns && tiles.RowDefinitions.Count == (int)Math.Ceiling(tiles.Children.Count / (double)columns)) return;
            tiles.ColumnDefinitions.Clear(); tiles.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < Math.Ceiling(tiles.Children.Count / (double)columns); i++) tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < tiles.Children.Count; i++) { Grid.SetColumn((FrameworkElement)tiles.Children[i], i % columns); Grid.SetRow((FrameworkElement)tiles.Children[i], i / columns); }
        }
        void Filter()
        {
            tiles.Children.Clear(); tiles.ColumnDefinitions.Clear();
            matches = store.Catalog.Where(a => $"{a.Name} {a.Category}".Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var app in matches) tiles.Children.Add(CatalogTile(app));
            if (matches.Count == 0) tiles.Children.Add(Text("No apps match your search.", 16, true));
            Arrange();
        }
        tiles.SizeChanged += (_, _) => Arrange(); search.TextChanged += (_, _) => Filter();
        catalog.Children.Add(search); catalog.Children.Add(tiles); layout.Children.Add(catalog);
        var summary = new StackPanel { Spacing = 18 };
        summary.Children.Add(Text("Summary", 25));
        summary.Children.Add(Text($"Installed                         {store.Registry.Installations.Count}", 15, true));
        summary.Children.Add(Text($"Catalog apps                    {store.Catalog.Count}", 15, true));
        summary.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, store.Catalog.Count), Value = store.Registry.Installations.Count });
        summary.Children.Add(Text($"{store.Registry.Installations.Count}/{store.Catalog.Count} apps installed", 14, true));
        summary.Children.Add(Text("Updates", 20));
        summary.Children.Add(Text(available.Count == 0 ? "Check published releases to see available packages." : $"{available.Count} published packages found. Review versions before installing.", 14, true));
        var install = new Button { Content = "Install apps", HorizontalAlignment = HorizontalAlignment.Stretch };
        install.Click += (_, _) => SelectPage("App install"); summary.Children.Add(install);
        var update = new Button { Content = "Manage updates", HorizontalAlignment = HorizontalAlignment.Stretch };
        update.Click += (_, _) => SelectPage("Updates"); summary.Children.Add(update);
        summary.Children.Add(Text("Private collection", 16));
        summary.Children.Add(Text("FishTank · Security\nFishBowl · Emulation\nFishNet · Media", 13, true));
        var summaryCard = Panel(summary); summaryCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(summaryCard, 1); layout.Children.Add(summaryCard);
        layout.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 670;
            summaryCard.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            layout.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 258);
        };
        PageBody.Children.Add(layout); Filter();
    }
    private void SelectPage(string tag) => Navigation.SelectedItem = Navigation.MenuItems.Cast<NavigationViewItem>().Single(i => i.Tag?.ToString() == tag);
    private Border CatalogTile(CatalogApp app)
    {
        var installed = store.Registry.Installations.FirstOrDefault(i => i.AppId == app.Id);
        var panel = new StackPanel { Spacing = 13, MinHeight = 214 };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 5 }; title.Children.Add(Text(app.Name, 22)); title.Children.Add(Text(app.Category, 12, true)); heading.Children.Add(title);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", app.Name + ".png");
        if (File.Exists(iconPath)) { var icon = new Image { Source = new BitmapImage(new Uri(iconPath)), Width = 56, Height = 56 }; Grid.SetColumn(icon, 1); heading.Children.Add(icon); }
        panel.Children.Add(heading); panel.Children.Add(Text(app.Summary, 14, true));
        panel.Children.Add(Text(installed is null ? "Not installed · Optional" : "Installed · " + installed.Version, 12, true));
        var open = new Button { Content = installed is null ? "View install options" : "Open app" };
        open.Click += (_, _) => { if (installed is not null) LaunchInstalled(installed); else SelectPage("App install"); };
        panel.Children.Add(open);
        return Panel(panel);
    }
}
