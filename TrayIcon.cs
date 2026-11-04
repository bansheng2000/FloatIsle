using System;
using System.Windows.Forms;

namespace FloatIsle;

/// <summary>
/// 托盘图标与菜单：显示/隐藏岛、点击穿透、开机自启、退出。
/// 只用框架自带的 WinForms NotifyIcon，不引 NuGet。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _visibilityItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private bool _disposed;

    internal TrayIcon(MainWindow window)
    {
        _window = window;

        _visibilityItem = new ToolStripMenuItem("隐藏岛", null, (_, _) =>
        {
            window.ToggleVisibility();
            Refresh();
        });

        _clickThroughItem = new ToolStripMenuItem("点击穿透", null, (_, _) =>
        {
            window.ToggleClickThrough();
            Refresh();
        });

        _autoStartItem = new ToolStripMenuItem("开机自启", null, (_, _) =>
        {
            AutoStart.SetEnabled(!AutoStart.IsEnabled());
            Refresh();
        });

        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => System.Windows.Application.Current.Shutdown());

        // 设置窗口是普通窗口（有焦点、可拖动），不是岛那种置顶不抢焦点的
        var settingsItem = new ToolStripMenuItem("设置…", null, (_, _) => window.ShowSettings());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_visibilityItem);
        menu.Items.Add(_clickThroughItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(exitItem);

        _notify = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "FloatIsle",
            Visible = true,
            ContextMenuStrip = menu,
        };

        window.ClickThroughChanged += (_, _) => Refresh();
        window.IsVisibleChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>把菜单勾选状态同步成窗口的真实状态（热键改穿透时也要走这里）。</summary>
    internal void Refresh()
    {
        _visibilityItem.Text = _window.IsVisible ? "隐藏岛" : "显示岛";
        _clickThroughItem.Checked = _window.ClickThrough;
        _autoStartItem.Checked = AutoStart.IsEnabled();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // 先撤图标再释放，否则进程退出后托盘里会留一个"幽灵图标"
        _notify.Visible = false;
        _notify.Dispose();
    }
}
