using Microsoft.UI.Xaml;

namespace BetterCapture.App.Services;

internal static class WindowAppearanceService
{
    internal static void ApplyDarkTitleBar(Window window)
    {
        var titleBar = window.AppWindow.TitleBar;
        var background = Microsoft.UI.ColorHelper.FromArgb(255, 31, 31, 31);
        var inactiveBackground = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 38);
        var foreground = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
        var inactiveForeground = Microsoft.UI.ColorHelper.FromArgb(255, 170, 170, 170);
        var hoverBackground = Microsoft.UI.ColorHelper.FromArgb(255, 55, 55, 55);
        var pressedBackground = Microsoft.UI.ColorHelper.FromArgb(255, 72, 72, 72);

        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = inactiveBackground;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
