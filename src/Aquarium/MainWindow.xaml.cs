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
        InitializeWorkspaces();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Aquarium.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 820));
        Root.Loaded += Initialize;
        Closed += (_, _) => { isClosed = true; scheduleTimer?.Stop(); workspaceMonitor?.Stop(); updateCancellation?.Cancel(); installCancellation?.Cancel(); };
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
            StartUpdateSchedule();
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
        hostingCancellation?.Cancel();
        HideWorkspace();
        if (ready) Render();
    }

    private static TextBlock Text(string value, double size = 14, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(muted ? ColorHelper.FromArgb(255, 160, 184, 195) : Colors.White)
    };

    private static Border Panel(UIElement child) => new()
    {
        Background = new SolidColorBrush(ColorHelper.FromArgb(255, 40, 40, 50)),
        BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 61, 61, 76)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
        Padding = new Thickness(24), Child = child
    };

    private void Render()
    {
        PageTitle.Text = page;
        PageBody.Children.Clear();
        PageScroll.ChangeView(null, 0, null, true);
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
            case "Store":
                RenderCatalogDashboard();
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
                Empty("Private by design", "The private store uses an Aquarium-only catalog. Update checks contact GitHub or WinGet when requested or when an enabled schedule is due. No accounts or telemetry are implemented.");
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




