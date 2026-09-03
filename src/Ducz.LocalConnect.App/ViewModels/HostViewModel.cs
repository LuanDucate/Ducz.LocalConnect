using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.Core.Clipboard;
using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.App.ViewModels;

public sealed record LogEntry(DateTime Time, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

public partial class HostViewModel : ObservableObject
{
    private const int MaxLogEntries = 200;

    private readonly HostServer _host;
    private readonly SettingsStore _settings;
    private readonly IShell _shell;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioPort))]
    private int _port;

    [ObservableProperty]
    private string _pin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(CanEditSettings))]
    [NotifyPropertyChangedFor(nameof(StateTitle))]
    [NotifyPropertyChangedFor(nameof(StateDescription))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private HostState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateDescription))]
    private string? _clientEndpoint;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _startWithWindows;

    public HostViewModel(HostServer host, SettingsStore settings, IShell shell)
    {
        _host = host;
        _settings = settings;
        _shell = shell;

        _port = settings.Current.HostPort;
        _pin = settings.Current.HostPin;
        _startWithWindows = settings.Current.StartHostWithWindows;
        _state = host.State;

        host.StateChanged += state => UiThread.Run(() => State = state);
        host.ClientConnected += endpoint => UiThread.Run(() =>
        {
            ClientEndpoint = FormatEndpoint(endpoint);
            _shell.Notify("Client connected", $"{ClientEndpoint} is now viewing this screen.", NoticeKind.Success);
        });
        host.ClientDisconnected += () => UiThread.Run(() =>
        {
            var who = ClientEndpoint ?? "The client";
            ClientEndpoint = null;
            _shell.Notify("Client disconnected", $"{who} left the session.", NoticeKind.Warning);
        });
        host.Log += message => UiThread.Run(() => AppendLog(message));

        RefreshAddresses();
    }

    public ObservableCollection<LocalNetwork.Address> Addresses { get; } = new();

    public ObservableCollection<LogEntry> Log { get; } = new();

    public int MinPort => ProtocolLimits.MinPort;

    public int MaxPort => ProtocolLimits.MaxPort;

    public int AudioPort => Port + ProtocolLimits.AudioPortOffset;

    public bool IsRunning => State != HostState.Stopped;

    public bool CanEditSettings => !IsRunning;

    public string StateTitle => State switch
    {
        HostState.Listening => "Waiting for a client",
        HostState.Connected => "Client connected",
        _ => "Host is off",
    };

    public string StateDescription => State switch
    {
        HostState.Listening => $"Share one of the addresses below and the PIN. Listening on port {Port}, audio on {AudioPort}.",
        HostState.Connected => $"{ClientEndpoint ?? "A client"} is viewing this screen.",
        _ => "Start the host to let another computer on your network view and control this one.",
    };

    public string ReceivedFilesDirectory => _settings.Current.ReceivedFilesDirectory;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var pin = Pin.Trim();
        if (pin.Length == 0)
        {
            _shell.Notify("PIN required", "Set a PIN before starting the host.", NoticeKind.Warning);
            return;
        }

        try
        {
            _host.Start(new HostOptions(Port, pin, _settings.Current.ReceivedFilesDirectory));
            _settings.Current.HostPort = Port;
            _settings.Current.HostPin = pin;
            _settings.Save();
        }
        catch (SocketException)
        {
            _shell.Notify("Port in use", $"Ports {Port} and {AudioPort} must both be free. Pick another port.", NoticeKind.Error);
        }
        catch (Exception exception)
        {
            _shell.Notify("Could not start host", exception.Message, NoticeKind.Error);
        }
    }

    private bool CanStart() => !IsRunning;

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settings.Current.StartHostWithWindows = value;
        // Persist the current PIN/port so the login-time host uses what's on screen now.
        _settings.Current.HostPin = Pin.Trim().Length == 0 ? _settings.Current.HostPin : Pin.Trim();
        _settings.Current.HostPort = Port;
        _settings.Save();

        try
        {
            WindowsStartup.Sync(value);
            if (value)
            {
                _shell.Notify("Start with Windows on", "The app will launch at login and start the host with this PIN, minimized to the tray.", NoticeKind.Success);
            }
        }
        catch (Exception exception)
        {
            _shell.Notify("Could not update Windows startup", exception.Message, NoticeKind.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        IsBusy = true;
        try
        {
            await _host.StopAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private void GeneratePin() => Pin = AppSettings.GeneratePin();

    [RelayCommand]
    private void RefreshAddresses()
    {
        Addresses.Clear();
        foreach (var address in LocalNetwork.GetIPv4Addresses())
        {
            Addresses.Add(address);
        }
    }

    [RelayCommand]
    private void CopyAddress(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            return;
        }

        try
        {
            ClipboardService.SetText(ip);
            _shell.Notify("Copied", $"{ip} copied to the clipboard.", NoticeKind.Success);
        }
        catch (Exception exception)
        {
            _shell.Notify("Clipboard busy", exception.Message, NoticeKind.Warning);
        }
    }

    [RelayCommand]
    private void OpenReceivedFolder()
    {
        var directory = _settings.Current.ReceivedFilesDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private void AppendLog(string message)
    {
        Log.Insert(0, new LogEntry(DateTime.Now, message));
        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    private static string? FormatEndpoint(EndPoint? endpoint) => endpoint switch
    {
        IPEndPoint ip => ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4().ToString() : ip.Address.ToString(),
        null => null,
        _ => endpoint.ToString(),
    };
}
