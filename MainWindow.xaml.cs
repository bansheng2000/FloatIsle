using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
// UseWindowsForms 的隐式 using 会带来 System.Drawing.Size/Point，钉死用 WPF 的
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
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
    // 本地提示（跳转失败）：与插件 notice 共用 NoticeText 那一行
    private string _localNotice = "";
    private DispatcherTimer? _localNoticeTimer;

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _clickThrough;
    private bool _hotKeyOk;
    private bool _regionTracking;
    private bool _restoringPlacement;

    // 这次展开用的是「对称」还是「上沿不动向下长」，收回按同一模式反向走回去
    private bool _expandSymmetric;
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

    /// <summary>面板宽（DIP）：LayoutTiles 按内容自然宽度 + 工作区宽度算，这里是首次测量前的兜底。</summary>
    private double _topWidth = 520;
    private bool _topOpen;
    private double _topHeight;
    private int _topColumns = 2;
    private double _topCardWidth = 260;
    private string? _topTilesKey;

    /// <summary>当前面板里卡片的实际顺序（拖拽预览只换这里 + 网格位置，松手才写配置）。</summary>
    private readonly List<System.Windows.Controls.Border> _tileOrder = new();
    private System.Windows.Controls.Border? _dragTile;
    private Point _dragFrom;
    private bool _draggingTile;
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
            // 两种形态胶囊都露在屏幕上（贴顶模式也一样），所以这里只算区域、记位置
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
            if (IsTopMode && CursorOverTopPanel())
            {
                return; // 贴顶：光标已经移到下面的工作台面板上
            }
            Island.BorderBrush = _idleBorderBrush; // 确认走了才收回悬停强调（避免假 MouseLeave 让边框闪）
            if (IsTopMode)
            {
                CloseTopWorkbench();
            }
            else
            {
                Animate(false);
            }
        };

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (IsTopMode)
            {
                OpenTopWorkbench(); // 贴顶：向下长出工作台，不走胶囊的对称展开
            }
            else
            {
                Animate(true);
            }
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

    // 「等你回答/等你批准」与「完成」用固定色（设置面板只允许改「在跑」色，见 README）
    private static readonly System.Windows.Media.Color WaitingColor =
        System.Windows.Media.Color.FromRgb(0xF5, 0xA6, 0x23);

    private static readonly System.Windows.Media.Color DoneColor =
        System.Windows.Media.Color.FromRgb(0x3D, 0xBE, 0x6B);

    private readonly SolidColorBrush _waitBrush = new(WaitingColor);
    private readonly SolidColorBrush _doneBrush = new(DoneColor);

    /// <summary>当前呼吸的状态键："" 未呼吸 / "running" / "waiting"；换键就重启动画（换色换周期）。</summary>
    private string _breathKind = "";

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
        PaintGlow(_glowSteady ? _doneBrush
                  : _breathKind == "waiting" ? _waitBrush
                  : _glowBrush);
    }

    private void PaintGlow(SolidColorBrush brush)
    {
        Glow.Stroke = brush;
        GlowOuter.Stroke = brush;
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
        if (_glowPulse)
        {
            return;
        }
        // 优先级：等你（琥珀，1.2 s 更急）> 在跑（GlowColor，1.6 s）
        bool waiting = _tasks?.IsWaitingOnUser == true;
        string kind = waiting ? "waiting" : "running";
        if (_glowBreathing && _breathKind == kind)
        {
            return; // 同状态同周期：不重启动画，否则每次 Changed 都把呼吸从当前帧打回去
        }
        _glowBreathing = true;
        _breathKind = kind;
        PaintGlow(waiting ? _waitBrush : _glowBrush);
        Glow.Visibility = Visibility.Visible;
        GlowOuter.Visibility = Visibility.Visible;
        int halfMs = waiting ? 600 : 800;
        Glow.BeginAnimation(UIElement.OpacityProperty,
            BreathAnimation(0.28, 1.0, halfMs));
        GlowOuter.BeginAnimation(UIElement.OpacityProperty,
            BreathAnimation(0.02, 0.60, halfMs));
    }

    private void StopBreath()
    {
        _glowBreathing = false;
        _breathKind = "";
        Glow.BeginAnimation(UIElement.OpacityProperty, null);
        GlowOuter.BeginAnimation(UIElement.OpacityProperty, null);
        if (!_glowSteady && !_glowPulse)
        {
            Glow.Visibility = Visibility.Collapsed;
            GlowOuter.Visibility = Visibility.Collapsed;
            Glow.Opacity = 0;
            GlowOuter.Opacity = 0;
        }
    }

    /// <summary>一次呼吸半程的动画（AutoReverse 后整周期 1.6 s），缓入缓出。</summary>
    private static DoubleAnimation BreathAnimation(double from, double to, int halfMs)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(halfMs))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Timeline.SetDesiredFrameRate(anim, GlowFps);
        return anim;
    }

    /// <summary>收到 completed：600ms 内脉冲 3 次（同色，只抬透明度），然后转常亮。</summary>
    private void OnTaskCompleted()
    {
        if (_glowSteady)
        {
            return;
        }
        if (HasActiveTasks)
        {
            // 优先级：等你 > 在跑 > 完成。还有活在跑/在等你时不出绿，直接回到对应呼吸
            _glowBreathing = false;
            StartBreath();
            return;
        }
        if (_glowPulse)
        {
            // 已经在一组 3 次脉冲里：排队，等这组放完再闪下一组（不互相顶掉）
            _glowPulseQueued = true;
            return;
        }
        _breathKind = "";
        PaintGlow(_doneBrush);
        _glowPulse = true;
        _glowBreathing = false; // 呼吸让位给脉冲
        Glow.Visibility = Visibility.Visible;
        GlowOuter.Visibility = Visibility.Visible;
        Glow.BeginAnimation(UIElement.OpacityProperty, null);
        GlowOuter.BeginAnimation(UIElement.OpacityProperty, null);
        // 3 次 300 ms 峰：芯顶满、晕顶到 0.75，比呼吸高一档，扫一眼就能看出「完成了」
        DoubleAnimation PulseAnim(double from, double to)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(a, 30); // 脉冲 900ms，5fps 抽不到峰
            return a;
        }
        DoubleAnimation pulse = PulseAnim(0.55, 1.0);
        DoubleAnimation pulseOuter = PulseAnim(0.2, 0.75);
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
            // 常亮：芯满、晕留一档（不再是内层 0.55 那种「暗一档」）
            _glowSteady = true;
            Glow.BeginAnimation(UIElement.OpacityProperty, null);
            GlowOuter.BeginAnimation(UIElement.OpacityProperty, null);
            Glow.Opacity = 1.0;
            GlowOuter.Opacity = 0.35;
        };
        Glow.BeginAnimation(UIElement.OpacityProperty, pulse);
        GlowOuter.BeginAnimation(UIElement.OpacityProperty, pulseOuter);
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
        GlowOuter.BeginAnimation(UIElement.OpacityProperty, null);
        Glow.Opacity = 0;
        GlowOuter.Opacity = 0;
        if (!_glowBreathing)
        {
            Glow.Visibility = Visibility.Collapsed;
            GlowOuter.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>当前是不是贴顶工作台形态（胶囊则保持全部原有行为）。</summary>
    private bool IsTopMode => _settings.Mode == AppSettings.ModeTop;

    /// <summary>
    /// 形态切换立即生效，不用重启。两种形态**胶囊都常驻**，只换展开方式与展开后的内容：
    /// 胶囊模式 = 悬停四向/向下展开紧凑列表；贴顶模式 = 胶囊待在顶上，悬停向下长出卡片工作台。
    /// 形态没变时直接返回（否则每改一下透明度都会重摆一次窗口）。
    /// </summary>
    private void ApplyMode()
    {
        if (_hwnd == IntPtr.Zero || _appliedMode == _settings.Mode)
        {
            return;
        }
        _appliedMode = _settings.Mode;

        // 胶囊在两种形态下都可见：以前贴顶模式是 Island.Visibility = Collapsed，靠碰顶叫出面板，
        // 用户要的是「默认也显示胶囊，只是展开方式不同」。
        Island.Visibility = Visibility.Visible;
        Island.Margin = new Thickness(WindowPadding);

        if (IsTopMode)
        {
            // 贴顶：胶囊贴窗口上沿，展开时向下长
            Island.VerticalAlignment = VerticalAlignment.Top;
            ResetWorkbench();
            ApplyWindowSize(CollapsedWidth, CollapsedHeight); // 收起尺寸与胶囊模式完全一致（176×60）
            if (!IsVisible && !_hiddenByFullscreen)
            {
                Show();
                Island.BeginAnimation(OpacityProperty, null);
                Island.Opacity = _settings.Opacity;
                RestorePlacementPixels();
            }
            UpdateWindowRegion();
            return;
        }

        Island.VerticalAlignment = VerticalAlignment.Center;
        ResetWorkbench();
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

    /// <summary>把工作台面板收干净，不动窗口、不放动画：切形态/全屏让位/异常路径用。</summary>
    private void ResetWorkbench()
    {
        EndTileDrag();
        _topOpen = false;
        TopPanel.BeginAnimation(HeightProperty, null);
        TopPanel.Height = double.NaN;
        TopPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 贴顶工作台展开：**上沿不动、只向下长**（宽度按胶囊中心居中），一次 SetWindowPos 落地，
    /// 然后面板高度从 0 滑到内容高度；区域每帧按「胶囊 ∪ 面板」重算。
    /// </summary>
    private void OpenTopWorkbench()
    {
        if (!IsTopMode || _topOpen || _hiddenByFullscreen || _hwnd == IntPtr.Zero)
        {
            return;
        }
        _topOpen = true;
        _expanded = true; // 面板里的文字用展开态
        RefreshIslandText();

        TopPanel.Visibility = Visibility.Visible;
        // 面板接在胶囊下沿（窗口顶部留白 + 胶囊高），不留缝
        TopPanel.Margin = new Thickness(0, CollapsedHeight + WindowPadding * 2, 0, 0);
        // relayout:false —— 这次展开自己会摆窗口，别让 RefreshTopTiles 多来一次 SetWindowPos
        RefreshTopTiles(relayout: false);
        _topHeight = MeasureWorkbenchHeight();

        double scale = CurrentScale;
        Rect before = WindowPixelRect();
        Rect work = CurrentMonitorBox(Placement.CurrentMonitors()).Work;
        // GrowDown = 上沿不动 + 左右居中 + 放不下才夹回工作区，正是贴顶要的语义
        PlaceWindowRect(Placement.GrowDown(work, before, new Size(
            _topWidth * scale,
            (CollapsedHeight + WindowPadding * 2 + _topHeight) * scale)));

        TopPanel.BeginAnimation(HeightProperty, null);
        TopPanel.Height = 0;
        var slide = new DoubleAnimation(0, _topHeight, AnimDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        TopPanel.BeginAnimation(HeightProperty, slide);
        StartRegionTracking();
    }

    /// <summary>光标离开（胶囊和面板都算）→ 面板高度滑回 0，滑完才缩窗。</summary>
    private void CloseTopWorkbench()
    {
        if (!_topOpen)
        {
            return;
        }
        _topOpen = false;

        double from = TopPanel.ActualHeight;
        if (from <= 0.5 || !IsVisible)
        {
            FinishCloseTopWorkbench();
            return;
        }

        var slide = new DoubleAnimation(from, 0, AnimDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        slide.Completed += (_, _) => FinishCloseTopWorkbench();
        TopPanel.BeginAnimation(HeightProperty, slide);
        StartRegionTracking();
    }

    private void FinishCloseTopWorkbench()
    {
        TopPanel.BeginAnimation(HeightProperty, null);
        TopPanel.Height = double.NaN;
        TopPanel.Visibility = Visibility.Collapsed;
        _expanded = false;
        // 收起时才缩窗：动画期间窗口保持大尺寸，面板不会被提前裁掉
        if (IsTopMode && _hwnd != IntPtr.Zero && IsVisible)
        {
            double scale = CurrentScale;
            Rect work = CurrentMonitorBox(Placement.CurrentMonitors()).Work;
            PlaceWindowRect(Placement.GrowDown(work, WindowPixelRect(), new Size(
                (CollapsedWidth + WindowPadding * 2) * scale,
                (CollapsedHeight + WindowPadding * 2) * scale)));
        }
        StopRegionTracking();
    }

    private void TopPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleCollapse();

    /// <summary>
    /// 贴顶面板的卡片：按 <c>Modules</c> 的顺序与显示开关建卡（隐藏的不建卡、不占位）。
    /// 只建结构 + 铺网格，不量高度——高度要等面板可见后由 <see cref="MeasureWorkbenchHeight"/> 量。
    /// </summary>
    private void RefreshTopTiles(bool relayout = true)
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
            (_usage?.Cost ?? -1).ToString("F2"),
            _mediaInfo.Title ?? "",
            _mediaInfo.Artist ?? "",
            _mediaInfo.Status ?? "",
            TasksConnectedNow ? "1" : "0",
            string.Join("\u0002", TaskList.Select(r => $"{r.Sid}:{(int)r.State}:{r.Workspace}")),
            // 顺序与显示开关也进 key：设置里上移/下移、关掉一个模块、拖拽换位都要重建
            string.Join(",", _settings.Modules.Select(m => m.Id + (m.Visible ? "+" : "-"))),
        });
        if (key == _topTilesKey)
        {
            return;
        }
        _topTilesKey = key;

        EndTileDrag();
        TopTiles.Children.Clear();
        _tileOrder.Clear();

        foreach (IslandModule module in _settings.Modules)
        {
            if (!module.Visible)
            {
                continue;
            }
            System.Windows.Controls.Border? card = BuildTopTile(module);
            if (card is null)
            {
                continue;
            }
            card.Tag = module.Id;
            card.MouseLeftButtonDown += Tile_MouseLeftButtonDown;
            _tileOrder.Add(card);
            TopTiles.Children.Add(card);
        }

        if (_tileOrder.Count == 0)
        {
            // 全关掉了：给一句提示，别留一块空面板
            System.Windows.Controls.Border empty = Tile("工作台", "没有可显示的模块", "在设置里打开「岛内容」的显示开关");
            empty.Tag = "";
            _tileOrder.Add(empty);
            TopTiles.Children.Add(empty);
        }

        LayoutTiles();
        if (relayout)
        {
            RelayoutOpenWorkbench();
        }
    }

    /// <summary>按内容自然宽度算列数与卡片宽，铺进网格（不量高度：高度等面板可见后再量）。</summary>
    private void LayoutTiles()
    {
        double natural = 0;
        foreach (System.Windows.Controls.Border card in _tileOrder)
        {
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            natural = Math.Max(natural, card.DesiredSize.Width);
        }

        double workWidthDip = CurrentMonitorBox(Placement.CurrentMonitors()).Work.Width / CurrentScale;
        var layout = Placement.WorkbenchLayout(workWidthDip, _tileOrder.Count, natural);
        _topColumns = layout.Columns;
        _topCardWidth = layout.CardWidth;
        _topWidth = layout.PanelWidth;

        TopTiles.ColumnDefinitions.Clear();
        TopTiles.RowDefinitions.Clear();
        for (int c = 0; c < _topColumns; c++)
        {
            TopTiles.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(_topCardWidth)
            });
            if (c < _topColumns - 1)
            {
                TopTiles.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                {
                    Width = new GridLength(Placement.TileGap)
                });
            }
        }
        int rows = (_tileOrder.Count + _topColumns - 1) / _topColumns;
        for (int r = 0; r < rows; r++)
        {
            TopTiles.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            if (r < rows - 1)
            {
                TopTiles.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
                {
                    Height = new GridLength(Placement.TileGap)
                });
            }
        }

        foreach (System.Windows.Controls.Border card in _tileOrder)
        {
            card.Width = _topCardWidth;
            card.Margin = new Thickness(0); // 间距由网格的空行/空列给，卡片自己不留边距
            // 顶对齐：卡片高度 = 内容 + 内边距，同一行里矮的卡不被拉高（拉高就是空气）
            card.VerticalAlignment = VerticalAlignment.Top;
        }
        ApplyTilePositions();
        TopPanel.Width = _topWidth;
    }

    /// <summary>按当前顺序把卡片摆进网格（拖拽预览时也调这个：只换位置，不重建卡片）。</summary>
    private void ApplyTilePositions()
    {
        for (int i = 0; i < _tileOrder.Count; i++)
        {
            System.Windows.Controls.Grid.SetColumn(_tileOrder[i], 2 * (i % _topColumns));
            System.Windows.Controls.Grid.SetRow(_tileOrder[i], 2 * (i / _topColumns));
        }
    }

    /// <summary>
    /// 量面板高度：卡片全部 Auto 高，面板高 = 卡片网格高 + 上下内边距。
    /// 没有固定最小高、没有 MinHeight 撑出来的空气。必须在 TopPanel 可见后调用。
    /// </summary>
    private double MeasureWorkbenchHeight()
    {
        TopPanel.Width = _topWidth;
        TopTiles.Measure(new Size(Math.Max(1, _topWidth - (2 * Placement.PanelPadding)), double.PositiveInfinity));
        // DesiredSize 已经含 TopTiles 自己的 Margin（=PanelPadding，上下各一份），
        // 再补一次 2*PanelPadding 就是底部多出 32 DIP 的空气——这里只认 DesiredSize。
        return Math.Max(1, TopTiles.DesiredSize.Height);
    }

    /// <summary>面板开着时改了顺序/开关/内容：重算高宽，仍然一次 SetWindowPos、上沿不动。</summary>
    private void RelayoutOpenWorkbench()
    {
        if (!_topOpen || TopPanel.Visibility != Visibility.Visible || _hwnd == IntPtr.Zero)
        {
            return;
        }
        _topHeight = MeasureWorkbenchHeight();
        TopPanel.BeginAnimation(HeightProperty, null);
        TopPanel.Height = _topHeight;
        double scale = CurrentScale;
        Rect work = CurrentMonitorBox(Placement.CurrentMonitors()).Work;
        PlaceWindowRect(Placement.GrowDown(work, WindowPixelRect(), new Size(
            _topWidth * scale,
            (CollapsedHeight + (WindowPadding * 2) + _topHeight) * scale)));
    }

    /// <summary>一块卡片的文案：每条信息独立一行（或「字段名：值」），不再用「 · 」串长串。</summary>
    private System.Windows.Controls.Border? BuildTopTile(IslandModule module)
    {
        switch (module.Id)
        {
            case IslandModuleIds.Usage:
                return Tile(
                    "今日用量",
                    _usage is null ? "无数据" : UsageLedger.UsageMain(_usage),
                    _usage is null ? new[] { "账本里没有今天的数据" } : UsageLedger.UsageDetails(_usage).ToArray());
            case IslandModuleIds.Quota:
                return Tile(
                    "余额 / 额度",
                    _usage is null ? "无数据" : UsageLedger.QuotaMain(_usage),
                    _usage is null ? new[] { "账本里没有今天的数据" } : UsageLedger.QuotaDetails(_usage).ToArray());
            case IslandModuleIds.Tasks:
                return BuildTaskTile();
            case IslandModuleIds.Media:
                return Tile(
                    "媒体",
                    _settings.MediaEnabled ? mediaTitleLine() : "媒体信息已关闭",
                    MediaDetails());
            default:
                return null;
        }
    }

    /// <summary>媒体卡副行：演唱者、状态各占一行（原来是一行里用「 · 」拼起来）。</summary>
    private string[] MediaDetails()
    {
        if (!_settings.MediaEnabled || string.IsNullOrWhiteSpace(_mediaInfo.Title))
        {
            return Array.Empty<string>();
        }
        return new[] { _mediaInfo.Artist ?? "", _mediaInfo.Status ?? "" }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    private System.Windows.Controls.Border BuildTaskTile()
    {
        IReadOnlyList<TaskRow> rows = TaskList;
        var details = new List<string>();
        if (!TasksConnectedNow)
        {
            details.Add(_tasks?.ShortStatus ?? "未连接");
        }
        else if (rows.Count == 0)
        {
            details.Add("没有正在运行的会话");
        }
        else
        {
            foreach (TaskRow row in rows.Take(4))
            {
                string workspace = string.IsNullOrWhiteSpace(row.Workspace) ? "未命名工作区" : row.Workspace;
                details.Add($"{workspace}：{row.StateText}");
            }
            if (rows.Count > 4)
            {
                details.Add($"还有 {rows.Count - 4} 个");
            }
        }
        string main = !TasksConnectedNow ? "DSH 未运行" : $"{rows.Count(r => r.State != TaskState.Ended)} 个在跑";
        return Tile("任务", main, details.ToArray());
    }

    /// <summary>
    /// 一块卡片：标题 + 主行 + 若干副行。<b>高度完全由内容决定</b>（Height 不设、MinHeight 不设），
    /// 宽度由 <see cref="LayoutTiles"/> 按列宽设，间距由网格的空行/空列给。
    /// 改这里别再引入固定高度或 MinHeight——自检会断言这块 Border 的 Height 是 NaN。
    /// </summary>
    internal static System.Windows.Controls.Border Tile(string title, string main, params string[] details)
    {
        var stack = new System.Windows.Controls.StackPanel();
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = main,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
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
                Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        return new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = stack,
        };
    }

    /// <summary>
    /// 卡片拖拽换位：按下只记状态并捕获鼠标——<b>不碰岛、不收起、不触发任务行跳转</b>。
    /// 移动超过 4 DIP 才算拖；拖动时被拖的卡半透明、其余卡实时让位，那就是插入位置提示。
    /// </summary>
    private void Tile_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border card || _tileOrder.Count < 2 ||
            !NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return;
        }
        _dragTile = card;
        _dragFrom = TopTiles.PointFromScreen(new Point(pt.X, pt.Y));
        _draggingTile = false;
        TopTiles.CaptureMouse();
        e.Handled = true; // 别让按下冒泡出去（岛在另一边，这是双保险）
    }

    private void TopTiles_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragTile is null || !NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return;
        }
        Point p = TopTiles.PointFromScreen(new Point(pt.X, pt.Y));
        if (!_draggingTile)
        {
            if (Math.Abs(p.X - _dragFrom.X) < 4 && Math.Abs(p.Y - _dragFrom.Y) < 4)
            {
                return;
            }
            _draggingTile = true;
            _dragTile.Opacity = 0.55;
            System.Windows.Controls.Panel.SetZIndex(_dragTile, 1);
        }

        int from = _tileOrder.IndexOf(_dragTile);
        int to = TileIndexAt(p);
        if (from >= 0 && to >= 0 && to != from)
        {
            (_tileOrder[from], _tileOrder[to]) = (_tileOrder[to], _tileOrder[from]);
            ApplyTilePositions();
        }
        e.Handled = true;
    }

    private void TopTiles_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        System.Windows.Controls.Border? card = _dragTile;
        bool moved = _draggingTile;
        if (card is null)
        {
            return;
        }
        EndTileDrag();
        e.Handled = true;
        if (!moved)
        {
            return;
        }
        var ids = _tileOrder.Select(c => c.Tag as string ?? "").ToList();
        UpdateSettings(s => s.ReorderVisible(ids));
    }

    private void TopTiles_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragTile is not null)
        {
            EndTileDrag();
        }
    }

    /// <summary>收拖拽状态（松手/丢捕获/重建卡片都走这里）：恢复透明度、清标记，不动配置顺序。</summary>
    private void EndTileDrag()
    {
        if (_dragTile is not null)
        {
            _dragTile.Opacity = 1;
            System.Windows.Controls.Panel.SetZIndex(_dragTile, 0);
        }
        _dragTile = null;
        _draggingTile = false;
        if (TopTiles is not null && TopTiles.IsMouseCaptured)
        {
            TopTiles.ReleaseMouseCapture();
        }
    }

    /// <summary>光标下是第几张卡；不在任何卡上时取中心最近的一张（拖到空白也落到最近位置）。</summary>
    private int TileIndexAt(Point p)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        for (int i = 0; i < _tileOrder.Count; i++)
        {
            System.Windows.Controls.Border card = _tileOrder[i];
            Rect box = card.TransformToAncestor(TopTiles).TransformBounds(new Rect(card.RenderSize));
            if (box.Contains(p))
            {
                return i;
            }
            double dx = p.X - (box.Left + (box.Width / 2));
            double dy = p.Y - (box.Top + (box.Height / 2));
            double score = (dx * dx) + (dy * dy * 4); // 纵向权重高：跨行时先按行判断
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
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
    /// 位置和尺寸**必须一次 SetWindowPos 落地**：先改 WPF 的 Width/Height 会让布局单独
    /// 再发一次 SetWindowPos，那一次位置还是旧的——用户看到的就是「先原地长大、40ms 后
    /// 再左移 80px」。收也是同理（先在偏左位置缩回、再右移）。
    /// </summary>
    private void ApplyWindowSize(double islandWidth, double islandHeight)
    {
        double width = islandWidth + WindowPadding * 2;
        double height = islandHeight + WindowPadding * 2;
        if (Math.Abs(Width - width) < 0.5 && Math.Abs(Height - height) < 0.5)
        {
            return;
        }

        double scale = CurrentScale;
        Rect before = WindowPixelRect();

        if (_hwnd == IntPtr.Zero)
        {
            Width = width;
            Height = height;
            return;
        }

        // 展开规则：上方空间够就对称（岛中心不动、四向各扩一半）；不够才上沿不动向下长，
        // 再放不下由 GrowDown 内部的 ClampInto 夹回来。收回按同一模式反向回去。
        Rect work = CurrentMonitorBox(Placement.CurrentMonitors()).Work;
        Size targetSize = new(width * scale, height * scale);
        Rect target;
        if (height > before.Height + 0.5)
        {
            Rect? symmetric = Placement.ExpandSymmetric(work, before, targetSize);
            _expandSymmetric = symmetric is not null;
            target = symmetric ?? Placement.GrowDown(work, before, targetSize);
        }
        else
        {
            target = _expandSymmetric
                ? Placement.ExpandSymmetric(work, before, targetSize) ?? Placement.GrowDown(work, before, targetSize)
                : Placement.GrowDown(work, before, targetSize);
        }

        // 对称模式：岛在窗口里居中 → 窗口中心不动，岛中心自然不动。
        // 向下长模式：岛必须贴住窗口上沿，否则岛高了以后「居中」会让岛顶往上跑（看着就是上下移动）。
        Island.Margin = new Thickness(WindowPadding);   // 上下左右各留 padding，两种对齐才都算得对
        Island.VerticalAlignment = _expandSymmetric ? VerticalAlignment.Center : VerticalAlignment.Top;

        _restoringPlacement = true;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
            (int)Math.Round(target.Left), (int)Math.Round(target.Top),
            (int)Math.Round(target.Width), (int)Math.Round(target.Height),
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // 再把 WPF 侧同步到同一个矩形；值已经一致，WPF 后续自己的 SetWindowPos 不会再挪一下
        Left = target.Left / scale;
        Top = target.Top / scale;
        Width = target.Width / scale;
        Height = target.Height / scale;
        _restoringPlacement = false;

        UpdateWindowRegion();
    }

    /// <summary>像素矩形一次性落位：位置与尺寸必须在同一次 SetWindowPos 里，然后同步 WPF 侧（否则会二次挪动）。</summary>
    private void PlaceWindowRect(Rect target)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _restoringPlacement = true;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
            (int)Math.Round(target.Left), (int)Math.Round(target.Top),
            (int)Math.Round(target.Width), (int)Math.Round(target.Height),
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        double scale = CurrentScale;
        Left = target.Left / scale;
        Top = target.Top / scale;
        Width = target.Width / scale;
        Height = target.Height / scale;
        _restoringPlacement = false;
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
        if (_restoringPlacement || _dragging || _topOpen)
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
        if (IsTopMode && suppress)
        {
            // 贴顶模式胶囊也常驻：全屏时先把工作台收掉，再走下面统一的淡出隐藏
            ResetWorkbench();
            if (IsVisible && !_hwnd.Equals(IntPtr.Zero))
            {
                PlaceWindowRect(Placement.GrowDown(
                    CurrentMonitorBox(Placement.CurrentMonitors()).Work,
                    WindowPixelRect(),
                    new Size((CollapsedWidth + WindowPadding * 2) * CurrentScale,
                             (CollapsedHeight + WindowPadding * 2) * CurrentScale)));
            }
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
    /// <summary>
    /// 岛本体（胶囊里那几行）算不算展开：贴顶模式下胶囊尺寸固定，永远只显示收起行，
    /// 完整内容在工作台卡片里。不这么判，展开态那 4 行会挤进 44 DIP 的胶囊、顶到窗口外面。
    /// </summary>
    private bool IslandExpanded => _expanded && !IsTopMode;

    private void RefreshIslandText()
    {
        // 贴顶模式下胶囊尺寸固定（永远 160×44），只显示收起行：完整内容在工作台卡片里。
        // 不这么判，展开态那 4 行会挤进 44 DIP 的胶囊，文字顶到窗口上沿外面（实测 y=-3）。
        bool expanded = IslandExpanded;
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

        string primary = expanded
            ? ids[0]
            : ids.Contains(_settings.CollapsedLine) ? _settings.CollapsedLine : ids[0];

        Line1.Text = ModuleTitle(primary, compact: !expanded);

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

        Line2.Text = FitSecondaryLine(parts);
        // 没有副行内容时别留一条空行（比如只开今日用量、又没在放歌）
        Line2.Visibility = IslandExpanded && parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNoticeText();
    }

    /// <summary>
    /// 插件给的失败原因（心跳 notice）只在展开态占一行；null 时下一次心跳就清掉，
    /// 我们不自己编文案、也不自己计时。
    /// </summary>
    private void UpdateNoticeText()
    {
        // 本地提示（跳转失败）优先：那是用户刚点的动作，反馈必须立刻看得见；插件 notice 4 秒后自动回来
        string notice = _localNotice.Length > 0 ? _localNotice : (_tasks?.Notice ?? "");
        NoticeText.Text = notice;
        NoticeText.Visibility = IslandExpanded && notice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 展开态任务区：最多 4 行「工作区 · 状态」，多的收成「还有 N 个」。
    /// 收起态不建行（160 DIP 装不下），主行只显示「N 个在跑」。
    /// </summary>
    private void RefreshTaskRows()
    {
        IReadOnlyList<TaskRow> rows = IslandExpanded && TasksConnectedNow ? TaskList : Array.Empty<TaskRow>();
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
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = TextAlignment.Center,
                // Stretch 才能吃到网格给的宽度上限、真的显示省略号；Center 会按自然宽度顶出胶囊
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 0),
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
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        TaskRowsPanel.Visibility = TaskRowsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>点一行任务 = 让插件把该会话窗口拉到前台。插件不在就静默失败（TasksHub.Jump）。</summary>
    private async void TaskRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: string sid })
        {
            return;
        }
        bool jumped = await TasksHub.JumpAsync(_settings.TasksJumpEndpoint, sid).ConfigureAwait(true);
        if (jumped)
        {
            return;
        }
        // 别让用户以为「点了没反应」：跳转端口不通时在岛上说一句（4 秒后自动清）
        ShowLocalNotice($"跳转失败：{_settings.TasksJumpEndpoint} 没响应");
    }

    /// <summary>本地提示（跳转失败等）与插件 notice 共用一行，4 秒后自动清掉。</summary>
    private void ShowLocalNotice(string text)
    {
        _localNotice = text;
        UpdateNoticeText();
        _localNoticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _localNoticeTimer.Stop();
        _localNoticeTimer.Tick -= LocalNoticeElapsed;
        _localNoticeTimer.Tick += LocalNoticeElapsed;
        _localNoticeTimer.Start();
        // 提示行出现要多 16 DIP 高，否则会把状态行挤出去（Animate 里已有这条规则，复用同一路径）
        if (_expanded)
        {
            Animate(true);
        }
    }

    private void LocalNoticeElapsed(object? sender, EventArgs e)
    {
        _localNoticeTimer?.Stop();
        _localNotice = "";
        UpdateNoticeText();
    }

    /// <summary>点在任务行子树里？（含「还有 N 个」这种无 Tag 的行：也当行内，不触发拖动）</summary>
    private bool IsInsideTaskRows(System.Windows.DependencyObject? source)
    {
        for (System.Windows.DependencyObject? node = source;
             node is not null && !ReferenceEquals(node, Island);
             node = System.Windows.Media.VisualTreeHelper.GetParent(node)
                    ?? System.Windows.LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, TaskRowsPanel) ||
                (node is FrameworkElement { Tag: string }))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 副行按宽度降级：完整串超过 280 DIP 就从尾部丢项（先丢「任务 N 个在跑」，再丢月/周），
    /// 剩下的仍超宽就交给 CharacterEllipsis。100% 下完整串实测 276 px，已经在边缘上。
    /// </summary>
    private string FitSecondaryLine(System.Collections.Generic.List<string> parts)
    {
        const double MaxWidth = 280;
        var keep = new System.Collections.Generic.List<string>(parts);
        while (keep.Count > 1 && MeasureTextWidth(string.Join(" · ", keep), 11) > MaxWidth)
        {
            keep.RemoveAt(keep.Count - 1);
        }
        return string.Join(" · ", keep);
    }

    /// <summary>按当前 DPI 量一段文字的自然宽度（DIP），只给副行降级用。</summary>
    private double MeasureTextWidth(string text, double size)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        var formatted = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface("Segoe UI"),
            size,
            System.Windows.Media.Brushes.White,
            System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
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
            if (IsTopMode)
            {
                OpenTopWorkbench(); // 贴顶：向下长出工作台
            }
            else
            {
                Animate(true);
            }
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
        ScheduleCollapse();
    }

    /// <summary>光标是否真的落在岛上（用屏幕坐标反算，绕开 WPF 在窗口挪动时给的假 MouseLeave）。</summary>
    /// <summary>离开胶囊或工作台：150 ms 后复核光标，真走了才收（自己 SetWindowPos 会补发假 MouseLeave）。</summary>
    private void ScheduleCollapse()
    {
        if (_dragging || _draggingTile)
        {
            return;
        }
        _hoverTimer.Stop();
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    private bool CursorOverTopPanel()
    {
        if (TopPanel.Visibility != Visibility.Visible || _hwnd == IntPtr.Zero ||
            !NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return false;
        }
        Point local = TopPanel.PointFromScreen(new Point(pt.X, pt.Y));
        return local.X >= 0 && local.Y >= 0 &&
               local.X <= TopPanel.ActualWidth && local.Y <= TopPanel.ActualHeight;
    }

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

        // 点在任务行上就不许走拖动/收起：窗口一收、鼠标被岛捕获，行上的 MouseUp 就轮不到了，
        // 表现就是「点任务行只是收起、不跳转」。
        if (IsInsideTaskRows(e.OriginalSource as System.Windows.DependencyObject))
        {
            e.Handled = true;
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

        double targetWidth = expand ? ExpandedWidth : CollapsedWidth;
        double targetHeight = expand ? expandedHeight : CollapsedHeight;

        // FillBehavior=Stop：动画一结束就把时钟摘掉。默认的 HoldEnd 会让 Island.Width/Height
        // 永久停在「被动画驱动」的状态（IsAnimated 一直为 true），布局/DPI/区域重算读到的
        // 都不是基值，而且时钟一直挂着不放。
        var widthAnim = new DoubleAnimation(targetWidth, spring)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        var heightAnim = new DoubleAnimation(targetHeight, spring)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        widthAnim.Completed += (_, _) =>
        {
            // 先把目标值落到基值，再摘时钟：两步顺序反了会瞬间弹回动画前的尺寸
            Island.Width = targetWidth;
            Island.Height = targetHeight;
            Island.BeginAnimation(WidthProperty, null);
            Island.BeginAnimation(HeightProperty, null);

            _animating = false;
            StopRegionTracking();
            if (!expand && !_dragging)
            {
                // 拖拽中先不收窗口：尺寸一改窗口原点会跟着挪，拖拽位移就不跟手了
                ApplyWindowSize(CollapsedWidth, CollapsedHeight);
            }
        };

        Island.BeginAnimation(WidthProperty, widthAnim);
        Island.BeginAnimation(HeightProperty, heightAnim);

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

        // 贴顶模式：胶囊常驻在顶上，展开时下面接一块卡片面板，两块不是一个矩形 → 求并
        IntPtr rgn;
        if (IsTopMode)
        {
            rgn = RoundRectRegion(Island);
            if (rgn == IntPtr.Zero)
            {
                return;
            }
            if (TopPanel.Visibility == Visibility.Visible && TopPanel.ActualHeight > 0.5)
            {
                IntPtr panel = RoundRectRegion(TopPanel);
                if (panel != IntPtr.Zero)
                {
                    // 合并失败（返回值 0 = ERROR）就退回只裁胶囊，绝不留整窗矩形
                    NativeMethods.CombineRgn(rgn, rgn, panel, NativeMethods.RGN_OR);
                    NativeMethods.DeleteObject(panel);
                }
            }
        }
        else
        {
            rgn = RoundRectRegion(Island);
            if (rgn == IntPtr.Zero)
            {
                return;
            }
        }

        if (NativeMethods.SetWindowRgn(_hwnd, rgn, true) == 0)
        {
            // 设置失败时区域仍归我们所有，必须自己释放
            NativeMethods.DeleteObject(rgn);
        }
    }

    /// <summary>把一个 Border 换算成窗口客户区坐标里的圆角矩形区域；不可见或尺寸为 0 返回 Zero。</summary>
    private IntPtr RoundRectRegion(System.Windows.Controls.Border shape)
    {
        if (shape.Visibility != Visibility.Visible)
        {
            return IntPtr.Zero;
        }
        double w = shape.ActualWidth;
        double h = shape.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return IntPtr.Zero;
        }
        // 形状在窗口里的位置用变换算：居中/贴边都由布局决定，这里不重复一遍对齐逻辑
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Point offset = shape.TransformToAncestor(this).Transform(new Point(0, 0));
        double radius = shape.CornerRadius.TopLeft; // 圆角半径（DIP），区域里要的是直径
        return NativeMethods.CreateRoundRectRgn(
            (int)Math.Round(offset.X * dpi.DpiScaleX),
            (int)Math.Round(offset.Y * dpi.DpiScaleY),
            (int)Math.Round((offset.X + w) * dpi.DpiScaleX),
            (int)Math.Round((offset.Y + h) * dpi.DpiScaleY),
            (int)Math.Round(radius * 2 * dpi.DpiScaleX),
            (int)Math.Round(radius * 2 * dpi.DpiScaleY));
    }
}





















