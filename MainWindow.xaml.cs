using System;
using System.Linq;
using System.Windows;
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

    /// <summary>当前是否整窗点击穿透（托盘菜单要读它）。</summary>
    internal bool ClickThrough => _clickThrough;

    /// <summary>设置面板直接改这份对象，改完走 UpdateSettings 生效并落盘。</summary>
    internal AppSettings Settings => _settings;

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

        // 位置（DIP）：存过就用存的，但夹进工作区，换分辨率后也不会跑到看不见的地方
        RestorePlacement();

        // 初始是收起态，状态行先藏起来（否则那行长文字会撑出胶囊）
        StatusText.Visibility = Visibility.Collapsed;
        MediaSubtitle.Visibility = Visibility.Collapsed;

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

    /// <summary>复位到顶部居中，并把实际坐标写回配置。</summary>
    internal void ResetPlacement()
    {
        _settings.Left = null;
        _settings.Top = null;

        Rect wa = SystemParameters.WorkArea;
        _restoringPlacement = true;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + 6;
        _restoringPlacement = false;

        SavePlacement();
    }

    /// <summary>设置面板上显示的位置说明（屏幕设备名 + 坐标）。</summary>
    internal string PlacementDescription()
    {
        string screen = CurrentScreenName() is string name && name.Length > 0
            ? System.IO.Path.GetFileName(name)
            : "未识别";
        return $"屏幕 {screen} · 左上角 {Left:F0},{Top:F0}（舞台 {ActualWidth:F0}×{ActualHeight:F0} DIP）";
    }

    private void RestorePlacement()
    {
        Rect wa = SystemParameters.WorkArea;
        double left = wa.Left + (wa.Width - Width) / 2;
        double top = wa.Top + 6;

        if (_settings.Left is double savedLeft && _settings.Top is double savedTop)
        {
            left = Math.Clamp(savedLeft, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            top = Math.Clamp(savedTop, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        }

        _restoringPlacement = true;
        Left = left;
        Top = top;
        _restoringPlacement = false;
    }

    private void SavePlacement()
    {
        if (_restoringPlacement)
        {
            return;
        }

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Screen = CurrentScreenName();
        SettingsStore.Save(_settings);
    }

    /// <summary>当前显示器设备名（\\.\DISPLAY1）。多屏还原未实现，只做记录。</summary>
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

    /// <summary>把一次媒体快照写到岛上；没有会话时显示「无媒体」，不保留上一首。</summary>
    private void UpdateMedia(MediaInfo info)
    {
        // 面板里关掉 SMTC 后，别让残留事件再把标题写回来
        if (!_settings.MediaEnabled)
        {
            MediaTitle.Text = "FloatIsle";
            MediaSubtitle.Text = "媒体信息已关闭";
            return;
        }

        bool hasMedia = !string.IsNullOrWhiteSpace(info.Title);
        MediaTitle.Text = hasMedia ? info.Title! : "FloatIsle";

        if (!hasMedia)
        {
            MediaSubtitle.Text = "无媒体";
            return;
        }

        string detail = string.Join(" · ", new[] { info.Artist, info.Status }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        MediaSubtitle.Text = string.IsNullOrEmpty(detail) ? "播放中" : detail;
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

        // 收起时只留标题，避免一行状态文字撑出胶囊
        StatusText.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        MediaSubtitle.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

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
