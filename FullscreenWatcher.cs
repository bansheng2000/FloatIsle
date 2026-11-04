using System;
using System.Windows.Threading;

namespace FloatIsle;

/// <summary>
/// 全屏让位：判断"当前是否不该打扰"，前台窗口一变就查一次（SetWinEventHook），
/// 另配一个 1 秒兜底定时器（微软文档明说"启动/结束全屏应用时不会发通知"，
/// 同一窗口内切全屏不会有前台变化事件，只能靠定时器补）。
///
/// 除 SHQueryUserNotificationState 外还要看前台窗口是否真的盖满显示器：
/// 本机实测同一份 exe 放在不同目录里，这个 API 会稳定返回 QUNS_BUSY(2)（见 README 已知限制），
/// 只看返回值会误隐藏，所以 2/3 这两个"全屏"状态要再过一道窗口几何校验。
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    // 兜底间隔：1 秒。每 tick 只是一次 shell 调用 + 一次 GetForegroundWindow，微秒级。
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromMilliseconds(1000);

    private readonly NativeMethods.WinEventProc _winEventProc; // 必须存字段：委托被 GC 回收后原生回调地址就失效
    private readonly DispatcherTimer _timer;
    private IntPtr _hook;
    private bool _suppressed;

    /// <summary>true = 进入全屏/锁屏，岛应该让位；false = 恢复。</summary>
    internal event Action<bool>? SuppressChanged;

    internal FullscreenWatcher()
    {
        _winEventProc = OnWinEvent;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FallbackInterval
        };
        _timer.Tick += (_, _) => Evaluate();
    }

    internal IntPtr HookHandle => _hook;

    internal void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return; // 已经挂着钩子了，重复 Start 不重装
        }

        // 只关心前台窗口变化；OUTOFCONTEXT 让回调进本线程的消息队列（不用跨线程封送）
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        Evaluate();
        _timer.Start();
    }

    /// <summary>关掉监视（设置里可以关全屏让位）：卸钩子停定时器，之后不再发事件。</summary>
    internal void Stop()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _suppressed = false;
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime) => Evaluate();

    private void Evaluate()
    {
        bool shouldSuppress = IsSuppressed();
        if (shouldSuppress == _suppressed)
        {
            return;
        }
        _suppressed = shouldSuppress;
        SuppressChanged?.Invoke(shouldSuppress);
    }

    /// <summary>1=锁屏/屏保；2=全屏应用；3=独占全屏游戏。2/3 还要确认真有全屏窗口。</summary>
    private static bool IsSuppressed()
    {
        if (NativeMethods.SHQueryUserNotificationState(out int state) != 0)
        {
            return false; // 查询失败就当正常，不误隐藏
        }

        return state switch
        {
            NativeMethods.QUNS_NOT_PRESENT => true,
            NativeMethods.QUNS_BUSY or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN => IsForegroundFullscreen(),
            _ => false,
        };
    }

    /// <summary>前台窗口是否盖满它所在的显示器（无边框全屏/独占全屏都成立）。</summary>
    private static bool IsForegroundFullscreen()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }
        if (!NativeMethods.GetWindowRect(fg, out NativeMethods.RECT r))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
        {
            return false;
        }

        return r.Left <= mi.rcMonitor.Left
            && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right
            && r.Bottom >= mi.rcMonitor.Bottom;
    }

    public void Dispose() => Stop();
}
