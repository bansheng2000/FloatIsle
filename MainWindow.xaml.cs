using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
// UseWindowsForms 的隐式 using 会带来 System.Drawing.Size/Point，钉死用 WPF 的
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FloatIsle;

public partial class MainWindow : Window
{
    // RegisterHotKey 的 id，同一进程内唯一即可
    private const int HotKeyId = 0x4F49;

    private const double CollapsedWidth = 160;
    private const double CollapsedHeight = 44;
    private const double ExpandedWidth = 320;
    private const double ExpandedHeight = 120;

    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(180));

    private readonly AppSettings _settings;
    private readonly string? _settingsBackupPath;
    private readonly FullscreenWatcher _fullscreen;
    private readonly DispatcherTimer _hoverTimer;

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

    /// <summary>
    /// 任务面板的监听端还没接（插件侧心跳已在推，岛上还缺接收方）。
    /// 未连接时任务模块整体不显示，按 Chief 的要求不留空壳——接上后把这里换成真状态。
    /// </summary>
    private const bool TasksConnected = false;

    /// <summary>当前是否整窗点击穿透（托盘菜单要读它）。</summary>
    internal bool ClickThrough => _clickThrough;

    /// <summary>设置面板直接改这份对象，改完走 UpdateSettings 生效并落盘。</summary>
    internal AppSettings Settings => _settings;

    /// <summary>设置面板上的账本读数（null = 无数据）。</summary>
    internal UsageSnapshot? Usage => _usage;

    internal string UsagePath => _usageWatcher?.Path ?? UsageLedger.LedgerPath;

    internal bool UsageWatching => _usageWatcher?.IsWatching == true;

    /// <summary>任务面板是否已接上心跳（现在恒为 false，设置面板据此显示「未连接」）。</summary>
    internal bool TasksConnectedNow => TasksConnected;

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

        // 布局算完之后把窗口裁成胶囊形状，形状外一律穿透
        Loaded += (_, _) =>
        {
            UpdateWindowRegion();
            // 句柄就绪后才知道自己在哪个显示器上，这时把屏幕名补进配置
            SavePlacement();
        };

        // 悬停展开延迟：0 时直接展开，定时器不启动
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
        // 模块顺序/开关、账本快照、媒体快照都可能变，统一在这里重排岛上文字
        RefreshIslandText();
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

        MoveTo(Placement.Resolve(monitors, _settings.Screen, saved, WindowPixelSize(scale), scale));

        _settings.Left = null;
        _settings.Top = null;
    }

    private void SavePlacement()
    {
        if (_restoringPlacement)
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
        // 任务面板没有接收端时整体隐藏，不留「任务 0 项」这种空壳
        ids.RemoveAll(id => id == IslandModuleIds.Tasks && !TasksConnected);

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

        Line1.Text = ModuleTitle(primary);

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
    }

    /// <summary>主行文字：媒体只放标题，详情留给副行，免得 280px 里全被省略号吃掉。</summary>
    private string ModuleTitle(string id) => id switch
    {
        IslandModuleIds.Usage => UsageLedger.UsageLine(_usage),
        IslandModuleIds.Quota => UsageLedger.QuotaLine(_usage),
        IslandModuleIds.Media => mediaTitleLine(),
        IslandModuleIds.Tasks => TasksConnected ? "任务：已连接" : "",
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

        // 句柄就绪、能问出"自己在哪块屏、什么缩放"之后，按设备像素落位
        RestorePlacementPixels();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotKeyId);
        }
        _hwndSource?.RemoveHook(WndProc);
        // 托盘图标必须先撤掉，否则进程退出后托盘里留幽灵图标
        _tray?.Dispose();
        _media?.Dispose();
        _usageWatcher?.Dispose();
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
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    UpdateWindowRegion();
                    SavePlacement();
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
            int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            if (on)
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
        StatusText.Text = _clickThrough
            ? $"点击穿透：开 · {hotKey} 关闭"
            : $"点击穿透：关 · {hotKey} 开启";
    }

    private void Island_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // 悬停延迟 0：直接展开，不必等定时器
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
        // 收起永远是立即的，不然鼠标走了它还挂着
        _hoverTimer.Stop();
        Animate(false);
    }

    private void Animate(bool expand)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 收起时只留主行：副行和状态行都会把 160×44 撑爆
        _expanded = expand;
        StatusText.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        Line2.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        RefreshIslandText(); // 主行内容随展开/收起切换（收起用设置里选的那一项）

        var widthAnim = new DoubleAnimation(expand ? ExpandedWidth : CollapsedWidth, AnimDuration)
        {
            EasingFunction = ease
        };
        widthAnim.Completed += (_, _) => StopRegionTracking();

        Island.BeginAnimation(WidthProperty, widthAnim);
        Island.BeginAnimation(HeightProperty,
            new DoubleAnimation(expand ? ExpandedHeight : CollapsedHeight, AnimDuration) { EasingFunction = ease });

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

    /// <summary>把窗口裁成与胶囊同形状的圆角矩形（区域外鼠标穿透）。</summary>
    private void UpdateWindowRegion()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        double islandW = Island.ActualWidth;
        double islandH = Island.ActualHeight;
        if (islandW <= 0 || islandH <= 0)
        {
            return;
        }

        // 岛在窗口里居中；窗口与岛的尺寸都是 DIP，SetWindowRgn 要设备像素
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double left = (ActualWidth - islandW) / 2;
        double top = (ActualHeight - islandH) / 2;
        double radius = Island.CornerRadius.TopLeft; // 圆角半径（DIP），区域里要的是直径

        int x1 = (int)Math.Round(left * dpi.DpiScaleX);
        int y1 = (int)Math.Round(top * dpi.DpiScaleY);
        int x2 = (int)Math.Round((left + islandW) * dpi.DpiScaleX);
        int y2 = (int)Math.Round((top + islandH) * dpi.DpiScaleY);
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
