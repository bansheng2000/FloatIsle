using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media.Animation;
// UseWindowsForms 的隐式 using 会带来 System.Drawing.Size/Point
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FloatIsle;

/// <summary>
/// 位置解析的自检：这套逻辑没法用"再插一块屏"来验（本机只有一块屏），
/// 所以喂一组构造出来的显示器布局，直接比对结果。
/// 用法：<c>FloatIsle.exe --selftest &lt;报告文件&gt;</c>，全过退出码 0，有失败 1。
/// 窗口尺寸从 <see cref="MainWindow"/> 的常量派生（收起 = 岛 + 两侧 8 DIP 描边余量），
/// 期望值是发出去的几何，常量一变这里就会红，不许再钉一个过时的尺寸。
/// </summary>
internal static class PlacementSelfTest
{
    private static readonly Size Collapsed = new(
        MainWindow.CollapsedWidth + 2 * MainWindow.WindowPadding,
        MainWindow.CollapsedHeight + 2 * MainWindow.WindowPadding);

    // 展开态不参与落位：窗口以原中心长大，Resolve 只在启动/复位时用收起尺寸。
    // 这条用例只保证「万一传入展开尺寸，夹紧仍然对」，不是落位契约。
    private static readonly Size Expanded = new(
        MainWindow.ExpandedWidth + 2 * MainWindow.WindowPadding,
        MainWindow.ExpandedHeight + 2 * MainWindow.WindowPadding);

