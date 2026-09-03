using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.Core.Protocol;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.App.ViewModels;

public partial class ConnectViewModel : ObservableObject
{
    private readonly SessionManager _manager;
    private readonly SettingsStore _settings;
    private readonly IShell _shell;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _host;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private string _sessionName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public ConnectViewModel(SessionManager manager, SettingsStore settings, IShell shell)
    {
        _manager = manager;
        _settings = settings;
        _shell = shell;

        _host = settings.Current.LastHost;
        _port = settings.Current.LastPort;
        RecentHosts = new ObservableCollection<string>(settings.Current.RecentHosts);
    }

    public ObservableCollection<string> RecentHosts { get; }

    public int MinPort => ProtocolLimits.MinPort;

    public int MaxPort => ProtocolLimits.MaxPort;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        ErrorMessage = null;
        IsConnecting = true;

        try
        {
            var host = Host.Trim();
            var options = new ClientOptions(host, Port, Pin.Trim());
            var name = string.IsNullOrWhiteSpace(SessionName) ? host : SessionName.Trim();

            var session = _manager.Create(name);
            session.SetConnection(host, Port, options.Pin);
            await session.Session.ConnectAsync(options, CancellationToken.None);
            session.OnConnected();

            _settings.Current.RememberHost(host);
            _settings.Current.LastPort = Port;
            _settings.Save();
            SyncRecentHosts();
            SessionName = string.Empty;

            _manager.Add(session);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect() => !IsConnecting && !string.IsNullOrWhiteSpace(Host);

    [RelayCommand]
    private void UseRecentHost(string? host)
    {
        if (!string.IsNullOrEmpty(host))
        {
            Host = host;
        }
    }

    [RelayCommand]
    private void ForgetRecentHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return;
        }

        _settings.Current.RecentHosts.RemoveAll(entry => string.Equals(entry, host, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        SyncRecentHosts();
    }

    private void SyncRecentHosts()
    {
        RecentHosts.Clear();
        foreach (var host in _settings.Current.RecentHosts)
        {
            RecentHosts.Add(host);
        }
    }
}
