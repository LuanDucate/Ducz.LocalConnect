using System.Diagnostics;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Capture;

public sealed class ScreenStreamer : IDisposable
{
    private readonly CaptureOptions _options;
    private readonly Func<RemoteFrame, CancellationToken, ValueTask> _send;
    private readonly FrameDiffEncoder _encoder;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile string? _requestedDevice;
    private volatile bool _displayChangeRequested;
    private volatile CaptureOptions? _requestedOptions;
    private volatile bool _active = true;
    private volatile bool _resumeRequested;
    private DisplayInfo? _currentDisplay;
    private Thread? _thread;

    public ScreenStreamer(CaptureOptions options, Func<RemoteFrame, CancellationToken, ValueTask> send)
    {
        _options = options;
        _send = send;
        _encoder = new FrameDiffEncoder(options);
    }

    public event Action<Exception>? Failed;

    public event Action<string>? Log;

    public Task Completion => _completion.Task;

    public DisplayInfo? CurrentDisplay => _currentDisplay;

    public void Start(string? deviceName, CancellationToken cancellationToken)
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("The streamer is already running.");
        }

        _requestedDevice = deviceName;
        _displayChangeRequested = true;
        _thread = new Thread(() => Loop(cancellationToken))
        {
            IsBackground = true,
            Name = "Ducz screen capture",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    public void SelectDisplay(string? deviceName)
    {
        _requestedDevice = deviceName;
        _displayChangeRequested = true;
    }

    public void SetQuality(StreamQuality quality) => _requestedOptions = CaptureOptions.ForQuality(quality) with
    {
        TargetFramesPerSecond = _options.TargetFramesPerSecond,
    };

    public void SetActive(bool active)
    {
        if (active && !_active)
        {
            _resumeRequested = true;
        }

        _active = active;
    }

    public Task StopAsync()
    {
        if (_thread is null)
        {
            _completion.TrySetResult();
        }

        return _completion.Task;
    }

    public void Dispose()
    {
        _completion.TrySetResult();
        _encoder.Dispose();
    }

    private void Loop(CancellationToken cancellationToken)
    {
        IScreenSource? source = null;
        var frameClock = new Stopwatch();
        var displayClock = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                frameClock.Restart();

                if (!_active)
                {
                    // Backgrounded: idle cheaply until the client comes back.
                    cancellationToken.WaitHandle.WaitOne(150);
                    continue;
                }

                if (_resumeRequested)
                {
                    _resumeRequested = false;
                    _encoder.Reset(); // first frame after resuming must be full
                }

                var newOptions = Interlocked.Exchange(ref _requestedOptions, null);
                if (newOptions is not null)
                {
                    _encoder.SetOptions(newOptions);
                }

                RemoteFrame? frame;
                try
                {
                    if (_displayChangeRequested || source is null || displayClock.Elapsed >= _options.DisplayRefreshInterval)
                    {
                        source = RefreshSource(source);
                        displayClock.Restart();
                    }

                    frame = _encoder.EncodeNext(source);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Log?.Invoke($"Screen temporarily unavailable ({exception.Message}); retrying.");
                    source?.Dispose();
                    source = null;
                    _encoder.Reset();
                    cancellationToken.WaitHandle.WaitOne(300);
                    continue;
                }

                if (frame is not null)
                {
                    try
                    {
                        _send(frame, cancellationToken).AsTask().GetAwaiter().GetResult();
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }

                var remaining = _options.FrameInterval - frameClock.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    cancellationToken.WaitHandle.WaitOne(remaining);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
        finally
        {
            source?.Dispose();
            _completion.TrySetResult();
        }
    }

    private IScreenSource RefreshSource(IScreenSource? current)
    {
        var explicitChange = _displayChangeRequested;
        _displayChangeRequested = false;

        var display = DisplayInfo.Resolve(_requestedDevice);
        if (current is not null && !explicitChange && current.Bounds == display.Bounds)
        {
            return current;
        }

        current?.Dispose();
        _currentDisplay = display;
        _encoder.Reset();
        return new GdiScreenSource(display.Bounds);
    }
}
