using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aquarium;

public sealed partial class MainWindow : Window
{
    private readonly LocalStore store = new();
    private string page = "Home";
    private bool ready;
    private bool busy;
    private bool isClosed;
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 820));
        Root.Loaded += Initialize;
        Closed += (_, _) => { isClosed = true; updateCancellation?.Cancel(); installCancellation?.Cancel(); };
    }

    private async void Initialize(object sender, RoutedEventArgs e)
    {
        if (ready) return;
        try
        {
            appearance.Load(); appearance.Apply(Root);
            await store.LoadAsync();
            installer = new PackageInstaller(store);
            ready = true;
            Navigation.SelectedItem = Navigation.MenuItems[0];
            Render();
            if (store.LoadWarning is not null) ShowNotice(store.LoadWarning, InfoBarSeverity.Warning);
            if (store.CanSave) await InstallBundledAsync();
        }
        catch (Exception ex)
        {
            PageSubtitle.Text = "Aquarium could not load its local catalog.";
            ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        page = args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "Home";
        if (ready) Render();
    }

    private static TextBlock Text(string value, double size = 14, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(muted ? ColorHelper.FromArgb(255, 160, 184, 195) : Colors.White)
    };

    private static Border Panel(UIElement child) => new()
    {
        Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 36, 47)),
        BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 34, 65, 78)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
        Padding = new Thickness(24), Child = child
    };

    private void Render()
    {
        PageTitle.Text = page;
        PageBody.Children.Clear();
        downloadLabel = null;
        PageBody.Spacing = appearance.Compact ? 8 : 16;
        PageSubtitle.Text = page switch
        {
            "Home" => "A calmer home for your apps. One private collection, built around you.",
            "Store" => "Only Aquarium apps. Browse the local catalog and build your collection.",
            "Library" => "Your installed apps and saved catalog entries.",
            "App install" => "Choose Aquarium apps from your public GitHub releases.",
            "Downloads" => "Live package activity and installation results.",
            "Updates" => "Check Aquarium-family releases and supported outside apps.",
            _ => "Local storage and the current scope of Aquarium."
        };
        switch (page)
        {
            case "Home":
                var hero = new StackPanel { Spacing = 14 };
                hero.Children.Add(Text("YOUR NEXT EXPLORATION", 11, true));
                hero.Children.Add(Text("Everything in its own current.", 30));
                hero.Children.Add(Text("Security, Emulation, and FishNet — connected through a shared private hub.", 16, true));
                var browse = new Button { Content = "Explore the collection  →" };
                browse.Click += (_, _) => Navigation.SelectedItem = Navigation.MenuItems[1];
                hero.Children.Add(browse);
                PageBody.Children.Add(Panel(hero));
                PageBody.Children.Add(Text($"{store.Catalog.Count} catalog entries   ·   {store.Registry.Installations.Count} installed   ·   {store.Registry.LibraryAppIds.Count} in Library", 14, true));
                foreach (var app in store.Catalog) AddCard(PageBody, app);
                break;
            case "Store":
                var search = new TextBox { PlaceholderText = "Search by app or category", MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationPropertiesName(search, "Search catalog");
                var results = new StackPanel { Spacing = 14 };
                void Filter()
                {
                    results.Children.Clear();
                    var matches = store.Catalog.Where(a => $"{a.Name} {a.Category}".Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var app in matches) AddCard(results, app);
                    if (matches.Count == 0) results.Children.Add(Text("No apps match your search.", 16, true));
                }
                search.TextChanged += (_, _) => Filter();
                PageBody.Children.Add(search);
                PageBody.Children.Add(results);
                Filter();
                break;
            case "Library":
                var saved = store.Catalog.Where(a => store.Registry.LibraryAppIds.Contains(a.Id) || store.Registry.Installations.Any(i => i.AppId == a.Id)).ToList();
                if (saved.Count == 0) Empty("Your collection starts here", "Visit App install to install an app, or save an entry from Store.");
                foreach (var app in saved) AddCard(PageBody, app);
                break;
            case "Downloads":
                RenderDownloads();
                break;
            case "App install":
                RenderInstallApps();
                break;
            case "Updates":
                RenderUpdates();
                break;
            case "Settings":
                PageBody.Children.Add(appearance.Editor(Root, Render, message => ShowNotice(message, InfoBarSeverity.Error)));
                Empty("Private by design", "The private store uses a bundled Aquarium-only catalog. Update checks contact GitHub or WinGet only when requested. No accounts or telemetry are implemented.");
                Empty("Local registry", store.RegistryPath + "\nLibrary bookmarks and managed installation records use schema version 1. Library changes are saved immediately.");
                Empty("App storage", installer.AppRoot + "\nFishTank and FishNet are included in the complete bundle; FishBowl is optional. App data remains in each app's own profile folder. Previous managed program versions are retained.");
                break;
        }
    }

    private static void AutomationPropertiesName(DependencyObject control, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, name);

    private void Empty(string title, string message)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Text(title, 22));
        content.Children.Add(Text(message, 14, true));
        PageBody.Children.Add(Panel(content));
    }

    private void AddCard(StackPanel parent, CatalogApp app)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text($"{app.Category.ToUpperInvariant()}   /   {app.Stage}", 11, true));
        content.Children.Add(Text(app.Name, 24));
        content.Children.Add(Text(app.Summary, 15, true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var details = new Button { Content = "View details" };
        details.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Title = app.Name, Content = app.Description,
                CloseButtonText = "Close"
            };
            await dialog.ShowAsync();
        };
        var included = store.Registry.LibraryAppIds.Contains(app.Id);
        var save = new Button { Content = included ? "Remove from library" : "Save to library", IsEnabled = store.CanSave && !busy };
        save.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            save.IsEnabled = false;
            try
            {
                await store.SetLibraryAsync(app.Id, !included);
                ShowNotice(included ? "Removed from library." : "Saved to library. No package was installed.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowNotice("Library change was not saved: " + ex.Message, InfoBarSeverity.Error); }
            finally { busy = false; Render(); }
        };
        actions.Children.Add(details);
        actions.Children.Add(save);
        content.Children.Add(actions);
        var installed = store.Registry.Installations.FirstOrDefault(i => i.AppId == app.Id);
        var open = new Button { Content = installed is null ? "Install options" : "Open " + app.Name };
        open.Click += (_, _) => { if (installed is not null) LaunchInstalled(installed); else Navigation.SelectedItem = Navigation.MenuItems.Cast<NavigationViewItem>().Single(i => i.Tag?.ToString() == "App install"); };
        content.Children.Add(open);
        content.Children.Add(Text(installed is null ? "Not installed" : "Installed · " + installed.Version, 12, true));
        parent.Children.Add(Panel(content));
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }
}



