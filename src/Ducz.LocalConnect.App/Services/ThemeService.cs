using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Ducz.LocalConnect.App.Services;

/// <summary>Maps the saved <see cref="ThemePreference"/> onto WPF-UI's theme manager.</summary>
public static class ThemeService
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    private static Window? _watchedWindow;

    public static void Apply(ThemePreference preference, Window? window)
    {
        if (_watchedWindow is not null)
        {
            SystemThemeWatcher.UnWatch(_watchedWindow);
            _watchedWindow = null;
        }

        switch (preference)
        {
            case ThemePreference.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, Backdrop, updateAccent: true);
                break;
            case ThemePreference.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, Backdrop, updateAccent: true);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
                if (window is not null)
                {
                    SystemThemeWatcher.Watch(window, Backdrop, updateAccents: true);
                    _watchedWindow = window;
                }

                break;
        }
    }
}
