using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Aquarium;

public sealed partial class MainWindow
{
    private StackPanel LibraryHeading(CatalogApp app)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var fallback = new SymbolIcon(app.ModuleId switch
        {
            "security" => Symbol.Permissions,
            "media" => Symbol.Video,
            _ => Symbol.Library
        });
        var frame = new Grid { Width = appearance.LibraryIconSize, Height = appearance.LibraryIconSize, Children = { fallback } };
        heading.Children.Add(frame);
        var title = Text(app.Name, 24);
        title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(title);

        var installed = store.Registry.Installations.FirstOrDefault(item => item.AppId == app.Id);
        if (installed is not null) _ = LoadLibraryIconAsync(installed, frame);
        return heading;
    }

    private async Task LoadLibraryIconAsync(InstalledApp installed, Grid frame)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(installer.InstalledExecutable(installed));
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 48);
            if (thumbnail is null) return;

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            if (isClosed) return;
            frame.Children.Clear();
            frame.Children.Add(new Image { Source = bitmap });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Keep the category glyph if Explorer cannot supply the installed app icon.
        }
    }
}
