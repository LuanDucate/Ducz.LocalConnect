using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.Core.Protocol;
using Microsoft.Win32;

namespace Ducz.LocalConnect.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly IShell _shell;

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private int _defaultPort;

    [ObservableProperty]
    private bool _startHostOnLaunch;

    [ObservableProperty]
    private string _receivedFilesDirectory;

    [ObservableProperty]
    private bool _minimizeToTray;

    public SettingsViewModel(SettingsStore settings, IShell shell)
    {
        _settings = settings;
        _shell = shell;

        var current = settings.Current;
        _theme = current.Theme;
        _defaultPort = current.HostPort;
        _startHostOnLaunch = current.StartHostOnLaunch;
        _receivedFilesDirectory = current.ReceivedFilesDirectory;
        _minimizeToTray = current.MinimizeToTray;
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settings.Current.MinimizeToTray = value;
        _settings.Save();
    }

    public IReadOnlyList<ThemePreference> Themes { get; } = Enum.GetValues<ThemePreference>();

    public int MinPort => ProtocolLimits.MinPort;

    public int MaxPort => ProtocolLimits.MaxPort;

    public string SettingsFilePath => _settings.FilePath;

    partial void OnThemeChanged(ThemePreference value)
    {
        _settings.Current.Theme = value;
        _settings.Save();
        ThemeService.Apply(value, System.Windows.Application.Current?.MainWindow);
    }

    partial void OnDefaultPortChanged(int value)
    {
        _settings.Current.HostPort = value;
        _settings.Save();
    }

    partial void OnStartHostOnLaunchChanged(bool value)
    {
        _settings.Current.StartHostOnLaunch = value;
        _settings.Save();
    }

    partial void OnReceivedFilesDirectoryChanged(string value)
    {
        _settings.Current.ReceivedFilesDirectory = value;
        _settings.Save();
    }

    [RelayCommand]
    private void BrowseReceivedFilesDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Where should received files be saved?",
            InitialDirectory = ReceivedFilesDirectory,
        };

        if (dialog.ShowDialog() == true)
        {
            ReceivedFilesDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void ResetReceivedFilesDirectory()
    {
        ReceivedFilesDirectory = Core.Sessions.HostOptions.DefaultReceivedFilesDirectory;
        _shell.Notify("Reset", "Received files will be saved to the desktop folder again.", NoticeKind.Info);
    }
}
