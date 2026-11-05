using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FloatIsle;

/// <summary>
/// 托盘图标与菜单：显示/隐藏岛、点击穿透、开机自启、退出。
/// 只用框架自带的 WinForms NotifyIcon，不引 NuGet。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    // csproj 里 <EmbeddedResource Include="assets\floatisle.ico" /> 的默认逻辑名
    private const string IconResource = "FloatIsle.assets.floatisle.ico";

    private readonly MainWindow _window;
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _visibilityItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private Icon? _appIcon;
    private int _iconSize;
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
            Text = "FloatIsle",
            Visible = true,
            ContextMenuStrip = menu,
        };
        // 先按系统 DPI 落一次，句柄就绪后由 MainWindow 用窗口 DPI 校正
        UpdateIconSize(96);

        window.ClickThroughChanged += (_, _) => Refresh();
        window.IsVisibleChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>
    /// 按 DPI 选 ico 档位：托盘小图标 = 16 DIP × 缩放（100% → 16 px，150% → 24 px）。
    /// 传窗口 DPI（`GetDpiForWindow`），`WM_DPICHANGED` 后要再调一次，否则缩放变了图标还是旧档。
    /// </summary>
    internal void UpdateIconSize(int dpi)
    {
        if (_disposed)
        {
            return;
        }
        if (dpi <= 0)
        {
            dpi = 96;
        }
        int size = Math.Clamp((int)Math.Round(16.0 * dpi / 96.0), 16, 256);
        if (size == _iconSize)
        {
            return;
        }

        Icon? icon = LoadIcon(size);
        if (icon is null)
        {
            return; // 资源缺失时保留上一个图标/系统默认，不抛
        }

        _notify.Icon = icon;
        _appIcon?.Dispose();
        _appIcon = icon;
        _iconSize = size;
    }

    /// <summary>从嵌入资源流里按目标尺寸挑档（`new Icon(stream)` 只取默认档，150% 下会糊）。</summary>
    private static Icon? LoadIcon(int size)
    {
        using Stream? stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResource);
        return stream is null ? null : new Icon(stream, size, size);
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
        _appIcon?.Dispose();
    }
}
