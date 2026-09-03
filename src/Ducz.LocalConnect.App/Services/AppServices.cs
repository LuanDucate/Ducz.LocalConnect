using Ducz.LocalConnect.App.ViewModels;
using Ducz.LocalConnect.Core.Sessions;

namespace Ducz.LocalConnect.App.Services;

public static class AppServices
{
    public static IShell Shell { get; set; } = new NoShell();

    private static readonly Lazy<SettingsStore> SettingsLazy = new(() => new SettingsStore());
    private static readonly Lazy<HostServer> HostLazy = new(() => new HostServer());
    private static readonly Lazy<SessionManager> SessionsLazy = new(() => new SessionManager(Settings, Shell));
    private static readonly Lazy<HostViewModel> HostViewModelLazy = new(() => new HostViewModel(Host, Settings, Shell));
    private static readonly Lazy<ConnectViewModel> ConnectViewModelLazy = new(() => new ConnectViewModel(Sessions, Settings, Shell));
    private static readonly Lazy<SettingsViewModel> SettingsViewModelLazy = new(() => new SettingsViewModel(Settings, Shell));
    private static readonly Lazy<AboutViewModel> AboutViewModelLazy = new(() => new AboutViewModel());

    public static SettingsStore Settings => SettingsLazy.Value;

    public static HostServer Host => HostLazy.Value;

    public static SessionManager Sessions => SessionsLazy.Value;

    public static HostViewModel HostViewModel => HostViewModelLazy.Value;

    public static ConnectViewModel ConnectViewModel => ConnectViewModelLazy.Value;

    public static SettingsViewModel SettingsViewModel => SettingsViewModelLazy.Value;

    public static AboutViewModel AboutViewModel => AboutViewModelLazy.Value;

    public static async Task ShutdownAsync()
    {
        if (SessionsLazy.IsValueCreated)
        {
            await Sessions.DisconnectAllAsync();
        }

        if (HostLazy.IsValueCreated)
        {
            await Host.StopAsync();
        }

        if (SettingsLazy.IsValueCreated)
        {
            Settings.Save();
        }
    }

    private sealed class NoShell : IShell
    {
        public bool IsFullScreen => false;

        public void Navigate(string pageTag)
        {
        }

        public void Notify(string title, string message, NoticeKind kind = NoticeKind.Info)
        {
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public void SetFullScreen(bool enabled)
        {
        }
    }
}
