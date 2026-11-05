using System;
using System.Collections.Generic;
using System.Windows;
// UseWindowsForms 会隐式引入 System.Drawing，把 Point/Size 弄成二义；这里钉死用 WPF 的
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FloatIsle;

/// <summary>一块显示器：设备名 + 整块区域 + 工作区。<b>坐标全是设备像素</b>（不是 DIP）。</summary>
internal readonly record struct MonitorBox(string DeviceName, Rect Bounds, Rect Work, bool IsPrimary);

/// <summary>
/// 位置解析：显示器列表 + 存过的位置 → 窗口该落的像素矩形。
/// 纯函数，不碰窗口/系统 API，靠 <see cref="PlacementSelfTest"/> 覆盖各种布局。
/// 真双屏没在这台机器上测过（只有一块屏），README「已知限制」写明了。
/// </summary>
internal static class Placement
{
    /// <summary>默认位置距工作区顶部的距离（DIP），由调用方按当前显示器缩放换成像素。</summary>
    internal const double TopMarginDip = 6;

    /// <summary>当前显示器列表。PerMonitorV2 进程里 WinForms Screen 报的是物理像素。</summary>
    internal static IReadOnlyList<MonitorBox> CurrentMonitors()
    {
        var list = new List<MonitorBox>();
        foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
        {
            System.Drawing.Rectangle b = screen.Bounds;
            System.Drawing.Rectangle w = screen.WorkingArea;
            list.Add(new MonitorBox(
                screen.DeviceName,
                new Rect(b.X, b.Y, b.Width, b.Height),
                new Rect(w.X, w.Y, w.Width, w.Height),
                screen.Primary));
        }
        return list;
    }

    /// <summary>
    /// 存过位置且同名显示器还在 → 夹进<b>该显示器</b>的工作区；
    /// 显示器没了 / 没存过 → 主屏顶部居中。
    /// </summary>
    internal static Rect Resolve(
        IReadOnlyList<MonitorBox> monitors, string? savedDevice, Point? savedPos,
        Size windowPx, double scale)
    {
        if (savedPos is Point pos)
        {
            foreach (MonitorBox m in monitors)
            {
                if (string.Equals(m.DeviceName, savedDevice, StringComparison.OrdinalIgnoreCase))
                {
                    return ClampInto(m.Work, pos, windowPx);
                }
            }
        }

        return DefaultRect(Primary(monitors), windowPx, scale);
    }

    /// <summary>主屏顶部居中（顶部留 <see cref="TopMarginDip"/> DIP）。</summary>
    internal static Rect DefaultRect(MonitorBox monitor, Size windowPx, double scale)
    {
        Rect work = monitor.Work;
        double x = work.Left + Math.Max(0, (work.Width - windowPx.Width) / 2);
        double y = work.Top + TopMarginDip * scale;
        return ClampInto(work, new Point(x, y), windowPx);
    }

    /// <summary>把窗口矩形夹进给定工作区；窗口比工作区还大时贴左上角，不留负坐标。</summary>
    internal static Rect ClampInto(Rect work, Point pos, Size size)
    {
        double maxX = Math.Max(work.Left, work.Right - size.Width);
        double maxY = Math.Max(work.Top, work.Bottom - size.Height);
        return new Rect(
            Math.Clamp(pos.X, work.Left, maxX),
            Math.Clamp(pos.Y, work.Top, maxY),
            size.Width,
            size.Height);
    }

    /// <summary>
    /// 展开/收起时摆窗口：<b>上边缘不动、向下长</b>，左右仍围绕原中心；
    /// 长出去超出工作区才用 <see cref="ClampInto"/> 夹回来（面板比工作区高时贴顶）。
    /// 旧做法是保持窗口中心，贴顶 6 DIP 时展开会把面板顶出屏幕上沿。
    /// </summary>
    internal static Rect GrowDown(Rect work, Rect current, Size target)
    {
        double x = current.Left + (current.Width - target.Width) / 2.0;
        return ClampInto(work, new Point(x, current.Top), target);
    }

    /// <summary>
    /// 对称展开：窗口中心不动（岛本体在窗口里居中，所以岛中心也不动），四向各扩一半。
    /// 放不进工作区就返回 null，让调用方退回 <see cref="GrowDown"/>（贴顶时上沿不动）。
    /// </summary>
    internal static Rect? ExpandSymmetric(Rect work, Rect current, Size target)
    {
        double x = current.Left + (current.Width - target.Width) / 2.0;
        double y = current.Top + (current.Height - target.Height) / 2.0;
        if (x < work.Left - 0.5 || y < work.Top - 0.5 ||
            x + target.Width > work.Right + 0.5 || y + target.Height > work.Bottom + 0.5)
        {
            return null;
        }
        return new Rect(x, y, target.Width, target.Height);
    }

    /// <summary>主屏；列表为空时给个 0,0 的假显示器（不该发生，但别让调用方空引用）。</summary>
    internal static MonitorBox Primary(IReadOnlyList<MonitorBox> monitors)
    {
        foreach (MonitorBox m in monitors)
        {
            if (m.IsPrimary)
            {
                return m;
            }
        }
        return monitors.Count > 0 ? monitors[0] : new MonitorBox(@"\\.\DISPLAY1", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), true);
    }
}
