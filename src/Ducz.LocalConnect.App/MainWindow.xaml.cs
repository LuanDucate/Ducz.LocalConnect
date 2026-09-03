using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Ducz.LocalConnect.App.Controls;
using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.App.ViewModels;
using Ducz.LocalConnect.App.Views;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace Ducz.LocalConnect.App;

public partial class MainWindow : FluentWindow, IShell
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkHome = 0x24;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkF11 = 0x7A;

    private const int SessionInsertIndex = 2;

    private readonly SnackbarService _snackbar = new();
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _barHideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly KeyboardHook _keyboardHook;
    private readonly SessionManager _manager;
    private readonly Dictionary<string, NavigationViewItem> _sessionItems = new(); // unpinned live sessions, by session id
    private readonly Dictionary<string, NavigationViewItem> _pinItems = new();     // pinned connections, by pin id

    private SessionViewModel? _session; // the active session, or null
    private TrayService? _tray;

    private WindowState _restoreState;
    private Rect _restoreBounds;
    private double _restoreCaptionHeight;
    private Thickness _restoreResizeBorder;

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        AppServices.Shell = this;
        _manager = AppServices.Sessions;
        _keyboardHook = new KeyboardHook(OnHookedKey);

        ScreenView = new RemoteScreenView();
        _snackbar.SetSnackbarPresenter(SnackbarPresenter);
        Navigation.SetPageProviderService(new ActivatorPageProvider());

        _hintTimer.Tick += (_, _) => HideFullScreenBar();
        _barHideTimer.Tick += (_, _) => HideFullScreenBar();

        TopHoverStrip.MouseEnter += (_, _) => ShowFullScreenBar();
        FullScreenHost.PreviewMouseMove += OnFullScreenMouseMove;
        FullScreenBar.MouseEnter += (_, _) => _barHideTimer.Stop();
        FullScreenBar.MouseLeave += (_, _) => _barHideTimer.Start();
        PreviewKeyDown += OnWindowPreviewKeyDown;

        _manager.SessionAdded += OnSessionAdded;
        _manager.SessionRemoved += OnSessionRemoved;
        _manager.ActiveChanged += OnActiveChanged;
        _manager.PinAdded += OnPinAdded;
        _manager.PinRemoved += OnPinRemoved;
        Navigation.SelectionChanged += OnNavSelectionChanged;

        Loaded += (_, _) =>
        {
            _tray = new TrayService(this);
            InitializePinItems();
            Navigation.Navigate(PageTags.Host);
            if (StartHiddenInTray && AppServices.Settings.Current.MinimizeToTray)
            {
                _tray.HideToTray();
            }
        };
        StateChanged += OnWindowStateChanged;
        Closed += (_, _) =>
        {
            _keyboardHook.Dispose();
            _tray?.Dispose();
        };
    }

    public static MainWindow? Current { get; private set; }

    /// <summary>The one remote-screen surface, re-pointed at whichever session is active.</summary>
    public RemoteScreenView ScreenView { get; }

    public bool IsFullScreen { get; private set; }

    /// <summary>Set before the window loads (autostart) to drop straight into the notification area.</summary>
    public bool StartHiddenInTray { get; set; }

    public SessionViewModel? ActiveSession => _session;

    public void Navigate(string pageTag) => Navigation.Navigate(pageTag);

    public void Notify(string title, string message, NoticeKind kind = NoticeKind.Info)
    {
        if (_tray is { IsInTray: true })
        {
            _tray.Notify(title, message, kind);
            return;
        }

        var (appearance, symbol) = kind switch
        {
            NoticeKind.Success => (ControlAppearance.Success, SymbolRegular.Checkmark24),
            NoticeKind.Warning => (ControlAppearance.Caution, SymbolRegular.Warning24),
            NoticeKind.Error => (ControlAppearance.Danger, SymbolRegular.ErrorCircle24),
            _ => (ControlAppearance.Secondary, SymbolRegular.Info24),
        };

        _snackbar.Show(title, message, appearance, new SymbolIcon(symbol), TimeSpan.FromSeconds(kind == NoticeKind.Error ? 6 : 3));
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        await box.ShowDialogAsync();
    }

    // --- Session & pin navigation items -------------------------------------------------------

    /// <summary>Builds the permanent sidebar items for saved (pinned) connections at startup.</summary>
    private void InitializePinItems()
    {
        foreach (var pin in _manager.Pins.ToArray())
        {
            var item = CreatePinItem(pin);
            Navigation.MenuItems.Insert(NextGroupIndex(), item);
            _pinItems[pin.Id] = item;
        }
    }

    private NavigationViewItem CreatePinItem(PinnedConnection pin)
    {
        var item = new NavigationViewItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Pin24 },
            Content = pin.Name,
            Tag = pin,
            TargetPageType = typeof(SessionPage),
            TargetPageTag = PinTag(pin),
        };

        var edit = new System.Windows.Controls.MenuItem { Header = "Edit address…" };
        edit.Click += (_, _) => EditPin(pin);
        var unpin = new System.Windows.Controls.MenuItem { Header = "Unpin" };
        unpin.Click += (_, _) => _manager.RemovePin(pin);
        item.ContextMenu = new ContextMenu();
        item.ContextMenu.Items.Add(edit);
        item.ContextMenu.Items.Add(unpin);
        return item;
    }

    private void OnSessionAdded(SessionViewModel vm) => UiThread.Run(() =>
    {
        if (vm.IsPinned)
        {
            // Reconnected a pinned entry: its permanent item already exists; bind it to the live name.
            if (_pinItems.TryGetValue(vm.PinId!, out var pinItem))
            {
                BindContentTo(pinItem, vm);
                Navigation.Navigate(PinTag(vm.PinId!));
            }

            return;
        }

        var item = new NavigationViewItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Desktop24 },
            Tag = vm,
            TargetPageType = typeof(SessionPage),
            TargetPageTag = SessionTag(vm),
        };
        BindContentTo(item, vm);

        Navigation.MenuItems.Insert(NextGroupIndex(), item);
        _sessionItems[vm.Id] = item;
        Navigation.Navigate(SessionTag(vm)); // selects the item → OnNavSelectionChanged → SetActive + show
    });

    private void OnSessionRemoved(SessionViewModel vm) => UiThread.Run(() =>
    {
        if (vm.IsPinned && _pinItems.TryGetValue(vm.PinId!, out var pinItem))
        {
            // Pinned session disconnected: keep the item, show it as an offline saved connection.
            var pin = _manager.Pins.FirstOrDefault(p => p.Id == vm.PinId);
            SetContentText(pinItem, pin?.Name ?? vm.Name);
            return;
        }

        if (_sessionItems.Remove(vm.Id, out var item))
        {
            Navigation.MenuItems.Remove(item);
        }
    });

    private void OnPinAdded(PinnedConnection pin, SessionViewModel vm) => UiThread.Run(() =>
    {
        // A live session was pinned: swap its transient item for a permanent pin item.
        if (_sessionItems.Remove(vm.Id, out var sessionItem))
        {
            Navigation.MenuItems.Remove(sessionItem);
        }

        var item = CreatePinItem(pin);
        BindContentTo(item, vm);
        Navigation.MenuItems.Insert(NextGroupIndex(), item);
        _pinItems[pin.Id] = item;
        Navigation.Navigate(PinTag(pin));
    });

    private void OnPinRemoved(PinnedConnection pin, SessionViewModel? liveSession) => UiThread.Run(() =>
    {
        if (_pinItems.Remove(pin.Id, out var pinItem))
        {
            Navigation.MenuItems.Remove(pinItem);
        }

        // If the connection is still live, keep it in the sidebar as an ordinary (unpinned) session.
        if (liveSession is not null)
        {
            var item = new NavigationViewItem
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.Desktop24 },
                Tag = liveSession,
                TargetPageType = typeof(SessionPage),
                TargetPageTag = SessionTag(liveSession),
            };
            BindContentTo(item, liveSession);
            Navigation.MenuItems.Insert(NextGroupIndex(), item);
            _sessionItems[liveSession.Id] = item;
            Navigation.Navigate(SessionTag(liveSession));
        }
    });

    private void OnNavSelectionChanged(NavigationView sender, RoutedEventArgs args)
    {
        switch (Navigation.SelectedItem)
        {
            case NavigationViewItem { Tag: SessionViewModel vm }:
                _manager.SetActive(vm);
                EnsureSessionPage();
                break;

            case NavigationViewItem { Tag: PinnedConnection pin }:
                var live = _manager.FindByPin(pin.Id);
                if (live is not null)
                {
                    _manager.SetActive(live);
                }
                else
                {
                    _ = _manager.ConnectPinAsync(pin); // offline → reconnect
                }

                EnsureSessionPage();
                break;
        }
    }

    private void EnsureSessionPage()
    {
        if (SessionPage.Current is null)
        {
            Navigation.Navigate(typeof(SessionPage));
        }
    }

    private void OnActiveChanged(SessionViewModel? vm) => UiThread.Run(() =>
    {
        if (IsFullScreen)
        {
            SetFullScreen(false);
        }

        if (_session is not null)
        {
            _session.PropertyChanged -= OnSessionPropertyChanged;
        }

        _session = vm;
        if (_session is not null)
        {
            _session.PropertyChanged += OnSessionPropertyChanged;
        }

        FullScreenHost.DataContext = vm;
        SessionPage.Current?.Bind(vm);
        UpdateKeyboardHook();

        // Keep the nav highlight in step when the active session changed programmatically.
        var item = ItemFor(vm);
        if (item is not null && !ReferenceEquals(Navigation.SelectedItem, item))
        {
            Navigation.Navigate((string)item.TargetPageTag);
        }
    });

    private NavigationViewItem? ItemFor(SessionViewModel? vm)
    {
        if (vm is null)
        {
            return null;
        }

        return vm.PinId is { } pinId && _pinItems.TryGetValue(pinId, out var pinItem)
            ? pinItem
            : _sessionItems.GetValueOrDefault(vm.Id);
    }

    private void EditPin(PinnedConnection pin)
    {
        var dialog = new EditPinDialog(pin) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _manager.UpdatePin(pin, dialog.ConnectionName, dialog.HostAddress, dialog.PortValue, dialog.PinValue);
        if (_pinItems.TryGetValue(pin.Id, out var item) && _manager.FindByPin(pin.Id) is null)
        {
            SetContentText(item, pin.Name); // offline item shows the saved name
        }
    }

    private int NextGroupIndex()
        => Math.Min(SessionInsertIndex + _sessionItems.Count + _pinItems.Count, Navigation.MenuItems.Count);

    private static void BindContentTo(NavigationViewItem item, SessionViewModel vm)
        => item.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SessionViewModel.Name)) { Source = vm });

    private static void SetContentText(NavigationViewItem item, string text)
    {
        BindingOperations.ClearBinding(item, ContentControl.ContentProperty);
        item.Content = text;
    }

    private static string SessionTag(SessionViewModel vm) => "session-" + vm.Id;

    private static string PinTag(PinnedConnection pin) => "pin-" + pin.Id;

    private static string PinTag(string pinId) => "pin-" + pinId;

    // --- Full screen --------------------------------------------------------------------------

    public void SetFullScreen(bool enabled)
    {
        if (enabled == IsFullScreen)
        {
            return;
        }

        IsFullScreen = enabled;
        DetachScreenView();
        var chrome = WindowChrome.GetWindowChrome(this);

        if (enabled)
        {
            _restoreState = WindowState;
            _restoreBounds = new Rect(Left, Top, Width, Height);

            var bounds = CurrentMonitorBoundsInDips();
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            if (chrome is not null)
            {
                _restoreCaptionHeight = chrome.CaptionHeight;
                _restoreResizeBorder = chrome.ResizeBorderThickness;
                chrome.CaptionHeight = 0;
                chrome.ResizeBorderThickness = new Thickness(0);
            }

            Chrome.Visibility = Visibility.Collapsed;
            FullScreenHost.Visibility = Visibility.Visible;
            FullScreenContent.Content = ScreenView;

            FullScreenBar.Visibility = Visibility.Visible;
            _hintTimer.Start();
            ScreenView.Focus();
        }
        else
        {
            _hintTimer.Stop();
            _barHideTimer.Stop();
            FullScreenContent.Content = null;
            FullScreenBar.Visibility = Visibility.Collapsed;
            FullScreenHost.Visibility = Visibility.Collapsed;
            Chrome.Visibility = Visibility.Visible;

            if (chrome is not null)
            {
                chrome.CaptionHeight = _restoreCaptionHeight;
                chrome.ResizeBorderThickness = _restoreResizeBorder;
            }

            Topmost = false;
            ResizeMode = ResizeMode.CanResize;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            WindowState = _restoreState;

            SessionPage.Current?.Bind(_session);
        }

        UpdateKeyboardHook();
    }

    /// <summary>Removes the shared view from wherever it currently lives so it can be re-parented.</summary>
    public void DetachScreenView()
    {
        switch (ScreenView.Parent)
        {
            case ContentControl host:
                host.Content = null;
                break;
            case Panel panel:
                panel.Children.Remove(ScreenView);
                break;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionViewModel.IsKeyboardCaptured) or nameof(SessionViewModel.IsConnected))
        {
            UpdateKeyboardHook();
        }
    }

    /// <summary>
    /// The low-level hook is on only in full screen with the keyboard captured: then the Windows
    /// key, Alt+Tab and every other shortcut go to the active session instead of this computer.
    /// </summary>
    private void UpdateKeyboardHook()
    {
        var wanted = IsFullScreen && _session is { IsConnected: true, IsKeyboardCaptured: true };
        if (wanted == _keyboardHook.IsInstalled)
        {
            return;
        }

        if (wanted)
        {
            try
            {
                _keyboardHook.Install();
            }
            catch (Win32Exception exception)
            {
                Notify("Keyboard hook unavailable", exception.Message, NoticeKind.Warning);
            }
        }
        else
        {
            _keyboardHook.Uninstall();
            ReleaseRemoteModifiers();
        }
    }

    // Runs on the UI thread inside the hook; must be quick.
    private bool OnHookedKey(int virtualKey, bool isDown)
    {
        if (GetForegroundWindow() != new WindowInteropHelper(this).Handle)
        {
            return false;
        }

        var session = _session;
        if (session?.Session is not { IsConnected: true } client)
        {
            return false;
        }

        if (virtualKey == VkF11)
        {
            if (isDown)
            {
                Dispatcher.BeginInvoke(() => session.ToggleFullScreenCommand.Execute(null));
            }

            return true;
        }

        if (virtualKey == VkHome && IsKeyDown(VkControl) && IsKeyDown(VkMenu))
        {
            if (isDown)
            {
                Dispatcher.BeginInvoke(() => session.ToggleKeyboardCaptureCommand.Execute(null));
            }

            return true;
        }

        _ = client.SendKeyAsync(virtualKey, isDown);
        return true;
    }

    private void ReleaseRemoteModifiers()
    {
        if (_session?.Session is not { IsConnected: true } client)
        {
            return;
        }

        foreach (var key in new[] { VkShift, VkControl, VkMenu, VkLeftWindows, VkRightWindows })
        {
            _ = client.SendKeyAsync(key, isDown: false);
        }
    }

    private void OnFullScreenMouseMove(object sender, MouseEventArgs e)
    {
        if (e.GetPosition(FullScreenHost).Y <= 4)
        {
            ShowFullScreenBar();
        }
    }

    private void ShowFullScreenBar()
    {
        if (!IsFullScreen)
        {
            return;
        }

        _hintTimer.Stop();
        _barHideTimer.Stop();
        FullScreenBar.Visibility = Visibility.Visible;
    }

    private void HideFullScreenBar()
    {
        _hintTimer.Stop();
        _barHideTimer.Stop();

        if (!FullScreenBar.IsMouseOver)
        {
            FullScreenBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Full-screen exit keys handled at the window, so they work even when focus is on a toolbar
    /// control rather than the remote screen. During keyboard capture the low-level hook handles
    /// real keys before WPF; this covers the not-captured case and injected keys.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsFullScreen || _session is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F11 || (key == Key.Escape && !_session.IsKeyboardCaptured))
        {
            e.Handled = true;
            _session.ToggleFullScreenCommand.Execute(null);
        }
        else if (key == Key.Home && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            e.Handled = true;
            _session.ToggleKeyboardCaptureCommand.Execute(null);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !IsFullScreen && AppServices.Settings.Current.MinimizeToTray && _tray is not null)
        {
            _tray.HideToTray();
        }
    }

    private Rect CurrentMonitorBoundsInDips()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
        var bottomRight = transform.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private sealed class ActivatorPageProvider : INavigationViewPageProvider
    {
        public object? GetPage(Type pageType) => Activator.CreateInstance(pageType);
    }
}
