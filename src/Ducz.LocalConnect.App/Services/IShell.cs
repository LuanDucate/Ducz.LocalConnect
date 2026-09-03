namespace Ducz.LocalConnect.App.Services;

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error,
}

public interface IShell
{
    void Navigate(string pageTag);

    void Notify(string title, string message, NoticeKind kind = NoticeKind.Info);

    Task ShowMessageAsync(string title, string message);

    bool IsFullScreen { get; }

    void SetFullScreen(bool enabled);
}

public static class PageTags
{
    public const string Host = "host";
    public const string Connect = "connect";
    public const string Session = "session";
    public const string Settings = "settings";
    public const string About = "about";
}
