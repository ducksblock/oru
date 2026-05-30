using System.Drawing;
using Forms = System.Windows.Forms;

namespace oru.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly Icon? _customIcon;

    public TrayIconService()
    {
        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        Icon icon = SystemIcons.Application;
        try
        {
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "oru.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _customIcon = new Icon(iconPath, 16, 16);
                icon = _customIcon;
            }
        }
        catch
        {
            // Fallback silently to default icon
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "oru",
            Icon = icon,
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.MouseClick += OnMouseClick;
    }

    public event Action? TrayActivated;

    public event Action? ExitRequested;

    public void Dispose()
    {
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _customIcon?.Dispose();
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            TrayActivated?.Invoke();
        }
    }
}
