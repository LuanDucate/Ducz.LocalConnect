using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.App.Controls;

public sealed class RemoteScreenView : Grid
{
    private const double MouseMoveMinIntervalMilliseconds = 12;

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(ClientSession), typeof(RemoteScreenView), new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty IsKeyboardCapturedProperty = DependencyProperty.Register(
        nameof(IsKeyboardCaptured), typeof(bool), typeof(RemoteScreenView), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFullScreenProperty = DependencyProperty.Register(
        nameof(IsFullScreen), typeof(bool), typeof(RemoteScreenView), new PropertyMetadata(false));

    public static readonly DependencyProperty ToggleFullScreenCommandProperty = DependencyProperty.Register(
        nameof(ToggleFullScreenCommand), typeof(ICommand), typeof(RemoteScreenView), new PropertyMetadata(null));

    public static readonly DependencyProperty ToggleKeyboardCaptureCommandProperty = DependencyProperty.Register(
        nameof(ToggleKeyboardCaptureCommand), typeof(ICommand), typeof(RemoteScreenView), new PropertyMetadata(null));

    private readonly Image _image;
    private readonly TextBlock _placeholder;

    private WriteableBitmap? _bitmap;
    private ClientSession? _subscribedSession;
    private long _lastMouseMoveTicks;

    public RemoteScreenView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _placeholder = new TextBlock
        {
            Text = "Waiting for the first frame…",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Children.Add(_image);
        Children.Add(_placeholder);

        MouseMove += OnMouseMove;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Unloaded += (_, _) => Subscribe(null);
        Loaded += (_, _) => Subscribe(Session);
    }

    public ClientSession? Session
    {
        get => (ClientSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public bool IsKeyboardCaptured
    {
        get => (bool)GetValue(IsKeyboardCapturedProperty);
        set => SetValue(IsKeyboardCapturedProperty, value);
    }

    public bool IsFullScreen
    {
        get => (bool)GetValue(IsFullScreenProperty);
        set => SetValue(IsFullScreenProperty, value);
    }

    public ICommand? ToggleFullScreenCommand
    {
        get => (ICommand?)GetValue(ToggleFullScreenCommandProperty);
        set => SetValue(ToggleFullScreenCommandProperty, value);
    }

    public ICommand? ToggleKeyboardCaptureCommand
    {
        get => (ICommand?)GetValue(ToggleKeyboardCaptureCommandProperty);
        set => SetValue(ToggleKeyboardCaptureCommandProperty, value);
    }

    public void Clear()
    {
        _bitmap = null;
        _image.Source = null;
        _placeholder.Visibility = Visibility.Visible;
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (RemoteScreenView)d;
        if (view.IsLoaded)
        {
            view.Subscribe((ClientSession?)e.NewValue);
        }
    }

    private void Subscribe(ClientSession? session)
    {
        if (ReferenceEquals(_subscribedSession, session))
        {
            return;
        }

        if (_subscribedSession is not null)
        {
            _subscribedSession.FrameReceived -= OnFrameReceived;
        }

        _subscribedSession = session;
        if (session is not null)
        {
            session.FrameReceived += OnFrameReceived;
        }
    }

    private void OnFrameReceived(RemoteFrame frame)
    {
        int stride;
        byte[] pixels;
        try
        {
            using var stream = new MemoryStream(frame.ImageBytes.ToArray(), writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            source.Freeze();

            stride = source.PixelWidth * 4;
            pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            if (source.PixelWidth != frame.Width || source.PixelHeight != frame.Height)
            {
                return; // header and image disagree; skip rather than write out of bounds
            }
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException)
        {
            return; // one corrupt frame is not worth tearing the session down
        }

        Dispatcher.InvokeAsync(() => Blit(frame, pixels, stride), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void Blit(RemoteFrame frame, byte[] pixels, int stride)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.DesktopWidth || _bitmap.PixelHeight != frame.DesktopHeight)
        {
            if (frame.Kind != RemoteFrameKind.Full)
            {
                return; // can't apply a delta before the first full frame of this size
            }

            _bitmap = new WriteableBitmap(frame.DesktopWidth, frame.DesktopHeight, 96, 96, PixelFormats.Bgra32, null);
            _image.Source = _bitmap;
            _placeholder.Visibility = Visibility.Collapsed;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), pixels, stride, frame.X, frame.Y);
    }

    private Point? ToRemotePoint(MouseEventArgs e)
    {
        var bitmap = _bitmap;
        if (bitmap is null || _image.ActualWidth <= 0 || _image.ActualHeight <= 0)
        {
            return null;
        }

        var local = e.GetPosition(_image);
        if (local.X < 0 || local.Y < 0 || local.X > _image.ActualWidth || local.Y > _image.ActualHeight)
        {
            return null;
        }

        var x = (int)Math.Round(local.X / _image.ActualWidth * bitmap.PixelWidth);
        var y = (int)Math.Round(local.Y / _image.ActualHeight * bitmap.PixelHeight);
        return new Point(Math.Clamp(x, 0, bitmap.PixelWidth - 1), Math.Clamp(y, 0, bitmap.PixelHeight - 1));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var session = Session;
        var point = ToRemotePoint(e);
        if (session is null || !session.IsConnected || point is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastMouseMoveTicks, now).TotalMilliseconds < MouseMoveMinIntervalMilliseconds)
        {
            return;
        }

        _lastMouseMoveTicks = now;
        _ = session.SendMouseMoveAsync((int)point.Value.X, (int)point.Value.Y);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        SendButton(e, isDown: true);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        SendButton(e, isDown: false);
        if (e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released && e.MiddleButton == MouseButtonState.Released)
        {
            ReleaseMouseCapture();
        }
    }

    private void SendButton(MouseButtonEventArgs e, bool isDown)
    {
        var session = Session;
        var point = ToRemotePoint(e);
        var button = ToRemoteButton(e.ChangedButton);
        if (session is null || !session.IsConnected || point is null || button is null)
        {
            return;
        }

        e.Handled = true;
        _ = session.SendMouseButtonAsync(button.Value, isDown, (int)point.Value.X, (int)point.Value.Y);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var session = Session;
        var point = ToRemotePoint(e);
        if (session is null || !session.IsConnected || point is null)
        {
            return;
        }

        e.Handled = true;
        _ = session.SendMouseWheelAsync((int)point.Value.X, (int)point.Value.Y, e.Delta);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.F11)
        {
            e.Handled = true;
            ToggleFullScreenCommand?.Execute(null);
            return;
        }

        if (key == Key.Home && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            e.Handled = true;
            ToggleKeyboardCaptureCommand?.Execute(null);
            return;
        }

        if (key == Key.Escape && IsFullScreen && !IsKeyboardCaptured)
        {
            e.Handled = true;
            ToggleFullScreenCommand?.Execute(null);
            return;
        }

        ForwardKey(e, key, isDown: true);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.F11 || (key == Key.Escape && IsFullScreen && !IsKeyboardCaptured))
        {
            e.Handled = true;
            return;
        }

        ForwardKey(e, key, isDown: false);
    }

    private void ForwardKey(KeyEventArgs e, Key key, bool isDown)
    {
        var session = Session;
        if (session is null || !session.IsConnected || !IsKeyboardCaptured)
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        e.Handled = true;
        _ = session.SendKeyAsync(virtualKey, isDown);
    }

    private static RemoteMouseButton? ToRemoteButton(MouseButton button) => button switch
    {
        MouseButton.Left => RemoteMouseButton.Left,
        MouseButton.Right => RemoteMouseButton.Right,
        MouseButton.Middle => RemoteMouseButton.Middle,
        _ => null,
    };
}