    internal static int Run(string reportPath)
    {
        // 单屏：2560×1600，任务栏 48（与这台机器一致）
        var single = new List<MonitorBox>
        {
            new(@"\\\\.\\DISPLAY1", new Rect(0, 0, 2560, 1600), new Rect(0, 0, 2560, 1552), true),
        };

        // 双屏左右：第二块接在右边
        var sideBySide = new List<MonitorBox>(single)
        {
            new(@"\\\\.\\DISPLAY2", new Rect(2560, 0, 1920, 1080), new Rect(2560, 0, 1920, 1040), false),
        };

        // 双屏上下：第二块在主屏下方
        var stacked = new List<MonitorBox>(single)
        {
            new(@"\\\\.\\DISPLAY2", new Rect(0, 1600, 1920, 1080), new Rect(0, 1600, 1920, 1040), false),
        };

        // 负坐标：第二块在主屏左边
        var negative = new List<MonitorBox>(single)
        {
            new(@"\\\\.\\DISPLAY2", new Rect(-1920, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1040), false),
        };

        // 150% 档按实机读数建模：物理工作区 0,0,2560,1528（任务栏 72 px）。
        // 实测（DPI=144）：收起 RECT=1148,9,1412,99 → 264×90，区域 box=12,12,251,77 → 239×65。
        var single150 = new List<MonitorBox>
        {
            new(@"\\\\.\\DISPLAY1", new Rect(0, 0, 2560, 1600), new Rect(0, 0, 2560, 1528), true),
        };

        Size collapsed150 = new(Collapsed.Width * 1.5, Collapsed.Height * 1.5); // 264×90

        var cases = new List<(string Name, Rect Expected, Rect Actual)>
        {
            // 收起态窗口 176×60：(2560−176)/2 = 1192，顶边 6
            ("单屏默认=顶部居中", new Rect(1192, 6, 176, 60),
                Placement.Resolve(single, null, null, Collapsed, 1.0)),

            ("单屏存过位置原样还原", new Rect(240, 420, 176, 60),
                Placement.Resolve(single, @"\\\\.\\DISPLAY1", new Point(240, 420), Collapsed, 1.0)),

            ("双屏左右：落在第二块屏内不动", new Rect(3000, 100, 176, 60),
                Placement.Resolve(sideBySide, @"\\\\.\\DISPLAY2", new Point(3000, 100), Collapsed, 1.0)),

            // 第二块屏 2560..4480，窗口 176 宽 → 最右 = 4480−176 = 4304
            ("双屏左右：坐标越界夹回第二块屏", new Rect(4304, 100, 176, 60),
                Placement.Resolve(sideBySide, @"\\\\.\\DISPLAY2", new Point(99999, 100), Collapsed, 1.0)),

            ("双屏上下：落在下方屏内不动", new Rect(100, 2000, 176, 60),
                Placement.Resolve(stacked, @"\\\\.\\DISPLAY2", new Point(100, 2000), Collapsed, 1.0)),

            ("负坐标显示器：左屏位置保持为负", new Rect(-1900, 10, 176, 60),
                Placement.Resolve(negative, @"\\\\.\\DISPLAY2", new Point(-1900, 10), Collapsed, 1.0)),

            ("保存的显示器已拔掉：回落主屏顶部居中", new Rect(1192, 6, 176, 60),
                Placement.Resolve(sideBySide, @"\\\\.\\DISPLAY9", new Point(3000, 100), Collapsed, 1.0)),

            ("窗口比工作区大：贴该屏左上角", new Rect(2560, 0, 2000, 1200),
                Placement.Resolve(sideBySide, @"\\\\.\\DISPLAY2", new Point(3000, 100), new Size(2000, 1200), 1.0)),

            // 150% 实机读数：176×60 → 264×90，(2560−264)/2 = 1148，顶边 6×1.5 = 9
            // 与 Windows 实际落位一致（JSON 里就是 LeftPx=1148, TopPx=9，区域 239×65）
            ("150% 缩放：默认位置（实机 1148,9 / 264×90）", new Rect(1148, 9, 264, 90),
                Placement.Resolve(single150, null, null, collapsed150, 1.5)),

            // 构造用例：存过的像素坐标在任何缩放下都不换算（不是实机读数，纯逻辑断言）
            ("150% 缩放：存过位置是像素，不做换算", new Rect(240, 420, 264, 90),
                Placement.Resolve(single150, @"\\\\.\\DISPLAY1", new Point(240, 420), collapsed150, 1.5)),

            // 展开态不参与落位，仅验证夹紧：336×136 居中 → (2560−336)/2 = 1112
            ("展开尺寸传入时夹紧仍正确（仅夹紧，不是落位契约）", new Rect(1112, 6, 336, 136),
                Placement.Resolve(single, null, null, Expanded, 1.0)),

            // 贴顶展开必须留在屏内：上边缘不动、向下长（100% 默认 top=6，有任务行 336×166）
            ("默认贴顶展开不离屏（100%，有任务行）", new Rect(1112, 6, 336, 166),
                Placement.GrowDown(single[0].Work, new Rect(1192, 6, 176, 60), new Size(336, 166))),

            // 150% 实机：收起 (1148,9,264×90) → 有任务行展开 504×249，top 仍为 9（不越出工作区顶）
            ("默认贴顶展开不离屏（150%，有任务行）", new Rect(1028, 9, 504, 249),
                Placement.GrowDown(single150[0].Work, new Rect(1148, 9, 264, 90), new Size(504, 249))),

            // 对称展开：上方空间够时窗口中心不动 → 岛中心也不动（1228,700 收 176×60，中心 1316,730）
            ("对称展开：胶囊中心不变（100%，有任务行）", new Rect(1148, 647, 336, 166),
                Placement.ExpandSymmetric(single[0].Work, new Rect(1228, 700, 176, 60), new Size(336, 166)) ?? Rect.Empty),

            // 贴顶（top=0）时上方放不下 → 必须退回「上沿不动、向下长」
            ("贴顶时对称展开放不下，退回向下长", new Rect(1148, 0, 336, 166),
                Placement.ExpandSymmetric(single[0].Work, new Rect(1228, 0, 176, 60), new Size(336, 166))
                ?? Placement.GrowDown(single[0].Work, new Rect(1228, 0, 176, 60), new Size(336, 166))),

            // 贴顶工作台：收起态与胶囊模式同尺寸、同位置（GrowDown 用同一尺寸是恒等变换）
            ("贴顶模式收起态与胶囊模式一致（176×60）", new Rect(1228, 0, 176, 60),
                Placement.GrowDown(single[0].Work, new Rect(1228, 0, 176, 60), Collapsed)),

            // 贴顶工作台展开（100% 实测）：窗口 560×263、上沿 0 不动、x 按胶囊中心居中 → 1036
            ("贴顶工作台展开只向下长（100%，实测 1036,0,560x263）", new Rect(1036, 0, 560, 263),
                Placement.GrowDown(single[0].Work, new Rect(1228, 0, 176, 60), new Size(560, 263))),

            // 150% 同一条：工作区 2560×1528、收起 264×90、目标 840×394.5（面板高 203 → (60+203)×1.5）。
            // 这条是按 1.5 缩放构造的，未在 150% 会话实机跑过 —— 读数出来就换成本机实测值。
            ("贴顶工作台展开只向下长（150%，按 1.5 构造，未实机）", new Rect(860, 9, 840, 394.5),
                Placement.GrowDown(new Rect(0, 0, 2560, 1528), new Rect(1148, 9, 264, 90), new Size(840, 394.5))),
        };

        var log = new StringBuilder();
        int failed = 0;
        log.AppendLine("Placement 自检（坐标单位 = 设备像素）");
        log.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "收起窗口 {0}x{1}，展开窗口 {2}x{3}",
            Collapsed.Width, Collapsed.Height, Expanded.Width, Expanded.Height));
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
        // 弹簧过冲：同一组常量喂给 BackEase，算它在这批动画的最大位移上会过冲多少 DIP
        double maxDelta = Math.Max(
            MainWindow.ExpandedWidth - MainWindow.CollapsedWidth,
            MainWindow.ExpandedTasksHeight - MainWindow.CollapsedHeight);
        double overshoot = SpringOvershoot(maxDelta);
        bool springOk = overshoot <= 4.0;
        if (!springOk)
        {
            failed++;
        }
        log.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} 弹簧过冲 ≤ 4 DIP（最大位移 {1} DIP）  期望 ≤4.00  实际 {2:F2}",
            springOk ? "PASS" : "FAIL", maxDelta, overshoot));

        // ---- 本轮新增：模块顺序 / 自动列数 / 卡片高度 / 设置窗位置判定 ----
        var extra = new List<(string Name, bool Ok, string Expected, string Actual)>();

        // 1. 缺失顺序：按默认顺序补齐（新装用户的路径）
        var orderMissing = new AppSettings();
        orderMissing.Normalize();
        extra.Add(("ModuleOrder 缺失时回落默认顺序", Ids(orderMissing) == string.Join(",", IslandModuleIds.DefaultOrder),
            string.Join(",", IslandModuleIds.DefaultOrder), Ids(orderMissing)));

        // 2. 只写了 ModuleOrder（没有 Modules）：采用它的顺序
        var orderOnly = new AppSettings { ModuleOrder = new List<string> { "tasks", "media", "usage", "quota" } };
        orderOnly.Normalize();
        extra.Add(("只有 ModuleOrder 时采用它的顺序", Ids(orderOnly) == "tasks,media,usage,quota",
            "tasks,media,usage,quota", Ids(orderOnly)));

        // 3. 非法 / 重复 id：过滤、去重、补齐，且不抛
        var badOrder = new AppSettings { ModuleOrder = new List<string> { "tasks", "bogus", "tasks", "quota" } };
        bool badThrew = false;
        try
        {
            badOrder.Normalize();
        }
        catch
        {
            badThrew = true;
        }
        bool badOk = !badThrew
                     && badOrder.Modules.Count == IslandModuleIds.DefaultOrder.Length
                     && badOrder.Modules.Select(m => m.Id).Distinct().Count() == badOrder.Modules.Count
                     && badOrder.Modules[0].Id == "tasks"
                     && badOrder.Modules[1].Id == "quota";
        extra.Add(("非法/重复 id 不抛且顺序有效", badOk, "tasks,quota,usage,media", Ids(badOrder)));

        // 4. 自动列数：宽 → 3 列、中 → 2 列、窄 → 1 列，且面板宽不超过工作区一半
        var wide = Placement.WorkbenchLayout(2000, 3, 200);
        var medium = Placement.WorkbenchLayout(1200, 3, 200);
        var narrow = Placement.WorkbenchLayout(500, 3, 200);
        extra.Add(("自动列数：宽 2000 → 3 列", wide.Columns == 3, "3", wide.Columns.ToString(CultureInfo.InvariantCulture)));
        extra.Add(("自动列数：宽 1200 → 2 列", medium.Columns == 2, "2", medium.Columns.ToString(CultureInfo.InvariantCulture)));
        extra.Add(("自动列数：宽 500 → 1 列", narrow.Columns == 1, "1", narrow.Columns.ToString(CultureInfo.InvariantCulture)));
        bool halfOk = wide.PanelWidth <= 2000 / 2.0 + 0.5
                      && medium.PanelWidth <= 1200 / 2.0 + 0.5
                      && narrow.PanelWidth <= 500 / 2.0 + 0.5;
        extra.Add(("面板宽 ≤ 工作区一半", halfOk, "≤1000/≤600/≤250",
            $"{wide.PanelWidth:F0}/{medium.PanelWidth:F0}/{narrow.PanelWidth:F0}"));
        extra.Add(("1 列时卡片不拉成全宽", narrow.CardWidth <= narrow.PanelWidth - 2 * Placement.PanelPadding,
            $"≤{narrow.PanelWidth - 2 * Placement.PanelPadding:F0}", $"{narrow.CardWidth:F0}"));

        // 5. 卡片高度必须随内容（Height=NaN、MinHeight=0），不许拿固定高度撑空气
        System.Windows.Controls.Border tile = MainWindow.Tile("今日用量", "1.2 亿 token", "3 次调用");
        bool autoHeight = double.IsNaN(tile.Height) && tile.MinHeight == 0;
        extra.Add(("卡片高度随内容（Height=NaN、MinHeight=0）", autoHeight, "NaN/0",
            $"{tile.Height}/{tile.MinHeight}"));

        // 6. 设置窗位置：屏内可见、屏外回落（记忆位置在别的显示器上时不能开在屏幕外）
        var monitors = new List<MonitorBox>
        {
            new(@"\\.\DISPLAY1", new Rect(0, 0, 2560, 1600), new Rect(0, 0, 2560, 1552), true)
        };
        bool onScreen = Placement.IsVisibleOnSomeMonitor(monitors, new Rect(200, 200, 400, 200));
        bool offScreen = Placement.IsVisibleOnSomeMonitor(monitors, new Rect(4000, 200, 400, 200));
        extra.Add(("设置窗记忆位置：屏内保留", onScreen, "True", onScreen.ToString()));
        extra.Add(("设置窗记忆位置：屏外回落居中", !offScreen, "False", offScreen.ToString()));

        // 7. 拖拽换序：可见顺序 = 给定顺序，隐藏模块留在原位
        var drag = new AppSettings
        {
            Modules = new List<IslandModule>
            {
                new() { Id = "usage" },
                new() { Id = "quota", Visible = false },
                new() { Id = "media" },
                new() { Id = "tasks" }
            }
        };
        drag.Normalize();
        drag.ReorderVisible(new[] { "tasks", "media", "usage" });
        string visibleOrder = string.Join(",", drag.Modules.Where(m => m.Visible).Select(m => m.Id));
        extra.Add(("拖拽换序后可见顺序 = 给定顺序", visibleOrder == "tasks,media,usage", "tasks,media,usage", visibleOrder));
        extra.Add(("拖拽换序后隐藏模块位置不变", drag.Modules[1].Id == "quota" && !drag.Modules[1].Visible,
            "quota@1(隐藏)", $"{drag.Modules[1].Id}@1"));

        // 用户报过「鼠标没碰到胶囊就把工作台打开了」：碰顶唤醒那条链已整条删除，
        // 这里钉住它不许回来——没有 OnTopWake 方法、也没有 TopEdgeHook 类型。
        bool noWakeMethod = typeof(MainWindow).GetMethod("OnTopWake",
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is null;
        bool noHookType = typeof(MainWindow).Assembly.GetType("FloatIsle.TopEdgeHook") is null;
        extra.Add(("贴顶展开不靠碰顶唤醒（无 OnTopWake）", noWakeMethod, "不存在", noWakeMethod ? "不存在" : "仍存在"));
        extra.Add(("贴顶展开不靠碰顶唤醒（无 TopEdgeHook）", noHookType, "不存在", noHookType ? "不存在" : "仍存在"));

        // 用户要求「按会话记账」：完成的会话显示「已完成」，点进去才清，不自己消失
        var done = new DoneLedger();
        var t0 = new DateTime(2026, 11, 5, 16, 0, 0);
        done.Mark("s1", "FloatIsle", t0);
        done.Mark("s2", "Chief", t0.AddSeconds(5));
        bool twoRows = done.Count == 2
                       && done.Snapshot().All(r => r.LineText.Contains("已完成", StringComparison.Ordinal))
                       && done.Snapshot()[0].Sid == "s2"; // 最近完成的在最上面
        extra.Add(("两个会话先后完成 → 未查看 2 条，文案含「已完成」", twoRows,
            "2 / 两行含已完成 / s2 在前",
            $"{done.Count} / {string.Join("|", done.Snapshot().Select(r => r.LineText))}"));

        done.Clear("s2");
        bool oneLeft = done.Count == 1 && done.Contains("s1") && !done.Contains("s2");
        extra.Add(("清掉其中一个 → 集合 = 1，另一个仍在", oneLeft, "1 / s1 在 / s2 不在",
            $"{done.Count} / s1={done.Contains("s1")} / s2={done.Contains("s2")}"));

        // 该 sid 重新开跑 → 它自己从集合消失（Apply 里 turn/start 走的就是 DoneLedger.Clear）
        bool restarted = done.Clear("s1") && done.Count == 0;
        extra.Add(("该 sid 重新 turn/start → 它自己从集合消失", restarted, "0", done.Count.ToString()));

        // 辉光优先级：等你/在跑（呼吸）> 未查看的完成（绿常亮）> 不发光
        var greenWhenIdle = MainWindow.DecideGlow(false, true) == MainWindow.GlowPlan.SteadyGreen;
        var blueWhenRunning = MainWindow.DecideGlow(true, true) == MainWindow.GlowPlan.Breath;
        var darkWhenEmpty = MainWindow.DecideGlow(false, false) == MainWindow.GlowPlan.None;
        extra.Add(("集合非空且无在跑 → 绿常亮", greenWhenIdle, "SteadyGreen", MainWindow.DecideGlow(false, true).ToString()));
        extra.Add(("有在跑 → 蓝呼吸（绿不覆盖）", blueWhenRunning, "Breath", MainWindow.DecideGlow(true, true).ToString()));
        extra.Add(("集合清空 → 不发光", darkWhenEmpty, "None", MainWindow.DecideGlow(false, false).ToString()));

        foreach ((string name, bool ok, string expected, string actual) in extra)
        {
            if (!ok)
            {
                failed++;
            }
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} {1}  期望 {2}  实际 {3}", ok ? "PASS" : "FAIL", name, expected, actual));
        }

        int total = cases.Count + 1 + extra.Count;
        log.AppendLine(failed == 0 ? $"ALL PASS ({total})" : $"FAILED {failed}/{total}");

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


    /// <summary>把模块顺序压成一行，报告里好比对。</summary>
    private static string Ids(AppSettings settings) =>
        string.Join(",", settings.Modules.Select(m => m.Id));

    /// <summary>把 BackEase 的最大值找出来，换算成"在这段位移上过冲了几个 DIP"。</summary>
    private static double SpringOvershoot(double delta)
    {
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = MainWindow.SpringAmplitude };
        double max = 0;
        for (int i = 0; i <= 1000; i++)
        {
            double v = ease.Ease(i / 1000.0);
            if (v > max)
            {
                max = v;
            }
        }
        return (max - 1.0) * delta;
    }

    private static string Fmt(Rect r) => string.Format(CultureInfo.InvariantCulture,
        "({0},{1},{2}x{3})", r.Left, r.Top, r.Width, r.Height);

    private static bool Close(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) < 0.01 && Math.Abs(a.Top - b.Top) < 0.01
        && Math.Abs(a.Width - b.Width) < 0.01 && Math.Abs(a.Height - b.Height) < 0.01;
}
