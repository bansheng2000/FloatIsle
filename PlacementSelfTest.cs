using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
// UseWindowsForms 的隐式 using 会带来 System.Drawing.Size/Point
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FloatIsle;

/// <summary>
/// 位置解析的自检：这套逻辑没法用"再插一块屏"来验（本机只有一块屏），
/// 所以喂一组构造出来的显示器布局，直接比对结果。
/// 用法：<c>FloatIsle.exe --selftest &lt;报告文件&gt;</c>，全过退出码 0，有失败 1。
/// </summary>
internal static class PlacementSelfTest
{
    private static readonly Size Window = new(380, 180);

    internal static int Run(string reportPath)
    {
        // 单屏：2560×1600，任务栏 48（与这台机器一致）
        var single = new List<MonitorBox>
        {
            new(@"\\.\DISPLAY1", new Rect(0, 0, 2560, 1600), new Rect(0, 0, 2560, 1552), true),
        };

        // 双屏左右：第二块接在右边
        var sideBySide = new List<MonitorBox>(single)
        {
            new(@"\\.\DISPLAY2", new Rect(2560, 0, 1920, 1080), new Rect(2560, 0, 1920, 1040), false),
        };

        // 双屏上下：第二块在主屏下方
        var stacked = new List<MonitorBox>(single)
        {
            new(@"\\.\DISPLAY2", new Rect(0, 1600, 1920, 1080), new Rect(0, 1600, 1920, 1040), false),
        };

        // 负坐标：第二块在主屏左边
        var negative = new List<MonitorBox>(single)
        {
            new(@"\\.\DISPLAY2", new Rect(-1920, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1040), false),
        };

        var cases = new List<(string Name, Rect Expected, Rect Actual)>
        {
            ("单屏默认=顶部居中", new Rect(1090, 6, 380, 180),
                Placement.Resolve(single, null, null, Window, 1.0)),

            ("单屏存过位置原样还原", new Rect(240, 420, 380, 180),
                Placement.Resolve(single, @"\\.\DISPLAY1", new Point(240, 420), Window, 1.0)),

            ("双屏左右：落在第二块屏内不动", new Rect(3000, 100, 380, 180),
                Placement.Resolve(sideBySide, @"\\.\DISPLAY2", new Point(3000, 100), Window, 1.0)),

            ("双屏左右：坐标越界夹回第二块屏", new Rect(4100, 100, 380, 180),
                Placement.Resolve(sideBySide, @"\\.\DISPLAY2", new Point(99999, 100), Window, 1.0)),

            ("双屏上下：落在下方屏内不动", new Rect(100, 2000, 380, 180),
                Placement.Resolve(stacked, @"\\.\DISPLAY2", new Point(100, 2000), Window, 1.0)),

            ("负坐标显示器：左屏位置保持为负", new Rect(-1900, 10, 380, 180),
                Placement.Resolve(negative, @"\\.\DISPLAY2", new Point(-1900, 10), Window, 1.0)),

            ("保存的显示器已拔掉：回落主屏顶部居中", new Rect(1090, 6, 380, 180),
                Placement.Resolve(sideBySide, @"\\.\DISPLAY9", new Point(3000, 100), Window, 1.0)),

            ("窗口比工作区大：贴该屏左上角", new Rect(2560, 0, 2000, 1200),
                Placement.Resolve(sideBySide, @"\\.\DISPLAY2", new Point(3000, 100), new Size(2000, 1200), 1.0)),

            ("150% 缩放：默认位置按缩放派生", new Rect(995, 9, 570, 270),
                Placement.Resolve(single, null, null, new Size(570, 270), 1.5)),

            ("150% 缩放：存过位置是像素，不做换算", new Rect(240, 420, 570, 270),
                Placement.Resolve(single, @"\\.\DISPLAY1", new Point(240, 420), new Size(570, 270), 1.5)),
        };

        var log = new StringBuilder();
        int failed = 0;
        log.AppendLine("Placement 自检（坐标单位 = 设备像素）");
        foreach ((string name, Rect expected, Rect actual) in cases)
        {
            bool ok = Close(expected, actual);
            if (!ok)
            {
                failed++;
            }
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} {1}  期望 {2}  实际 {3}", ok ? "PASS" : "FAIL", name, Fmt(expected), Fmt(actual)));
        }
        log.AppendLine(failed == 0 ? $"ALL PASS ({cases.Count})" : $"FAILED {failed}/{cases.Count}");

        try
        {
            File.WriteAllText(reportPath, log.ToString());
        }
        catch
        {
            // 报告写不出去也要让退出码说话
        }
        return failed == 0 ? 0 : 1;
    }

    private static string Fmt(Rect r) => string.Format(CultureInfo.InvariantCulture,
        "({0},{1},{2}x{3})", r.Left, r.Top, r.Width, r.Height);

    private static bool Close(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) < 0.01 && Math.Abs(a.Top - b.Top) < 0.01
        && Math.Abs(a.Width - b.Width) < 0.01 && Math.Abs(a.Height - b.Height) < 0.01;
}
