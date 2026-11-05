using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
// UseWindowsForms 的隐式 using 会带来 System.Drawing.Size/Point，钉死用 WPF 的
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FloatIsle;

public partial class MainWindow : Window
{
    // RegisterHotKey 的 id，同一进程内唯一即可
    private const int HotKeyId = 0x4F49;

    internal const double CollapsedWidth = 160;
    internal const double CollapsedHeight = 44;
    internal const double ExpandedWidth = 320;
    internal const double ExpandedHeight = 120;

    /// <summary>展开态有任务行时的高（最多 4 行 + 一行「还有 N 个」，120 DIP 装不下）。</summary>
    internal const double ExpandedTasksHeight = 150;

    /// <summary>任务区最多显示几行，多出来的收成「还有 N 个」。</summary>
    private const int MaxTaskRows = 4;

    /// <summary>窗口只在岛上内容外面留 8 DIP：1px 描边 + 圆角抗锯齿，不再合成整片透明空气。</summary>
    internal const double WindowPadding = 8;

    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(180));

    /// <summary>展开/收起的弹簧：200ms + 轻微过冲（selftest 断言过冲 ≤ 4 DIP）。</summary>
    internal static readonly Duration SpringDuration = new(TimeSpan.FromMilliseconds(200));

    /// <summary>BackEase 的幅度：0.19 在 160 DIP 位移上约 3 px 过冲（selftest 卡 4 DIP，0.35 会到 11 px）。</summary>
    internal const double SpringAmplitude = 0.19;

    private readonly AppSettings _settings;
    private readonly string? _settingsBackupPath;
    private readonly FullscreenWatcher _fullscreen;
    private readonly DispatcherTimer _hoverTimer;
    // 收起前的二次确认：自己挪窗/改区域会给一次假 MouseLeave，隔 150ms 看光标是不是真的走了
    private readonly DispatcherTimer _leaveTimer;

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _clickThrough;
    private bool _hotKeyOk;
    private bool _regionTracking;
    private bool _restoringPlacement;
    private TrayIcon? _tray;
    private MediaSession? _media;
    private SettingsWindow? _settingsWindow;
    private bool _hiddenByFullscreen;

    // 媒体快照与账本快照都只存最近一份，岛上文字统一由 RefreshIslandText() 拼
    private MediaInfo _mediaInfo = new(null, null, null);
    private UsageSnapshot? _usage;
    private UsageWatcher? _usageWatcher;
    private bool _expanded;
    private bool _animating; // 展开/收起动画是否在跑（重复的 MouseEnter 不许重启动画，否则岛会上下抽动）

    // 任务面板接收端：心跳/事件驱动，空闲时没有轮询（只有一个一次性超时定时器）
    private TasksHub? _tasks;
    private string? _tasksEndpointApplied;
    private string? _taskRowsKey;

    // 拖拽：WS_EX_NOACTIVATE 的窗口不能 DragMove（它要求窗口被激活，会把焦点抢走），只能自己跟手
    private bool _dragging;

    // 悬停强调：平时 1px 24% 白边，悬停加深到 40%（改 Brush 比重画几何便宜）
    private static readonly Brush HoverBorderBrush = Frozen("#66FFFFFF");
    private Brush? _idleBorderBrush;
    private NativeMethods.POINT _dragCursorStart;
    private Rect _dragWindowStart;

    // 贴顶工作台：钩子只在光标碰顶时投一条消息，回 UI 线程才建面板
    private const double TopPanelWidth = 560;
    private const double TopPanelEdgeMargin = 24;
    private TopEdgeHook? _edgeHook;
    private bool _topOpen;
    private double _topHeight;
    private string? _topTilesKey;
    // 当前已经套用过的形态：设置每改一项都会走 ApplySettings，形态没变就别重摆窗口
    private string? _appliedMode;

    /// <summary>当前是否整窗点击穿透（托盘菜单要读它）。</summary>
    internal bool ClickThrough => _clickThrough;

    /// <summary>设置面板直接改这份对象，改完走 UpdateSettings 生效并落盘。</summary>
    internal AppSettings Settings => _settings;

    /// <summary>设置面板上的账本读数（null = 无数据）。</summary>
    internal UsageSnapshot? Usage => _usage;

    internal string UsagePath => _usageWatcher?.Path ?? UsageLedger.LedgerPath;

    internal bool UsageWatching => _usageWatcher?.IsWatching == true;

    /// <summary>任务面板是否连着（25 秒内有心跳/事件；设置面板的状态栏读的也是这个判定）。</summary>
    internal bool TasksConnectedNow => _tasks?.Connected == true;

    internal IReadOnlyList<TaskRow> TaskList => _tasks?.Snapshot() ?? Array.Empty<TaskRow>();

    /// <summary>人话状态（绑定失败/超时/已连接），设置面板与岛共用同一份文案。</summary>
    internal string TasksStatus => _tasks?.StatusText ?? "未启动";

    /// <summary>上次读 settings.json 失败时的 .bak 路径（null = 正常读到/首次运行）。</summary>
    internal string? SettingsBackupPath => _settingsBackupPath;

    /// <summary>穿透状态变化（热键或托盘菜单都能改），托盘据此刷新勾选。</summary>
    internal event EventHandler? ClickThroughChanged;

    public MainWindow()
    {
        InitializeComponent();

        // 配置先读回来：坏了用默认值 + 改名成 .bak，绝不崩（见 SettingsStore.Load）
        SettingsLoadResult loaded = SettingsStore.Load();
        _settings = loaded.Settings;
        _settingsBackupPath = loaded.BackupPath;

        // 位置：等句柄建好、知道自己在哪个显示器上之后再按设备像素落位（见 RestorePlacementPixels）

        // 初始是收起态：副行和状态行先藏起来（否则那几行字会撑出胶囊）
        StatusText.Visibility = Visibility.Collapsed;
        Line2.Visibility = Visibility.Collapsed;

        // 假磨砂的噪点：一块 64×64 的固定灰度噪声平铺，种子写死 → 每次启动纹理一致、可复现
        _idleBorderBrush = Island.BorderBrush; // 悬停强调的还原值

        // 布局算完之后把窗口裁成胶囊形状，形状外一律穿透
        Loaded += (_, _) =>
        {
            if (IsTopMode)
            {
                // 贴顶模式平时不该有窗口露在屏幕上：启动时已经挪到屏幕外，这里直接藏掉。
                // 也绝不把当前（屏幕外/面板）坐标写进配置——那是胶囊的位置。
                Hide();
                return;
            }
            UpdateWindowRegion();
            // 句柄就绪后才知道自己在哪个显示器上，这时把屏幕名补进配置
            SavePlacement();
        };

        // 悬停展开延迟：0 时直接展开，定时器不启动
        _leaveTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _leaveTimer.Interval = TimeSpan.FromMilliseconds(150);
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (_dragging)
            {
                return;
            }
            if (CursorOverIsland() || Island.IsMouseOver)
            {
                return; // 假离开：光标还在岛上，保持展开
            }
            Island.BorderBrush = _idleBorderBrush; // 确认走了才收回悬停强调（避免假 MouseLeave 让边框闪）
            Animate(false);
        };

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            Animate(true);
        };

        // 托盘图标 + 菜单（显示/隐藏、穿透、自启、设置、退出）
        _tray = new TrayIcon(this);

        // 全屏让位：全屏/锁屏时淡出隐藏，退出后淡入恢复（设置里可以关）
        _fullscreen = new FullscreenWatcher();
        _fullscreen.SuppressChanged += OnFullscreenChanged;

        // 账本：启动同步读一次拿初值，之后只靠 watcher 事件（+2 秒去抖），绝不轮询
        _usage = UsageLedger.Read(UsageLedger.LedgerPath);
        _usageWatcher = new UsageWatcher();
        _usageWatcher.Changed += OnUsageChanged;

        // 任务面板：只绑 127.0.0.1，接收插件推的事件与 10 秒心跳
        _tasks = new TasksHub(Dispatcher);
        _tasks.Changed += OnTasksChanged;
        _tasks.Completed += OnTaskCompleted;

        Island.Opacity = _settings.Opacity;

        // 贴顶模式启动时窗口会被第一帧的 Show() 摆出来：先挪到屏幕外，Loaded 里再藏（否则顶部会闪一下）
        if (IsTopMode)
        {
            Left = -10000;
            Top = -10000;
        }

        ApplySettings();

        // 位置变化（拖动/复位）时把坐标写回去
        LocationChanged += (_, _) => SavePlacement();
    }

    /// <summary>设置面板/热键/托盘的唯一入口：改一项 → 立即生效 → 落盘。</summary>
    internal void UpdateSettings(Action<AppSettings> change)
    {
        change(_settings);
        _settings.Normalize();
        ApplySettings();
        SettingsStore.Save(_settings);
    }

    /// <summary>把当前配置整套套上去。可重复调用（每次设置变更都走这里）。</summary>
    private void ApplySettings()
    {
        // 先清掉淡入淡出动画，否则本地 Opacity 会被动画值盖住
        Island.BeginAnimation(OpacityProperty, null);
        Island.Opacity = _settings.Opacity;
        ApplyClickThrough();
        ApplyMediaEnabled();
        ApplyFullscreenEnabled();
        ApplyTasks();
        ApplyGlowColor();
        // 形态切换立即生效（胶囊 ↔ 贴顶），形态没变时这里什么都不做
        ApplyMode();
        // 模块顺序/开关、账本快照、媒体快照都可能变，统一在这里重排岛上文字
        RefreshIslandText();
        RefreshTopTiles();
    }

    /// <summary>接收地址变了就重开监听；绑定失败时任何一次设置变更都会自动重试。</summary>
    private void ApplyTasks()
    {
        if (_tasks is null)
        {
            return;
        }
        if (_tasksEndpointApplied == _settings.TasksEndpoint && _tasks.IsListening)
        {
            return;
        }
        _tasksEndpointApplied = _settings.TasksEndpoint;
        _tasks.Start(_settings.TasksEndpoint);
    }

    private void OnTasksChanged()
    {
        UpdateStatusText();
        UpdateGlow();
        RefreshIslandText();
    }

    // ---------- 呼吸辉光（有活在跑才呼吸，完成闪 3 下再常亮） ----------

    // 呼吸是慢动画，10fps 肉眼看不出台阶；CPU 硬线优先（实测见 README）
    private const int GlowFps = 5;

    private readonly SolidColorBrush _glowBrush =
        new(System.Windows.Media.Color.FromRgb(0x4D, 0xA3, 0xFF));

    private bool _glowBreathing;
    private bool _glowSteady;
    private bool _glowPulse;
    // 脉冲是「独占」状态：期间任何 Changed 都不许重开呼吸（会通过 BeginAnimation 顶掉脉冲），
    // 这段时间内又完成的任务排一次队，等当前 3 次脉冲放完再放下一组
    private bool _glowPulseQueued;

    private bool HasActiveTasks => TasksConnectedNow && _tasks!.ActiveCount > 0;

    private void ApplyGlowColor()
    {
        if (AppSettings.TryNormalizeColor(_settings.GlowColor, out string hex) &&
            System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color color)
        {
            _glowBrush.Color = color;
        }
        Glow.Stroke = _glowBrush;
    }

    /// <summary>有任务在跑 → 边框呼吸；任务清空 → 立刻停（除非正显示「已完成」常亮）。</summary>
    private void UpdateGlow()
    {
        if (HasActiveTasks)
        {
            _glowSteady = false;
            StartBreath();
        }
        else if (_glowBreathing)
        {
            StopBreath();
        }
    }

    /// <summary>整圈同步明暗：亮 1 秒 / 暗 1 秒，正弦缓动，只动描边透明度（胶囊本体和文字不动）。</summary>
    private void StartBreath()
    {
        // 脉冲期间不许重开呼吸：两者写的是同一个 Opacity 属性，BeginAnimation 会把脉冲顶掉
        if (_glowBreathing || _glowPulse)
        {
            return;
        }
        _glowBreathing = true;
        Glow.Stroke = _glowBrush;
        Glow.Visibility = Visibility.Visible;
        var breath = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(1000))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Timeline.SetDesiredFrameRate(breath, GlowFps);
        Glow.BeginAnimation(UIElement.OpacityProperty, breath);
    }

    private void StopBreath()
    {
        _glowBreathing = false;
        Glow.BeginAnimation(UIElement.OpacityProperty, null);
        if (!_glowSteady && !_glowPulse)
        {
            Glow.Visibility = Visibility.Collapsed;
            Glow.Opacity = 0;
        }
    }

    /// <summary>收到 completed：600ms 内脉冲 3 次（同色，只抬透明度），然后转常亮。</summary>
    private void OnTaskCompleted()
    {
        if (_glowSteady)
        {
            return;
        }
        if (_glowPulse)
        {
            // 已经在一组 3 次脉冲里：排队，等这组放完再闪下一组（不互相顶掉）
            _glowPulseQueued = true;
            return;
        }
        Glow.Stroke = _glowBrush;
        _glowPulse = true;
        _glowBreathing = false; // 呼吸让位给脉冲
        Glow.Visibility = Visibility.Visible;
        Glow.BeginAnimation(UIElement.OpacityProperty, null);
        var pulse = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(100))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            // 正弦缓入缓出：峰和谷都不是硬拐点，避免锯齿感（时长不变，600ms 三连峰时序不变）
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Timeline.SetDesiredFrameRate(pulse, 30); // 脉冲只有 600ms，5fps 会一帧都抽不到
        Glow.Opacity = 1;
        pulse.Completed += (_, _) =>
        {
            _glowPulse = false;
            if (_glowPulseQueued)
            {
                // 排队的那次完成：紧接着再放一组 3 次脉冲
                _glowPulseQueued = false;
                OnTaskCompleted();
                return;
            }
            // 闪完：还有活就回呼吸，否则保持「常亮但暗一档」
            if (HasActiveTasks)
            {
                _glowBreathing = false;
                StartBreath();
                return;
            }
            _glowSteady = true;
            Glow.BeginAnimation(UIElement.OpacityProperty, null);
            Glow.Opacity = 0.55;
        };
        Glow.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    /// <summary>展开/看一眼就算「已读」，常亮消失（不自动定时熄灭）。</summary>
    private void ClearGlowSteady()
    {
        if (!_glowSteady)
        {
            return;
        }
        _glowSteady = false;
        Glow.BeginAnimation(UIElement.OpacityProperty, null);
        Glow.Opacity = 0;
        if (!_glowBreathing)
        {
            Glow.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>当前是不是贴顶工作台形态（胶囊则保持全部原有行为）。</summary>
    private bool IsTopMode => _settings.Mode == AppSettings.ModeTop;

    /// <summary>
    /// 形态切换立即生效，不用重启：
    /// 贴顶 → 胶囊：摘下钩子、藏掉面板、把胶囊按存好的像素位置放回来；
    /// 胶囊 → 贴顶：藏掉胶囊，挂上 WH_MOUSE_LL，等光标碰顶。
    /// 形态没变时直接返回（否则每改一下透明度都会重摆一次窗口）。
    /// </summary>
    private void ApplyMode()
    {
        if (_hwnd == IntPtr.Zero || _appliedMode == _settings.Mode)
        {
            return;
        }
        _appliedMode = _settings.Mode;

        Island.Visibility = IsTopMode ? Visibility.Collapsed : Visibility.Visible;

        if (IsTopMode)
        {
            if (_edgeHook is null)
            {
                _edgeHook = new TopEdgeHook(_hwnd);
                _edgeHook.Start();
            }
            else
            {
                _edgeHook.Start(); // 幂等：从胶囊切回来时重新挂上
            }
            UpdateTopThreshold();
            if (IsVisible && !_topOpen)
            {
                Hide();
            }
            return;
        }

        _edgeHook?.Stop();
        _topOpen = false;
        TopPanel.Visibility = Visibility.Collapsed;
        TopPanel.BeginAnimation(HeightProperty, null);
        ApplyWindowSize(_expanded ? ExpandedWidth : CollapsedWidth,
            _expanded ? ExpandedHeight : CollapsedHeight);
        if (!IsVisible && !_hiddenByFullscreen)
        {
            Show();
            Island.BeginAnimation(OpacityProperty, null);
            Island.Opacity = _settings.Opacity;
            RestorePlacementPixels();
            UpdateWindowRegion();
        }
    }

    /// <summary>
    /// 贴顶模式的唤醒阈值：4 DIP 按缩放折算成物理像素。
    /// 钩子里只拿这个最大阈值做粗判（少算几次 DPI），真正的精确判定在 OnTopWake 里按光标所在屏复核。
    /// </summary>
    private void UpdateTopThreshold()
    {
        if (_edgeHook is null)
        {
            return;
        }
        double maxScale = 1;
        foreach (MonitorBox monitor in Placement.CurrentMonitors())
        {
            maxScale = Math.Max(maxScale, MonitorScale(monitor));
        }
        _edgeHook.ThresholdPx = (int)Math.Ceiling(4 * maxScale);
    }

    /// <summary>某块显示器的缩放（1.0 = 96 DPI）。取显示器中心点问 Shcore，问不到退回当前窗口的缩放。</summary>
    private double MonitorScale(MonitorBox monitor)
    {
        var center = new NativeMethods.POINT
        {
            X = (int)Math.Round(monitor.Bounds.Left + monitor.Bounds.Width / 2),
            Y = (int)Math.Round(monitor.Bounds.Top + monitor.Bounds.Height / 2)
        };
        IntPtr hmon = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hmon != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(hmon, 0, out uint dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }
        return CurrentScale;
    }

    /// <summary>光标在哪块显示器上（不在任何一块里时取主屏）。</summary>
    private static MonitorBox MonitorAt(IReadOnlyList<MonitorBox> monitors, NativeMethods.POINT cursor)
    {
        foreach (MonitorBox monitor in monitors)
        {
            if (monitor.Bounds.Contains(cursor.X, cursor.Y))
            {
                return monitor;
            }
        }
        return Placement.Primary(monitors);
    }

    /// <summary>钩子投来 WM_TOPWAKE 后的真正处理：复核阈值 → 按光标 X 摆面板 → 下滑展开。</summary>
    private void OnTopWake()
    {
        if (!IsTopMode || _topOpen || _hiddenByFullscreen || _hwnd == IntPtr.Zero)
        {
            return;
        }
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        IReadOnlyList<MonitorBox> monitors = Placement.CurrentMonitors();
        MonitorBox monitor = MonitorAt(monitors, cursor);
        double scale = MonitorScale(monitor);

        // 钩子是按"最大阈值"粗判的，这里按光标所在屏的缩放精确复核一次 4 DIP
        if (cursor.Y - monitor.Bounds.Top > 4 * scale)
        {
            return;
        }

        ShowTopPanel(monitor, scale, cursor.X);
    }

    /// <summary>把面板按光标 X 摆好（左右各留 24 DIP），然后从高度 0 滑到内容高度。</summary>
    private void ShowTopPanel(MonitorBox monitor, double scale, int cursorX)
    {
        _topOpen = true;
        if (_edgeHook is not null)
        {
            _edgeHook.Open = true; // 面板开着期间钩子别再投消息
        }
        _expanded = true;      // 面板里的文字用展开态（完整行，不用收起态的紧凑文案）
        RefreshTopTiles();
        RefreshIslandText();

        TopPanel.Visibility = Visibility.Visible;
        TopTiles.Measure(new Size(TopPanelWidth - 32, double.PositiveInfinity));
        _topHeight = Math.Max(120, TopTiles.DesiredSize.Height + 32); // 上下各 16 DIP 内边距

        Width = TopPanelWidth;
        Height = _topHeight;
        TopPanel.BeginAnimation(HeightProperty, null);
        TopPanel.Height = 0;

        double panelPx = TopPanelWidth * scale;
        double heightPx = _topHeight * scale;
        double margin = TopPanelEdgeMargin * scale;
        Rect work = monitor.Work;
        double maxLeft = Math.Max(work.Left + margin, work.Right - panelPx - margin);
        double left = Math.Clamp(cursorX - panelPx / 2, work.Left + margin, maxLeft);

        MoveTo(new Rect(left, work.Top, panelPx, heightPx));
        Show();
        Island.BeginAnimation(OpacityProperty, null); // 清掉上次淡出动画的残留
        UpdateWindowRegion();

        var slide = new DoubleAnimation(0, _topHeight, AnimDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        TopPanel.BeginAnimation(HeightProperty, slide);
        StartRegionTracking();
    }

    /// <summary>鼠标离开面板：向上滑回 0 再隐藏窗口；钩子重新允许唤醒。</summary>
    private void HideTopPanel()
    {
        if (!_topOpen)
        {
            return;
        }
        _topOpen = false;
        if (_edgeHook is not null)
        {
            _edgeHook.Open = false;
        }

        double from = TopPanel.ActualHeight;
        if (from <= 0.5 || !IsVisible)
        {
            FinishHideTopPanel();
            return;
        }

        var slide = new DoubleAnimation(from, 0, AnimDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        slide.Completed += (_, _) => FinishHideTopPanel();
        TopPanel.BeginAnimation(HeightProperty, slide);
        StartRegionTracking();
    }

    private void FinishHideTopPanel()
    {
        TopPanel.BeginAnimation(HeightProperty, null);
        TopPanel.Height = double.NaN;
        TopPanel.Visibility = Visibility.Collapsed;
        StopRegionTracking();
        if (IsTopMode)
        {
            Hide();
        }
    }

    private void TopPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => HideTopPanel();

    /// <summary>
    /// 贴顶面板的三块卡片：用量 / 任务 / 媒体。数据与胶囊上的文字同源（同一份快照）。
    /// 两列由 XAML 里 WrapPanel 的 ItemWidth=264 决定（560 - 32 内边距 - 12 间距，除 2）。
    /// </summary>
    private void RefreshTopTiles()
    {
        if (!IsTopMode || TopTiles is null)
        {
            return;
        }

        string key = string.Join("\u0001", new[]
        {
            _usage?.Day ?? "-",
            (_usage?.Tokens ?? -1).ToString(),
            (_usage?.Calls ?? -1).ToString(),
            _usage?.Balance?.ToString("F2") ?? "-",
            _usage?.FiveHour?.ToString() ?? "-",
            _usage?.Weekly?.ToString() ?? "-",
            _usage?.Monthly?.ToString() ?? "-",
            _mediaInfo.Title ?? "",
            _mediaInfo.Artist ?? "",
            _mediaInfo.Status ?? "",
            TasksConnectedNow ? "1" : "0",
            string.Join("\u0002", TaskList.Select(r => $"{r.Sid}:{(int)r.State}:{r.Workspace}")),
        });
        if (key == _topTilesKey)
        {
            return;
        }
        _topTilesKey = key;

        TopTiles.Children.Clear();
        TopTiles.Children.Add(Tile(
            "今日用量",
            _usage is null ? "无数据" : UsageLedger.UsageLine(_usage),
            _usage is null ? "账本里没有今天的数据" : UsageLedger.QuotaLine(_usage)));

        IReadOnlyList<TaskRow> rows = TaskList;
        var taskLines = new System.Collections.Generic.List<string>();
        if (!TasksConnectedNow)
        {
            taskLines.Add(_tasks?.ShortStatus ?? "未连接");
        }
        else if (rows.Count == 0)
        {
            taskLines.Add("没有正在运行的会话");
        }
        else
        {
            foreach (TaskRow row in rows.Take(3))
            {
                string workspace = string.IsNullOrWhiteSpace(row.Workspace) ? "未命名工作区" : row.Workspace;
                taskLines.Add($"{workspace} · {row.StateText}");
            }
            if (rows.Count > 3)
            {
                taskLines.Add($"还有 {rows.Count - 3} 个");
            }
        }
        string taskMain = !TasksConnectedNow
            ? "DSH 未运行"
            : $"{rows.Count(r => r.State != TaskState.Ended)} 个在跑";
        TopTiles.Children.Add(Tile("任务", taskMain, taskLines.ToArray()));

        TopTiles.Children.Add(Tile(
            "媒体",
            _settings.MediaEnabled ? mediaTitleLine() : "媒体信息已关闭",
            mediaDetailLine()));
    }

    /// <summary>一块卡片：标题 + 主行 + 若干副行。宽度由 WrapPanel 的 ItemWidth 决定，间距 12 DIP。</summary>
    private static System.Windows.Controls.Border Tile(string title, string main, params string[] details)
    {
        var stack = new System.Windows.Controls.StackPanel();
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = main,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });
        foreach (string detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                continue;
            }
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = detail,
                FontSize = 10,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        return new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 12, 12),
            Child = stack,
        };
    }

    /// <summary>托盘菜单的「开机自启」不归 JSON 管，真源是注册表。</summary>
    private void ApplyClickThrough() => SetClickThrough(_settings.ClickThrough);

    private void ApplyMediaEnabled()
    {
        if (_settings.MediaEnabled)
        {
            if (_media is null)
            {
                _media = new MediaSession(Dispatcher);
                _media.Updated += UpdateMedia;
                _media.Start();
            }
            return;
        }

        if (_media is not null)
        {
            _media.Updated -= UpdateMedia;
            _media.Dispose();
            _media = null;
        }
        // 关掉后别把上一首留在岛上
        UpdateMedia(new MediaInfo(null, null, null));
    }

    private void ApplyFullscreenEnabled()
    {
        if (_settings.FullscreenSuppress)
        {
            _fullscreen.Start();
            return;
        }

        _fullscreen.Stop();
        if (_hiddenByFullscreen)
        {
            _hiddenByFullscreen = false;
            if (IsTopMode)
            {
                return; // 贴顶模式没有常驻窗口，退出全屏也不该自己弹出来
            }
            Show();
            UpdateWindowRegion();
            FadeIn();
        }
    }

    /// <summary>给托盘/设置面板用：打开普通设置窗口（有焦点、可拖动、不是穿透窗口）。</summary>
    internal void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>复位到<b>当前显示器</b>顶部居中，并把实际像素坐标写回配置。</summary>
    internal void ResetPlacement()
    {
        _settings.LeftPx = null;
        _settings.TopPx = null;
        _settings.Left = null;
        _settings.Top = null;

        IReadOnlyList<MonitorBox> monitors = Placement.CurrentMonitors();
        double scale = CurrentScale;
        Rect rect = Placement.DefaultRect(CurrentMonitorBox(monitors), WindowPixelSize(scale), scale);
        MoveTo(rect);
        SavePlacement();
    }

    /// <summary>设置面板上显示的位置说明（屏幕设备名 + 像素坐标 + 当前缩放）。</summary>
    internal string PlacementDescription()
    {
        string screen = CurrentScreenName() is string name && name.Length > 0
            ? System.IO.Path.GetFileName(name)
            : "未识别";
        Rect r = WindowPixelRect();
        return $"屏幕 {screen} · 左上角 {r.Left:F0},{r.Top:F0} 像素 · 缩放 {CurrentScale * 100:F0}%"
            + $"（舞台 {ActualWidth:F0}×{ActualHeight:F0} DIP）";
    }

    /// <summary>当前显示器的缩放（1.0 = 96 DPI）。像素量一律从这里派生，别散落硬编码。</summary>
    private double CurrentScale
    {
        get
        {
            if (_hwnd != IntPtr.Zero)
            {
                uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
            return VisualTreeHelper.GetDpi(this).DpiScaleX;
        }
    }

    /// <summary>窗口舞台尺寸，设备像素（WPF 的 Width/Height 是 DIP）。</summary>
    private Size WindowPixelSize(double scale) =>
        new(Math.Round(Width * scale), Math.Round(Height * scale));

    /// <summary>窗口在屏幕上的真实矩形（设备像素）。</summary>
    private Rect WindowPixelRect()
    {
        if (_hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT r))
        {
            return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        return new Rect(Left, Top, Width, Height);
    }

    /// <summary>
    /// 窗口尺寸 = 岛上内容 + 两侧留白。尺寸变了要顺手把窗口挪一下，
    /// 保证胶囊中心不跳（区域随后按新尺寸重算）。
    /// </summary>
    private void ApplyWindowSize(double islandWidth, double islandHeight)
    {
        double width = islandWidth + WindowPadding * 2;
        double height = islandHeight + WindowPadding * 2;
        if (Math.Abs(Width - width) < 0.5 && Math.Abs(Height - height) < 0.5)
        {
            return;
        }

        Rect before = WindowPixelRect();

        Width = width;
        Height = height;

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        double scale = CurrentScale;
        double px = width * scale;
        double py = height * scale;

        // 上边缘不动、向下长（胶囊上沿不跳，贴顶展开不会把上部顶出屏幕）；左右仍居中，
        // 超出当前显示器工作区才由 GrowDown 内部的 ClampInto 夹回来
        Rect target = Placement.GrowDown(CurrentMonitorBox(Placement.CurrentMonitors()).Work,
            before, new Size(px, py));
        MoveTo(target);
        UpdateWindowRegion();
    }

    /// <summary>按像素矩形落位。带 SWP_NOACTIVATE——岛任何时候都不许抢焦点。</summary>
    private void MoveTo(Rect rect)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _restoringPlacement = true;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
            (int)Math.Round(rect.Left), (int)Math.Round(rect.Top), 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        _restoringPlacement = false;
    }

    /// <summary>句柄所属的显示器（按设备名匹，找不到用主屏）。</summary>
    private MonitorBox CurrentMonitorBox(IReadOnlyList<MonitorBox> monitors)
    {
        string? name = CurrentScreenName();
        foreach (MonitorBox m in monitors)
        {
            if (string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }
        }
        return Placement.Primary(monitors);
    }

    /// <summary>启动落位：同名显示器还在就夹进它的工作区，否则主屏顶部居中。</summary>
    private void RestorePlacementPixels()
    {
        IReadOnlyList<MonitorBox> monitors = Placement.CurrentMonitors();
        double scale = CurrentScale;

        Point? saved = null;
        if (_settings.LeftPx is double px && _settings.TopPx is double py)
        {
            saved = new Point(px, py);
        }
        else if (_settings.Left is double oldLeft && _settings.Top is double oldTop)
        {
            // v0.2-D 存的是 DIP：按主屏缩放折成像素，迁移完这两条老字段就不再用
            double systemScale = NativeMethods.GetDpiForSystem() / 96.0;
            saved = new Point(oldLeft * systemScale, oldTop * systemScale);
        }

        // v0.2-E/H 的位置是「380×180 窗口」的左上角、胶囊居中；现在窗口贴着胶囊，
        // 按胶囊中心换算一次，升级后岛不会横跳 100 多像素
        if (saved is Point old && _settings.PlacementVersion < 2)
        {
            const double legacyWindowWidth = 380;   // v0.2-E/H 的窗口尺寸，只用于这次迁移
            const double legacyWindowHeight = 180;
            double offsetX = ((legacyWindowWidth - CollapsedWidth) / 2.0 - WindowPadding) * scale;
            double offsetY = ((legacyWindowHeight - CollapsedHeight) / 2.0 - WindowPadding) * scale;
            saved = new Point(old.X + offsetX, old.Y + offsetY);
            _settings.PlacementVersion = 2;
        }

        MoveTo(Placement.Resolve(monitors, _settings.Screen, saved, WindowPixelSize(scale), scale));

        _settings.Left = null;
        _settings.Top = null;
    }

    private void SavePlacement()
    {
        // 拖动中每帧都会触发 LocationChanged，别每帧写一次文件
        // 贴顶模式下的窗口坐标是面板位置，不是胶囊位置，绝不能覆盖 LeftPx/TopPx
        if (_restoringPlacement || _dragging || IsTopMode)
        {
            return;
        }

        Rect r = WindowPixelRect();
        _settings.LeftPx = r.Left;
        _settings.TopPx = r.Top;
        _settings.Left = null; // 老 DIP 字段不再写，留着只会和像素字段打架
        _settings.Top = null;
        _settings.Screen = CurrentScreenName();
        SettingsStore.Save(_settings);
    }

    /// <summary>当前显示器设备名（\\.\DISPLAY1）。多屏还原按设备名找屏，找不到回落主屏顶部居中。</summary>
    private string? CurrentScreenName()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return System.Windows.Forms.Screen.FromHandle(_hwnd)?.DeviceName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>全屏/锁屏时让位；退出全屏后恢复（淡入 160ms，远小于 1s 要求）。</summary>
    private void OnFullscreenChanged(bool suppress)
    {
        // 钩子也要知道：全屏/锁屏期间不许把贴顶面板叫出来
        if (_edgeHook is not null)
        {
            _edgeHook.Suppressed = suppress;
        }

        if (IsTopMode)
        {
            // 贴顶模式本来就没有常驻窗口：只需把已经弹出的面板收掉；退出全屏不自动弹（等用户再碰顶边）
            if (suppress && _topOpen)
            {
                HideTopPanel();
            }
            return;
        }

        if (suppress)
        {
            if (!IsVisible)
            {
                return;
            }
            _hiddenByFullscreen = true;
            FadeOutThenHide();
            return;
        }

        // 只有"因为全屏才隐藏"的才恢复；用户手动隐藏的不擅自弹出来
        if (!_hiddenByFullscreen)
        {
            return;
        }
        _hiddenByFullscreen = false;
        Show();
        UpdateWindowRegion();
        FadeIn();
    }

    private void FadeOutThenHide()
    {
        var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(140)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (_hiddenByFullscreen)
            {
                Hide();
            }
        };
        Island.BeginAnimation(OpacityProperty, fade);
    }

    private void FadeIn()
    {
        var fade = new DoubleAnimation(0, _settings.Opacity, new Duration(TimeSpan.FromMilliseconds(160)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Island.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>媒体快照只更新数据，岛上文字统一由 RefreshIslandText() 拼（模块可能被关掉/挪位）。</summary>
    private void UpdateMedia(MediaInfo info)
    {
        _mediaInfo = info;
        RefreshIslandText();
    }

    /// <summary>账本去抖后重读一次；读不出来就是「无数据」，不保留旧数字。</summary>
    private void OnUsageChanged()
    {
        _usage = UsageLedger.Read(UsageLedger.LedgerPath);
        RefreshIslandText();
    }

    /// <summary>
    /// 按「岛内容」里的顺序和开关排岛上两行。
    /// 收起时主行用设置里选定那一项；展开时主行是顺序里第一个可见模块，其余合并进副行。
    /// </summary>
    private void RefreshIslandText()
    {
        var ids = _settings.Modules.Where(m => m.Visible).Select(m => m.Id).ToList();
        // 任务面板没连上（25 秒内无心跳）或一个会话都没有时整体隐藏，不留「任务 0 个在跑」这种空壳
        bool hasTasks = TasksConnectedNow && _tasks!.Snapshot().Count > 0;
        ids.RemoveAll(id => id == IslandModuleIds.Tasks && !hasTasks);

        RefreshTaskRows();

        if (ids.Count == 0)
        {
            Line1.Text = "FloatIsle";
            Line2.Text = "";
            Line2.Visibility = Visibility.Collapsed;
            return;
        }

        string primary = _expanded
            ? ids[0]
            : ids.Contains(_settings.CollapsedLine) ? _settings.CollapsedLine : ids[0];

        Line1.Text = ModuleTitle(primary, compact: !_expanded);

        string mediaDetail = mediaDetailLine();
        var parts = new System.Collections.Generic.List<string>(3);
        if (mediaDetail.Length > 0 && (primary == IslandModuleIds.Media || ids.Contains(IslandModuleIds.Media)))
        {
            parts.Add(mediaDetail);
        }
        foreach (string id in ids)
        {
            if (id == primary || id == IslandModuleIds.Media)
            {
                continue;
            }
            parts.Add(ModuleTitle(id));
        }

        Line2.Text = string.Join(" · ", parts);
        // 没有副行内容时别留一条空行（比如只开今日用量、又没在放歌）
        Line2.Visibility = _expanded && parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNoticeText();
    }

    /// <summary>
    /// 插件给的失败原因（心跳 notice）只在展开态占一行；null 时下一次心跳就清掉，
    /// 我们不自己编文案、也不自己计时。
    /// </summary>
    private void UpdateNoticeText()
    {
        string notice = _tasks?.Notice ?? "";
        NoticeText.Text = notice;
        NoticeText.Visibility = _expanded && notice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 展开态任务区：最多 4 行「工作区 · 状态」，多的收成「还有 N 个」。
    /// 收起态不建行（160 DIP 装不下），主行只显示「N 个在跑」。
    /// </summary>
    private void RefreshTaskRows()
    {
        IReadOnlyList<TaskRow> rows = _expanded && TasksConnectedNow ? TaskList : Array.Empty<TaskRow>();
        // 心跳每 10 秒来一次、内容多数没变：签名相同就别重建控件（重建一轮 WPF 布局要几十毫秒）
        string key = string.Join("\u0001", rows.Select(r => $"{r.Sid}\u0002{(int)r.State}\u0002{r.Workspace}\u0002{r.Detail}"));
        if (key == _taskRowsKey)
        {
            return;
        }
        _taskRowsKey = key;

        TaskRowsPanel.Children.Clear();
        if (rows.Count == 0)
        {
            TaskRowsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (TaskRow row in rows.Take(MaxTaskRows))
        {
            string workspace = string.IsNullOrWhiteSpace(row.Workspace) ? "未命名工作区" : row.Workspace;
            var text = new System.Windows.Controls.TextBlock
            {
                Text = $"{workspace} · {row.StateText}",
                Foreground = row.State == TaskState.Ended
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
                Tag = row.Sid,
            };
            if (row.State != TaskState.Ended)
            {
                text.Cursor = System.Windows.Input.Cursors.Hand;
                text.MouseLeftButtonUp += TaskRow_Click;
            }
            TaskRowsPanel.Children.Add(text);
        }

        if (rows.Count > MaxTaskRows)
        {
            TaskRowsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"还有 {rows.Count - MaxTaskRows} 个",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        TaskRowsPanel.Visibility = TaskRowsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>点一行任务 = 让插件把该会话窗口拉到前台。插件不在就静默失败（TasksHub.Jump）。</summary>
    private void TaskRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string sid })
        {
            TasksHub.Jump(_settings.TasksJumpEndpoint, sid);
        }
    }

    /// <summary>
    /// 主行文字：媒体只放标题，详情留给副行，免得 280px 里全被省略号吃掉。
    /// compact = 收起态（可用宽度 158 DIP），用量/额度改用更短的一行；媒体标题无法预知长度，
    /// 仍交给 CharacterEllipsis（那是标题本身长，不是文案设计问题）。
    /// </summary>
    private string ModuleTitle(string id, bool compact = false) => id switch
    {
        IslandModuleIds.Usage => compact ? UsageLedger.UsageCompactLine(_usage) : UsageLedger.UsageLine(_usage),
        IslandModuleIds.Quota => UsageLedger.QuotaLine(_usage),
        IslandModuleIds.Media => mediaTitleLine(),
        IslandModuleIds.Tasks => compact
            ? $"{TaskList.Count(r => r.State != TaskState.Ended)} 个在跑"
            : $"任务 {TaskList.Count(r => r.State != TaskState.Ended)} 个在跑",
        _ => ""
    };

    private string mediaTitleLine()
    {
        if (!_settings.MediaEnabled)
        {
            return "媒体信息已关闭";
        }
        return string.IsNullOrWhiteSpace(_mediaInfo.Title) ? "无媒体" : _mediaInfo.Title!;
    }

    /// <summary>媒体副信息（艺人 · 播放状态）。关闭 SMTC 或没有会话时为空。</summary>
    private string mediaDetailLine()
    {
        if (!_settings.MediaEnabled || string.IsNullOrWhiteSpace(_mediaInfo.Title))
        {
            return "";
        }
        string detail = string.Join(" · ", new[] { _mediaInfo.Artist, _mediaInfo.Status }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return detail;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // 不抢焦点 + 不进 Alt+Tab / 任务栏
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        // 全局热键：Ctrl+Alt+I 切换整窗点击穿透
        _hotKeyOk = NativeMethods.RegisterHotKey(
            _hwnd, HotKeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_I);

        // 配置里的「默认穿透」等句柄就绪后才真正生效
        SetClickThrough(_settings.ClickThrough);

        // 有了窗口句柄才知道真实 DPI：托盘图标按它选 ico 档位（100% → 16 px，150% → 24 px）
        _tray?.UpdateIconSize((int)NativeMethods.GetDpiForWindow(_hwnd));


        // 收起态窗口贴着胶囊（176×60），先定尺寸再落位，夹紧才算得对
        ApplyWindowSize(CollapsedWidth, CollapsedHeight);

        // 句柄就绪、能问出"自己在哪块屏、什么缩放"之后，按设备像素落位
        RestorePlacementPixels();

        // 形态：贴顶模式在这里挂上钩子并把自己藏起来（胶囊模式什么都不做）
        ApplyMode();
    }

    protected override void OnClosed(EventArgs e)
    {        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotKeyId);
        }
        _hwndSource?.RemoveHook(WndProc);
        // 托盘图标必须先撤掉，否则进程退出后托盘里留幽灵图标
        _tray?.Dispose();
        _media?.Dispose();
        _usageWatcher?.Dispose();
        _tasks?.Dispose();
        _edgeHook?.Dispose();
        _fullscreen.Dispose();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            SetClickThrough(!_clickThrough);
            handled = true;
        }
        else if (msg == NativeMethods.WM_TOPWAKE)
        {
            // 钩子只在"光标碰顶且面板关着"时投这条；真正建面板在这里（UI 线程）做
            OnTopWake();
            handled = true;
        }
        else if (msg == NativeMethods.WM_DPICHANGED)
        {
            // 缩放变了（系统改缩放，或岛被挪到另一块缩放不同的屏）：lParam 是系统给的建议矩形（物理像素）
            NativeMethods.RECT r = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
            _restoringPlacement = true;
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top,
                r.Right - r.Left, r.Bottom - r.Top,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            _restoringPlacement = false;

            // 区域要等布局按新 DPI 落定后再算，否则仍拿旧 ActualWidth 画圆角
            // 托盘图标同理：换缩放后要按新 DPI 重新选 ico 档位，否则还是旧档、看着发糊
            int newDpi = (int)(wParam.ToInt64() & 0xFFFF);
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    UpdateWindowRegion();
                    SavePlacement();
                    _tray?.UpdateIconSize(newDpi);
                }),
                DispatcherPriority.Loaded);

            // 不标 handled：WPF 自己也要按新 DPI 更新布局
        }
        return IntPtr.Zero;
    }

    /// <summary>开/关整窗鼠标穿透。开启后鼠标事件落到岛下面的窗口。</summary>
    private void SetClickThrough(bool on)
    {
        _clickThrough = on;

        // 穿透状态本身就是「默认穿透」这个字段：热键/托盘切换也算改设置，一起落盘
        _settings.ClickThrough = on;
        SettingsStore.Save(_settings);

        if (_hwnd != IntPtr.Zero)
        {
            // 贴顶面板要能点（点任务行），所以这个形态下不套 WS_EX_TRANSPARENT；
            // 胶囊形态照旧按设置/热键切换
            bool effective = on && !IsTopMode;
            int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            if (effective)
            {
                ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
            }
            else
            {
                // LAYERED 保留：WPF 透明窗口自身依赖它
                ex &= ~NativeMethods.WS_EX_TRANSPARENT;
            }
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }

        UpdateStatusText();
        ClickThroughChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>给托盘菜单用：翻转整窗穿透（热键走的是同一条路径）。</summary>
    internal void ToggleClickThrough() => SetClickThrough(!_clickThrough);

    /// <summary>给托盘菜单用：显示/隐藏岛。隐藏不销毁窗口，热键和托盘都还在。</summary>
    internal void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        _hiddenByFullscreen = false; // 用户手动要它出来，就别再按"全屏让位"的状态恢复
        Show();
        Island.BeginAnimation(OpacityProperty, null); // 清掉淡出动画残留
        Island.Opacity = _settings.Opacity;
        UpdateWindowRegion(); // 重新显示后按当前尺寸重算一次区域
    }

    private void UpdateStatusText()
    {
        string hotKey = _hotKeyOk ? "Ctrl+Alt+I" : "热键注册失败";
        string click = _clickThrough
            ? $"点击穿透：开 · {hotKey} 关闭"
            : $"点击穿透：关 · {hotKey} 开启";
        // 任务面板没连上时，状态行顶一句短的（设置里「联动」栏仍用完整文案，判定是同一个 Connected）
        string tasks = TasksConnectedNow ? "" : _tasks?.ShortStatus ?? "";
        StatusText.Text = tasks.Length > 0 ? $"{tasks} · {click}" : click;
    }

    private void Island_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // 拖拽期间鼠标在窗口边缘进出是常态，不许因此展开/收起
        if (_dragging)
        {
            return;
        }
        _leaveTimer.Stop();
        // 悬停时描边加深一档（"轻微强调"，不放大几何，免得和展开动画的尺寸打架）
        Island.BorderBrush = HoverBorderBrush;
        StartHover();
    }

    /// <summary>悬停规则：0 = 立即展开，否则等 HoverDelayMs。</summary>
    private void StartHover()
    {
        if (_settings.HoverDelayMs <= 0)
        {
            Animate(true);
            return;
        }

        _hoverTimer.Interval = TimeSpan.FromMilliseconds(_settings.HoverDelayMs);
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void Island_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // 拖拽期间不动；收起永远是立即的，不然鼠标走了它还挂着
        if (_dragging)
        {
            return;
        }
        _hoverTimer.Stop();

        // 自己 SetWindowPos / 改区域时系统会补发假 MouseLeave。隔 150ms 再确认一次：
        // 光标真走了才收起，否则「展开→leave→收起→enter→展开」会一直抖（用户看到的就是岛一直在闪）。
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    /// <summary>光标是否真的落在岛上（用屏幕坐标反算，绕开 WPF 在窗口挪动时给的假 MouseLeave）。</summary>
    private bool CursorOverIsland()
    {
        if (_hwnd == IntPtr.Zero || !Island.IsVisible ||
            !NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return false;
        }
        Point local = Island.PointFromScreen(new Point(pt.X, pt.Y));
        return local.X >= 0 && local.Y >= 0 &&
               local.X <= Island.ActualWidth && local.Y <= Island.ActualHeight;
    }

    /// <summary>
    /// 按住胶囊左键开始拖动：先收起（拖的时候还挂着展开态会很怪）并记住起点。
    /// 不能用 DragMove()——那个要求窗口被激活，会直接破掉"不抢焦点"。
    /// </summary>
    private void Island_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsTopMode || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _dragging = true;
        _hoverTimer.Stop();
        Animate(false);

        if (NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            _dragCursorStart = cursor;
            _dragWindowStart = WindowPixelRect();
            Island.CaptureMouse();
        }
        e.Handled = true;
    }

    /// <summary>拖动中：窗口跟着光标走（SetWindowPos + SWP_NOACTIVATE），松手前不夹、不落盘。</summary>
    private void Island_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        // 鼠标在别处松开时收不到 MouseUp，这里兜一次，避免窗口一直黏在光标上
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        MoveTo(new Rect(
            _dragWindowStart.Left + (cursor.X - _dragCursorStart.X),
            _dragWindowStart.Top + (cursor.Y - _dragCursorStart.Y),
            _dragWindowStart.Width,
            _dragWindowStart.Height));
    }

    private void Island_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        EndDrag();
        e.Handled = true;
    }

    /// <summary>松手：夹进当前显示器工作区（不许留半截在屏幕外）→ 写回像素坐标 → 鼠标还在就按悬停规则展开。</summary>
    private void EndDrag()
    {
        _dragging = false;
        if (Island.IsMouseCaptured)
        {
            Island.ReleaseMouseCapture();
        }

        IReadOnlyList<MonitorBox> monitors = Placement.CurrentMonitors();
        double scale = CurrentScale;
        ApplyWindowSize(CollapsedWidth, CollapsedHeight); // 拖拽期间没收的窗口在这里收
        Rect current = WindowPixelRect();
        MoveTo(Placement.ClampInto(
            CurrentMonitorBox(monitors).Work,
            new Point(current.Left, current.Top),
            WindowPixelSize(scale)));
        SavePlacement();

        if (Island.IsMouseOver)
        {
            StartHover();
        }
    }

    /// <summary>把 "#AARRGGBB" 变成冻结的实色刷（冻结后跨线程也安全，不参与布局失效）。</summary>
    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private void Animate(bool expand)
    {
        // 已经在往同一目标动，就别重启：悬停期间 WPF 会因为窗口尺寸变化重复送 MouseEnter，
        // 每次重启动画都从当前帧重新缓动，肉眼看到的就是「上下来回闪」。
        if (_animating && expand == _expanded)
        {
            return;
        }
        _animating = true;

        // 弹簧感：BackEase 的 EaseOut 会在收尾处轻轻过冲一下。Amplitude 由 SpringAmplitude 定，
        // selftest 里按同一公式算出最大过冲像素，超过 4 DIP 就红（不许弹成果冻）。
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = SpringAmplitude };
        var spring = SpringDuration;

        // 收起时只留主行：副行和状态行都会把 160×44 撑爆
        _expanded = expand;
        if (expand)
        {
            ClearGlowSteady(); // 看一眼就算已读，常亮收掉
        }
        StatusText.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        Line2.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        RefreshIslandText(); // 主行内容随展开/收起切换（收起用设置里选的那一项）

        // 展开态有任务行时要更高，否则第 3、4 行会被胶囊裁掉
        double expandedHeight = TaskRowsPanel.Visibility == Visibility.Visible
            ? ExpandedTasksHeight
            : ExpandedHeight;
        if (NoticeText.Visibility == Visibility.Visible)
        {
            expandedHeight += 16; // 提示行自己占一行，别把状态行挤掉
        }

        // 展开：窗口先长到目标尺寸（区域永远装在窗口里，中间帧不会出现「区域比窗口大」）；
        // 收起：等胶囊缩回去再收窗口，避免最后一帧把胶囊切掉
        if (expand)
        {
            ApplyWindowSize(ExpandedWidth, expandedHeight);
        }

        var widthAnim = new DoubleAnimation(expand ? ExpandedWidth : CollapsedWidth, spring)
        {
            EasingFunction = ease
        };
        widthAnim.Completed += (_, _) =>
        {
            _animating = false;
            StopRegionTracking();
            if (!expand && !_dragging)
            {
                // 拖拽中先不收窗口：尺寸一改窗口原点会跟着挪，拖拽位移就不跟手了
                ApplyWindowSize(CollapsedWidth, CollapsedHeight);
            }
        };

        Island.BeginAnimation(WidthProperty, widthAnim);
        Island.BeginAnimation(HeightProperty,
            new DoubleAnimation(expand ? expandedHeight : CollapsedHeight, spring) { EasingFunction = ease });

        StartRegionTracking();
    }

    /// <summary>动画期间逐帧把窗口区域改成当前胶囊大小，避免"点得到看不见的地方"。</summary>
    private void StartRegionTracking()
    {
        if (_regionTracking)
        {
            return;
        }
        _regionTracking = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRegionTracking()
    {
        if (!_regionTracking)
        {
            return;
        }
        _regionTracking = false;
        CompositionTarget.Rendering -= OnRendering;
        UpdateWindowRegion(); // 补最后一帧
    }

    private void OnRendering(object? sender, EventArgs e) => UpdateWindowRegion();

    /// <summary>把窗口裁成当前形态的形状（胶囊圆角矩形 / 贴顶面板），区域外鼠标穿透。</summary>
    private void UpdateWindowRegion()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // 两种形态各裁各的：胶囊在窗口里居中，贴顶面板贴左上角
        System.Windows.Controls.Border shape = IsTopMode ? TopPanel : Island;
        if (shape.Visibility != Visibility.Visible)
        {
            return;
        }

        double shapeW = shape.ActualWidth;
        double shapeH = shape.ActualHeight;
        if (shapeW <= 0 || shapeH <= 0)
        {
            return;
        }

        // 形状在窗口里的位置用变换算：居中/贴边都由布局决定，这里不重复一遍对齐逻辑
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Point offset = shape.TransformToAncestor(this).Transform(new Point(0, 0));
        double radius = shape.CornerRadius.TopLeft; // 圆角半径（DIP），区域里要的是直径

        int x1 = (int)Math.Round(offset.X * dpi.DpiScaleX);
        int y1 = (int)Math.Round(offset.Y * dpi.DpiScaleY);
        int x2 = (int)Math.Round((offset.X + shapeW) * dpi.DpiScaleX);
        int y2 = (int)Math.Round((offset.Y + shapeH) * dpi.DpiScaleY);
        int ellipseW = (int)Math.Round(radius * 2 * dpi.DpiScaleX);
        int ellipseH = (int)Math.Round(radius * 2 * dpi.DpiScaleY);

        IntPtr rgn = NativeMethods.CreateRoundRectRgn(x1, y1, x2, y2, ellipseW, ellipseH);
        if (rgn == IntPtr.Zero)
        {
            return;
        }
        if (NativeMethods.SetWindowRgn(_hwnd, rgn, true) == 0)
        {
            // 设置失败时区域仍归我们所有，必须自己释放
            NativeMethods.DeleteObject(rgn);
        }
    }
}





















