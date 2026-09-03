using System.Collections.ObjectModel;
using Ducz.LocalConnect.App.ViewModels;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.App.Services;

public sealed class SessionManager
{
    private readonly SettingsStore _settings;
    private readonly IShell _shell;

    public SessionManager(SettingsStore settings, IShell shell)
    {
        _settings = settings;
        _shell = shell;
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    public SessionViewModel? Active { get; private set; }

    public IReadOnlyList<PinnedConnection> Pins => _settings.Current.Pins;

    public event Action<SessionViewModel>? SessionAdded;

    public event Action<SessionViewModel>? SessionRemoved;

    public event Action<SessionViewModel?>? ActiveChanged;

    public event Action<PinnedConnection, SessionViewModel>? PinAdded;

    public event Action<PinnedConnection, SessionViewModel?>? PinRemoved;

    public bool HasSessions => Sessions.Count > 0;

    public SessionViewModel Create(string name)
        => new(new ClientSession(), _settings, _shell, this, name);

    public void Add(SessionViewModel session)
    {
        Sessions.Add(session);
        SessionAdded?.Invoke(session);
        SetActive(session);
    }

    public void SetActive(SessionViewModel? session)
    {
        if (ReferenceEquals(Active, session))
        {
            session?.SetActive(true);
            return;
        }

        Active?.SetActive(false);
        Active = session;
        session?.SetActive(true);
        ActiveChanged?.Invoke(session);
    }

    public void Remove(SessionViewModel session)
    {
        var index = Sessions.IndexOf(session);
        if (index < 0)
        {
            return;
        }

        Sessions.Remove(session);
        SessionRemoved?.Invoke(session);

        if (!ReferenceEquals(Active, session))
        {
            return;
        }

        var next = Sessions.Count > 0 ? Sessions[Math.Min(index, Sessions.Count - 1)] : null;
        SetActive(next);
        if (next is null)
        {
            _shell.Navigate(PageTags.Connect);
        }
    }

    public SessionViewModel? FindByPin(string pinId)
        => Sessions.FirstOrDefault(session => session.PinId == pinId);

    public void Pin(SessionViewModel session)
    {
        if (session.IsPinned)
        {
            return;
        }

        var pin = new PinnedConnection
        {
            Name = session.Name,
            Host = session.Host,
            Port = session.Port,
            Pin = session.Pin,
        };
        _settings.Current.Pins.Add(pin);
        _settings.Save();

        session.PinId = pin.Id;
        PinAdded?.Invoke(pin, session);
    }

    public void Unpin(SessionViewModel session)
    {
        if (session.PinId is not { } pinId)
        {
            return;
        }

        var pin = _settings.Current.Pins.FirstOrDefault(entry => entry.Id == pinId);
        session.PinId = null;
        if (pin is not null && _settings.Current.Pins.Remove(pin))
        {
            _settings.Save();
            PinRemoved?.Invoke(pin, session);
        }
    }

    public void RemovePin(PinnedConnection pin)
    {
        var live = FindByPin(pin.Id);
        if (live is not null)
        {
            live.PinId = null;
        }

        if (_settings.Current.Pins.Remove(pin))
        {
            _settings.Save();
            PinRemoved?.Invoke(pin, live);
        }
    }

    public void UpdatePin(PinnedConnection pin, string name, string host, int port, string pinCode)
    {
        pin.Name = name;
        pin.Host = host;
        pin.Port = port;
        pin.Pin = pinCode;
        _settings.Save();

        var live = FindByPin(pin.Id);
        if (live is not null)
        {
            live.SetConnection(host, port, pinCode);
            live.Name = name; // also refreshes the sidebar item and re-saves the pin
        }
    }

    public void SyncPin(SessionViewModel session)
    {
        var pin = _settings.Current.Pins.FirstOrDefault(entry => entry.Id == session.PinId);
        if (pin is null)
        {
            return;
        }

        pin.Name = session.Name;
        pin.Host = session.Host;
        pin.Port = session.Port;
        pin.Pin = session.Pin;
        _settings.Save();
    }

    public async Task<SessionViewModel?> ConnectPinAsync(PinnedConnection pin)
    {
        var existing = FindByPin(pin.Id);
        if (existing is not null)
        {
            SetActive(existing);
            return existing;
        }

        var session = Create(pin.Name);
        session.PinId = pin.Id;
        session.SetConnection(pin.Host, pin.Port, pin.Pin);

        try
        {
            await session.Session.ConnectAsync(new ClientOptions(pin.Host, pin.Port, pin.Pin), CancellationToken.None);
            session.OnConnected();
            Add(session);
            return session;
        }
        catch (Exception exception)
        {
            _shell.Notify($"Could not reconnect to {pin.Name}", exception.Message, NoticeKind.Error);
            return null;
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var session in Sessions.ToArray())
        {
            await session.Session.DisconnectAsync();
        }
    }
}
