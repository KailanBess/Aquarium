using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Aquarium;

public sealed partial class MainWindow
{
    private bool catalogInstalledOnly = true;
    private bool catalogListView;
    private static SolidColorBrush Ink(string hex) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1,2),16), Convert.ToByte(hex.Substring(3,2),16), Convert.ToByte(hex.Substring(5,2),16)));
    private void RenderCatalogDashboard()
    {
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var mine = new Button { Content = $"My Apps  {store.Registry.Installations.Count}", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Background = null, BorderThickness = new Thickness(0,0,0,catalogInstalledOnly ? 3 : 0), Padding = new Thickness(8,10,8,14) };
        var all = new Button { Content = $"App Library  {store.Catalog.Count}", FontSize = 22, Background = null, BorderThickness = new Thickness(0,0,0,catalogInstalledOnly ? 0 : 3), Padding = new Thickness(8,10,8,14), Opacity = catalogInstalledOnly ? .65 : 1 };
        mine.Click += (_,_) => { catalogInstalledOnly = true; Render(); }; all.Click += (_,_) => { catalogInstalledOnly = false; Render(); };
        tabs.Children.Add(mine); tabs.Children.Add(all); header.Children.Add(tabs);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var view = new Button { Content = catalogListView ? "Grid view" : "List view", MinWidth = 82 };
        view.Click += (_,_) => { catalogListView = !catalogListView; Render(); }; tools.Children.Add(view);
        Grid.SetColumn(tools,1); header.Children.Add(tools); PageBody.Children.Add(header);
        PageBody.Children.Add(new Border { Height = 1, Background = Ink("#343343"), Margin = new Thickness(0,0,0,4) });
        var layout = new Grid { ColumnSpacing = 24 };
        layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        var collection = new StackPanel { Spacing = 18 };
        var search = new TextBox { PlaceholderText = "Search apps", Height = 40 }; AutomationPropertiesName(search,"Search app catalog"); collection.Children.Add(search);
        var tiles = new Grid { ColumnSpacing = 14, RowSpacing = 14, VerticalAlignment = VerticalAlignment.Top };
        void Arrange()
        {
            int columns = catalogListView ? 1 : Math.Max(1, Math.Min(3, (int)(Math.Max(280, tiles.ActualWidth) / 285)));
            if (tiles.ColumnDefinitions.Count == columns && tiles.RowDefinitions.Count == (int)Math.Ceiling(tiles.Children.Count/(double)columns)) return;
            tiles.ColumnDefinitions.Clear(); tiles.RowDefinitions.Clear();
            for(int i=0;i<columns;i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<Math.Ceiling(tiles.Children.Count/(double)columns);i++) tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for(int i=0;i<tiles.Children.Count;i++) { Grid.SetColumn((FrameworkElement)tiles.Children[i],i%columns); Grid.SetRow((FrameworkElement)tiles.Children[i],i/columns); }
        }
        void Filter()
        {
            tiles.Children.Clear(); tiles.ColumnDefinitions.Clear();
            var matches = store.Catalog.Where(a => (!catalogInstalledOnly || store.Registry.Installations.Any(i=>i.AppId==a.Id)) && (page!="Library" || store.Registry.LibraryAppIds.Contains(a.Id) || store.Registry.Installations.Any(i=>i.AppId==a.Id)) && (a.Name+" "+a.Category).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
            foreach(var app in matches) tiles.Children.Add(CatalogTile(app));
            if(matches.Count==0) tiles.Children.Add(Text("No apps match this view. Open App Library to browse the collection.",15,true));
            Arrange();
        }
        search.TextChanged += (_,_)=>Filter(); tiles.SizeChanged += (_,_)=>Arrange(); collection.Children.Add(tiles); layout.Children.Add(collection);
        var summary = new StackPanel { Spacing = 18 };
        var summaryHeader = new Grid(); summaryHeader.ColumnDefinitions.Add(new ColumnDefinition()); summaryHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summaryHeader.Children.Add(Text("Summary",23)); var rescan = new Button { Content = "↻ Re-scan", Background = null, BorderThickness = new Thickness(0), IsEnabled = installCancellation is null };
        rescan.Click += async (_,_)=>await CheckPackagesAsync(); Grid.SetColumn(rescan,1); summaryHeader.Children.Add(rescan); summary.Children.Add(summaryHeader);
        void Metric(string label, string value) { var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(Text(label,14,true)); var number=Text(value,18); Grid.SetColumn(number,1); row.Children.Add(number); summary.Children.Add(row); }
        Metric("Installed apps", store.Registry.Installations.Count.ToString()); Metric("Available to install", store.Catalog.Count(a=>!store.Registry.Installations.Any(i=>i.AppId==a.Id)).ToString());
        summary.Children.Add(new Border { Height = 1, Background = Ink("#444354"), Margin = new Thickness(0,4,0,4) });
        summary.Children.Add(new ProgressBar { Minimum=0,Maximum=Math.Max(1,store.Catalog.Count),Value=store.Registry.Installations.Count,Height=6 });
        summary.Children.Add(Text($"{store.Registry.Installations.Count}/{store.Catalog.Count} apps installed",13,true));
        summary.Children.Add(Text(available.Count==0 ? "Updates haven't been checked." : $"{available.Count} published packages found. Review versions before installing.",14,true));
        var ornament = new Grid { Height=105, Opacity=.65 };
        ornament.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width=170,Height=170,Stroke=Ink("#53516F"),StrokeThickness=18,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top });
        summary.Children.Add(ornament);
        var update = new Button { Content="Check updates  ▷",Height=52,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Ink("#F7F7FA"),Foreground=Ink("#151520"),FontSize=17,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold };
        update.Click += async (_,_)=> { SelectPage("Updates"); await RunCheck(true); }; summary.Children.Add(update);
        var right = new StackPanel { Spacing=14,VerticalAlignment=VerticalAlignment.Top };
        right.Children.Add(new Border { Background=Ink("#2A293D"),CornerRadius=new CornerRadius(10),Padding=new Thickness(22),Child=summary });
        var device = new StackPanel { Spacing=8 }; device.Children.Add(Text("Microsoft Windows",17)); device.Children.Add(Text($"Build {Environment.OSVersion.Version.Build} · {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}",13,true)); device.Children.Add(Text(Environment.MachineName,14));
        right.Children.Add(new Border { Background=Ink("#171722"),BorderBrush=Ink("#343343"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(20),Child=device });
        Grid.SetColumn(right,1); layout.Children.Add(right);
        layout.SizeChanged += (_,args)=> { bool narrow=args.NewSize.Width<760; right.Visibility=narrow?Visibility.Collapsed:Visibility.Visible; layout.ColumnDefinitions[1].Width=new GridLength(narrow?0:270); };
        PageBody.Children.Add(layout); Filter();
    }
    private void SelectPage(string tag) => Navigation.SelectedItem = Navigation.MenuItems.Cast<NavigationViewItem>().Single(i => i.Tag?.ToString() == tag);
    private Border CatalogTile(CatalogApp app)
    {
        var installed=store.Registry.Installations.FirstOrDefault(i=>i.AppId==app.Id);
        var body=new Grid { RowSpacing=18,MinHeight=catalogListView?100:142 };
        body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var top=new Grid { ColumnSpacing=12 }; top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var title=new StackPanel { Spacing=6 }; var name=Text(app.Name,18); name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; title.Children.Add(name); title.Children.Add(Text(app.Category,12,true)); top.Children.Add(title);
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets",app.Name+".png"); if(File.Exists(iconPath)) { var icon=new Image { Source=new BitmapImage(new Uri(iconPath)),Width=56,Height=56 }; Grid.SetColumn(icon,1); top.Children.Add(icon); }
        body.Children.Add(top);
        var bottom=new Grid { ColumnSpacing=10 }; bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var status=new StackPanel { Spacing=5 };
        status.Children.Add(new Border { Child=new TextBlock { Text=installed is null?"Optional":"Installed",Foreground=Ink(installed is null?"#B0ABCB":"#68CDB7"),FontSize=12,HorizontalAlignment=HorizontalAlignment.Center },BorderBrush=Ink(installed is null?"#66627C":"#3F927F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(12,3,12,3),HorizontalAlignment=HorizontalAlignment.Left });
        if(installed is not null) { var version=Text(installed.Version.Contains("preview",StringComparison.OrdinalIgnoreCase)||installed.Version.Contains("review",StringComparison.OrdinalIgnoreCase)?"Review build":installed.Version,12,true); ToolTipService.SetToolTip(version,installed.Version); status.Children.Add(version); }
        bottom.Children.Add(status);
        var open=new Button { Content=installed is null?"Install":hostedApps.TryGetValue(app.Id,out var session)&&session.IsAlive?"Resume":"Open",VerticalAlignment=VerticalAlignment.Bottom,MinWidth=74 };
        AutomationPropertiesName(open,(installed is null?"Install ":"Open ")+app.Name);
        open.Click += (_,_)=> { if(installed is null) SelectPage("App install"); else LaunchInstalled(installed); };
        Grid.SetColumn(open,1); bottom.Children.Add(open); Grid.SetRow(bottom,1); body.Children.Add(bottom);
        return new Border { Background=Ink("#211F30"),BorderBrush=Ink("#302E42"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(20),Child=body };
    }
    private void RenderStorefront()
    {
        var header=new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        header.Children.Add(Text("Discover your Aquarium",30)); var install=new Button { Content="All apps →" }; install.Click += (_,_)=>SelectPage("App install"); Grid.SetColumn(install,1); header.Children.Add(install); PageBody.Children.Add(header);
        PageBody.Children.Add(Text("Featured",23));
        var heroes=new Grid { ColumnSpacing=18,RowSpacing=18 }; heroes.ColumnDefinitions.Add(new ColumnDefinition()); heroes.ColumnDefinitions.Add(new ColumnDefinition());
        var featured=store.Catalog.Where(a=>a.Name is "FishTank" or "FishNet").ToArray();
        for(int i=0;i<featured.Length;i++) { var card=FeaturedApp(featured[i]); Grid.SetColumn(card,i); heroes.Children.Add(card); }
        heroes.SizeChanged += (_,args)=> { bool narrow=args.NewSize.Width<760; heroes.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star); if(heroes.RowDefinitions.Count==0){heroes.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });heroes.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });} for(int i=0;i<heroes.Children.Count;i++){Grid.SetRow((FrameworkElement)heroes.Children[i],narrow?i:0);Grid.SetColumn((FrameworkElement)heroes.Children[i],narrow?0:i);} };
        PageBody.Children.Add(heroes); PageBody.Children.Add(Text("The Aquarium collection",23));
        var rows=new StackPanel { Spacing=10 }; foreach(var app in store.Catalog) { var row=new Grid { ColumnSpacing=18 }; row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });var copy=new StackPanel { Spacing=5 };copy.Children.Add(Text(app.Name,19));copy.Children.Add(Text(app.Summary,14,true));row.Children.Add(copy);var action=new Button {Content=store.Registry.Installations.Any(i=>i.AppId==app.Id)?"Open":"Get",MinWidth=82,CornerRadius=new CornerRadius(20)};action.Click+=(_,_)=>{var installed=store.Registry.Installations.FirstOrDefault(i=>i.AppId==app.Id);if(installed is null)SelectPage("App install");else LaunchInstalled(installed);};Grid.SetColumn(action,1);row.Children.Add(action);rows.Children.Add(new Border {Background=Ink("#252431"),CornerRadius=new CornerRadius(10),Padding=new Thickness(20),Child=row});} PageBody.Children.Add(rows);
    }
    private Border FeaturedApp(CatalogApp app)
    {
        bool tank=app.Name=="FishTank";
        var background=new LinearGradientBrush { StartPoint=new Point(0,1),EndPoint=new Point(1,0) }; background.GradientStops.Add(new GradientStop {Color=Ink(tank?"#190B35":"#092B3B").Color,Offset=0}); background.GradientStops.Add(new GradientStop {Color=Ink(tank?"#60239B":"#126B86").Color,Offset=1});
        var canvas=new Grid { MinHeight=300,Padding=new Thickness(26),Background=background,CornerRadius=new CornerRadius(12) };
        var decoration=new Microsoft.UI.Xaml.Shapes.Ellipse { Width=260,Height=260,Stroke=Ink(tank?"#B46CFF":"#57CDE6"),StrokeThickness=35,Opacity=.15,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,-70,-70,0) };canvas.Children.Add(decoration);
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets",app.Name+".png");if(File.Exists(iconPath))canvas.Children.Add(new Image {Source=new BitmapImage(new Uri(iconPath)),Width=140,Height=140,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Opacity=.9});
        var content=new StackPanel { Spacing=13,MaxWidth=320,HorizontalAlignment=HorizontalAlignment.Left };
        content.Children.Add(Text(tank?"SECURITY, CLEARLY.":"WATCH. LISTEN. MAKE IT YOURS.",11));var name=Text(app.Name,38);name.FontWeight=Microsoft.UI.Text.FontWeights.Bold;content.Children.Add(name);content.Children.Add(new TextBlock {Text=tank?"Your protection, in focus.\nReview evidence. Run a scan.\nKeep recovery close.":"A home for your media.\nPlay, convert and collect.\nConnect the services you choose.",FontSize=16,TextWrapping=TextWrapping.Wrap,Foreground=Ink("#EEEAF8"),MaxWidth=265});
        var action=new Button {Content=store.Registry.Installations.Any(i=>i.AppId==app.Id)?"Open "+app.Name+"  →":"Install "+app.Name,Background=Ink("#FCFBFF"),Foreground=Ink("#191525"),CornerRadius=new CornerRadius(24),MinWidth=190,Height=44,Margin=new Thickness(0,14,0,0),FontWeight=Microsoft.UI.Text.FontWeights.SemiBold}; action.Click+=(_,_)=>{var installed=store.Registry.Installations.FirstOrDefault(i=>i.AppId==app.Id);if(installed is null)SelectPage("App install");else LaunchInstalled(installed);};content.Children.Add(action);canvas.Children.Add(content);
        return new Border { CornerRadius=new CornerRadius(12),Child=canvas };
    }
}
