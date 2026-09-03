using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace Ducz.LocalConnect.App.Services;
public sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly NotifyIcon _icon;

    public TrayService(Window window)
    {
        _window = window;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Ducz LocalConnect", null, (_, _) => Restore());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _window.Close());

        _icon = new NotifyIcon
        {
            Text = "Ducz LocalConnect",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                Restore();
            }
        };
    }

    public bool IsInTray => _icon.Visible;

    public void HideToTray()
    {
        _icon.Visible = true;
        _window.Hide();
    }

    public void Restore()
    {
        _icon.Visible = false;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Notify(string title, string message, NoticeKind kind)
    {
        if (!IsInTray)
        {
            return;
        }

        var icon = kind switch
        {
            NoticeKind.Warning => ToolTipIcon.Warning,
            NoticeKind.Error => ToolTipIcon.Error,
            _ => ToolTipIcon.Info,
        };
        _icon.ShowBalloonTip(4000, title, message, icon);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream);
    }
}
