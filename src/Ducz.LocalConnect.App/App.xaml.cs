using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Ducz.LocalConnect.App.Services;

namespace Ducz.LocalConnect.App;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(5);

    private bool _shutdownDone;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        var settings = AppServices.Settings.Current;
        var autostart = e.Args.Contains(WindowsStartup.AutostartArgument, StringComparer.OrdinalIgnoreCase);

        // Keep the Run-key entry in step with the saved preference (the exe path can move after an update).
        try
        {
            WindowsStartup.Sync(settings.StartHostWithWindows);
        }
        catch
        {
            // A locked-down registry shouldn't stop the app from starting.
        }

        ThemeService.Apply(settings.Theme, window: null);

        var window = new MainWindow { StartHiddenInTray = autostart };
        window.Closing += OnMainWindowClosing;
        MainWindow = window;
        window.Show();

        ThemeService.Apply(settings.Theme, window);

        if (settings.StartHostOnLaunch || settings.StartHostWithWindows)
        {
            AppServices.HostViewModel.StartCommand.Execute(null);
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownDone)
        {
            return;
        }

        var shutdown = AppServices.ShutdownAsync();
        if (shutdown.IsCompleted)
        {
            _shutdownDone = true;
            return;
        }

        e.Cancel = true;
        var window = (Window)sender!;
        window.IsEnabled = false;

        try
        {
            await Task.WhenAny(shutdown, Task.Delay(ShutdownGracePeriod));
        }
        finally
        {
            _shutdownDone = true;
            // Re-issue the close after the current Closing dispatch has unwound.
            _ = window.Dispatcher.BeginInvoke(window.Close);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(e.Exception.Message, "Ducz LocalConnect", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
