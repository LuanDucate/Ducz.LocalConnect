using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.App.ViewModels;

namespace Ducz.LocalConnect.App.Views;

public partial class SessionPage : Page
{
    public SessionPage()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Current = this;
            Bind(AppServices.Sessions.Active);
        };
        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            if (MainWindow.Current is { IsFullScreen: false } window)
            {
                window.DetachScreenView();
            }
        };
    }

    /// <summary>The page instance currently on screen, if any.</summary>
    public static SessionPage? Current { get; private set; }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

    /// <summary>Shows <paramref name="vm"/> (the active session) and points the shared screen view at it.</summary>
    public void Bind(SessionViewModel? vm)
    {
        DataContext = vm;

        var window = MainWindow.Current;
        if (window is null || window.IsFullScreen)
        {
            return; // the screen view lives in the full-screen host right now
        }

        window.DetachScreenView();
        var view = window.ScreenView;

        if (vm is null)
        {
            view.Session = null;
            ScreenHost.Content = null;
            return;
        }

        view.Session = vm.Session;
        view.ToggleFullScreenCommand = vm.ToggleFullScreenCommand;
        view.ToggleKeyboardCaptureCommand = vm.ToggleKeyboardCaptureCommand;
        view.SetBinding(Controls.RemoteScreenView.IsKeyboardCapturedProperty, new Binding(nameof(SessionViewModel.IsKeyboardCaptured)) { Source = vm });
        view.SetBinding(Controls.RemoteScreenView.IsFullScreenProperty, new Binding(nameof(SessionViewModel.IsFullScreen)) { Source = vm });

        ScreenHost.Content = view;
        if (vm.IsConnected)
        {
            view.Focus();
        }
    }

    private void GoToConnect_Click(object sender, RoutedEventArgs e) => AppServices.Shell.Navigate(PageTags.Connect);
}
