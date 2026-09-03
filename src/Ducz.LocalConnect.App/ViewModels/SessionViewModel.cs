using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.Core.Clipboard;
using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Sessions;
using Microsoft.Win32;

namespace Ducz.LocalConnect.App.ViewModels;

public partial class SessionViewModel : ObservableObject
{
    private readonly ClientSession _client;
    private readonly SettingsStore _settings;
    private readonly IShell _shell;
    private readonly SessionManager _manager;
    private bool _syncingMonitors;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinned))]
    [NotifyPropertyChangedFor(nameof(PinText))]
    private string? _pinId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardCaptureText))]
    private bool _isKeyboardCaptured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullScreenText))]
    private bool _isFullScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteText))]
    private bool _audioMuted;

    [ObservableProperty]
    private RemoteMonitorInfo? _selectedMonitor;

    [ObservableProperty]
    private StreamQuality _quality;

    [ObservableProperty]
    private string _fpsText = "–";

    [ObservableProperty]
    private string _bandwidthText = "–";

    [ObservableProperty]
    private string _latencyText = "–";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFileCommand))]
    private bool _isTransferring;

    [ObservableProperty]
    private double _transferProgress;

    public SessionViewModel(ClientSession client, SettingsStore settings, IShell shell, SessionManager manager, string name)
    {
        _client = client;
        _settings = settings;
        _shell = shell;
        _manager = manager;
        _name = name;
        _audioMuted = settings.Current.AudioMuted;
        _quality = settings.Current.StreamQuality;
        client.AudioMuted = _audioMuted;

        client.MonitorsChanged += monitors => UiThread.Run(() => SyncMonitors(monitors));
        client.StatsUpdated += stats => UiThread.Run(() => ShowStats(stats));
        client.Log += message => UiThread.Run(() => StatusText = message);
        client.ClipboardReceived += _ => UiThread.Run(() => shell.Notify("Clipboard", $"Remote clipboard from {Name} copied here.", NoticeKind.Success));
        client.Disconnected += reason => UiThread.Run(() => OnHostClosed(reason));
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; }

    public string Pin { get; private set; } = string.Empty;

    public bool IsPinned => PinId is not null;

    public string PinText => IsPinned ? "Unpin" : "Pin";

    public ClientSession Session => _client;

    public ObservableCollection<RemoteMonitorInfo> Monitors { get; } = new();

    public IReadOnlyList<StreamQuality> Qualities { get; } = Enum.GetValues<StreamQuality>();

    public bool IsDisconnected => !IsConnected;

    public string KeyboardCaptureText => IsKeyboardCaptured ? "Keyboard: remote" : "Keyboard: local";

    public string FullScreenText => IsFullScreen ? "Exit full screen" : "Full screen";

    public string MuteText => AudioMuted ? "Unmute" : "Mute";

    public void SetConnection(string host, int port, string pin)
    {
        Host = host;
        Port = port;
        Pin = pin;
    }

    partial void OnNameChanged(string value)
    {
        if (IsPinned)
        {
            _manager.SyncPin(this); // keep the saved connection's name in step
        }
    }

    [RelayCommand]
    private void TogglePin()
    {
        if (IsPinned)
        {
            _manager.Unpin(this);
        }
        else
        {
            _manager.Pin(this);
        }
    }

    public void OnConnected()
    {
        IsConnected = true;
        IsKeyboardCaptured = true;
        StatusText = "Connected.";
        SyncMonitors(_client.Monitors);

        if (Quality != StreamQuality.Balanced)
        {
            _ = _client.SelectQualityAsync(Quality);
        }
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        if (_client.IsConnected)
        {
            _client.AudioMuted = active ? AudioMuted : true;
            _ = _client.SetStreamingAsync(active);
        }

        if (!active)
        {
            FpsText = BandwidthText = LatencyText = "–";
        }
    }

    partial void OnQualityChanged(StreamQuality value)
    {
        _settings.Current.StreamQuality = value;
        _settings.Save();
        if (_client.IsConnected)
        {
            _ = _client.SelectQualityAsync(value);
        }
    }

    partial void OnSelectedMonitorChanged(RemoteMonitorInfo? value)
    {
        if (_syncingMonitors || value is null || value.IsSelected || !_client.IsConnected)
        {
            return;
        }

        _ = _client.SelectMonitorAsync(value.DeviceName);
    }

    partial void OnAudioMutedChanged(bool value)
    {
        if (IsActive && _client.IsConnected)
        {
            _client.AudioMuted = value;
        }

        _settings.Current.AudioMuted = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task SendCtrlAltDelAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        await _client.SendSecureAttentionAsync();
        _shell.Notify("Ctrl+Alt+Del sent", "Only works if the host allows software Ctrl+Alt+Del (see troubleshooting).", NoticeKind.Info);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsFullScreen)
        {
            _shell.SetFullScreen(false);
            IsFullScreen = false;
        }

        await _client.DisconnectAsync();
        IsConnected = false;
        _manager.Remove(this);
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        if (!IsConnected)
        {
            return;
        }

        IsFullScreen = !IsFullScreen;
        _shell.SetFullScreen(IsFullScreen);
    }

    [RelayCommand]
    private void ToggleKeyboardCapture() => IsKeyboardCaptured = IsConnected && !IsKeyboardCaptured;

    [RelayCommand]
    private void ToggleMute() => AudioMuted = !AudioMuted;

    [RelayCommand]
    private async Task SendClipboardAsync()
    {
        try
        {
            var text = ClipboardService.GetText();
            if (text.Length == 0)
            {
                _shell.Notify("Clipboard is empty", "Copy some text first.", NoticeKind.Warning);
                return;
            }

            await _client.SendClipboardTextAsync(text);
            _shell.Notify("Clipboard sent", $"{Name}'s clipboard now has your text.", NoticeKind.Success);
        }
        catch (Exception exception)
        {
            _shell.Notify("Could not send clipboard", exception.Message, NoticeKind.Error);
        }
    }

    [RelayCommand]
    private async Task FetchClipboardAsync()
    {
        try
        {
            await _client.RequestClipboardAsync();
        }
        catch (Exception exception)
        {
            _shell.Notify("Could not fetch clipboard", exception.Message, NoticeKind.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendFile))]
    private async Task SendFileAsync()
    {
        var dialog = new OpenFileDialog { Title = $"Choose a file to send to {Name}" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsTransferring = true;
        TransferProgress = 0;
        try
        {
            var progress = new Progress<double>(value => TransferProgress = value * 100);
            await _client.SendFileAsync(dialog.FileName, progress, CancellationToken.None);
            _shell.Notify("File sent", System.IO.Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception exception)
        {
            _shell.Notify("Could not send file", exception.Message, NoticeKind.Error);
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private bool CanSendFile() => !IsTransferring;

    private void SyncMonitors(IReadOnlyList<RemoteMonitorInfo> monitors)
    {
        _syncingMonitors = true;
        try
        {
            Monitors.Clear();
            foreach (var monitor in monitors)
            {
                Monitors.Add(monitor);
            }

            SelectedMonitor = Monitors.FirstOrDefault(monitor => monitor.IsSelected);
        }
        finally
        {
            _syncingMonitors = false;
        }
    }

    private void ShowStats(SessionStats stats)
    {
        if (!IsActive)
        {
            return; // background sessions are paused; leave the placeholders
        }

        FpsText = $"{stats.FramesPerSecond:0} fps";
        BandwidthText = stats.KilobytesPerSecond >= 1024 ? $"{stats.KilobytesPerSecond / 1024:0.0} MB/s" : $"{stats.KilobytesPerSecond:0} KB/s";
        LatencyText = stats.LatencyMilliseconds switch
        {
            <= 0 => "–",
            < 1 => "<1 ms",
            var ms => $"{ms:0} ms",
        };
    }

    private void OnHostClosed(string reason)
    {
        if (IsFullScreen)
        {
            _shell.SetFullScreen(false);
            IsFullScreen = false;
        }

        IsConnected = false;
        IsKeyboardCaptured = false;
        StatusText = reason;
        FpsText = BandwidthText = LatencyText = "–";
        _shell.Notify($"{Name} disconnected", reason, NoticeKind.Warning);
        _manager.Remove(this);
    }
}
