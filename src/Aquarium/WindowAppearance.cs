using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aquarium;

public sealed partial class MainWindow
{
    private const int DwmBorderColor = 34;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, uint size);

    private void ApplyAppearance()
    {
        var accent = new SolidColorBrush(appearance.Accent);
        Wordmark.Foreground = PageEyebrow.Foreground = accent;
        Navigation.Background = appearance.SurfaceBrush;
        PageScroll.Background = Root.Background = appearance.PageBrush;
        PageTitle.FontSize = appearance.TitleSize;
        Navigation.OpenPaneLength = appearance.SidebarWidth;
        PageBody.Spacing = appearance.Compact ? 8 : 16;
        foreach (var card in PageBody.Children.OfType<Border>())
        {
            card.Background = appearance.SurfaceBrush;
            card.CornerRadius = new CornerRadius(appearance.CardRadius);
            card.Padding = new Thickness(appearance.Compact ? 14 : 24);
        }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            bool useSystem = appearance.TitleBar == "System";
            bool useAccent = appearance.TitleBar == "Accent";
            Windows.UI.Color? background = useSystem ? null : useAccent ? appearance.Accent : appearance.PageBrush.Color;
            Windows.UI.Color? foreground = useSystem ? null : useAccent ? Colors.Black : Colors.White;
            titleBar.BackgroundColor = titleBar.ButtonBackgroundColor = background;
            titleBar.ForegroundColor = titleBar.ButtonForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = titleBar.ButtonInactiveBackgroundColor = useSystem ? null : appearance.SurfaceBrush.Color;
            titleBar.InactiveForegroundColor = titleBar.ButtonInactiveForegroundColor = useSystem ? null : Colors.White;
            titleBar.ButtonHoverBackgroundColor = titleBar.ButtonPressedBackgroundColor = useSystem ? null : appearance.SurfaceBrush.Color;
            titleBar.ButtonHoverForegroundColor = titleBar.ButtonPressedForegroundColor = useSystem ? null : Colors.White;
        }

        var border = appearance.BorderColor;
        uint color = border is { } rgb
            ? (uint)(rgb.R | (rgb.G << 8) | (rgb.B << 16))
            : appearance.HideBorder ? 0xFFFFFFFEu : 0xFFFFFFFFu;
        var applied = DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this),
            DwmBorderColor, ref color, sizeof(uint)) == 0;
        Root.BorderBrush = border is { } fallback ? new SolidColorBrush(fallback) : null;
        Root.BorderThickness = new Thickness(!applied && border.HasValue ? 2 : 0);
    }
}
