using System;
using System.Runtime.InteropServices;

namespace FloatIsle;

/// <summary>
/// 贴顶模式的唤醒钩子：WH_MOUSE_LL 全局低级鼠标钩子。
/// 回调里**只做**「nCode 判断 + y 与阈值比较 + 必要时 PostMessage」三件事；
/// 建控件、读账本、算显示器这些一律回到 UI 线程的 WndProc 里做——
/// 钩子会串行挡住全系统的鼠标输入，在里面干重活会让别人的鼠标都卡。
/// </summary>
internal sealed class TopEdgeHook : IDisposable
{
    private readonly IntPtr _hwnd;

    // 委托必须留字段：被 GC 掉的话钩子回调就成了野指针，鼠标一动就崩进程
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hook;

    internal TopEdgeHook(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = Callback;
    }

    /// <summary>阈值（物理像素）：光标 y 小于等于它算「碰到顶边」。按当前显示器缩放折算后由调用方写入。</summary>
    internal int ThresholdPx { get; set; } = 6;

    /// <summary>面板开着（或正在开/关）：钩子不再重复投消息，避免鼠标停在顶部时消息洪水。</summary>
    internal bool Open { get; set; }

    /// <summary>全屏让位期间挂钩子也不该把面板叫出来。</summary>
    internal bool Suppressed { get; set; }

    internal bool IsHooked => _hook != IntPtr.Zero;

    // 低级钩子的回调只在「装钩子的那个线程」上被调用，且那个线程必须自己泵消息。
    // 放一个独立后台线程专管钩子：UI 线程忙（动画、布局）时钩子照收，
    // 回调里只 PostMessage 回 UI 线程，不做任何重活。
    private System.Threading.Thread? _thread;
    private int _threadId;

    internal void Start()
    {
        if (_thread != null)
        {
            return;
        }
        _thread = new System.Threading.Thread(Pump)
        {
            IsBackground = true,
            Name = "FloatIsle-TopEdgeHook",
        };
        _thread.Start();
        for (int i = 0; i < 100 && _hook == IntPtr.Zero && _threadId == 0; i++)
        {
            System.Threading.Thread.Sleep(3);
        }
    }

    private void Pump()
    {
        _threadId = (int)NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            while (true)
            {
                NativeMethods.WaitMessage();
                while (NativeMethods.PeekMessage(
                    out NativeMethods.MSG msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
                {
                    if (msg.message == NativeMethods.WM_QUIT)
                    {
                        return;
                    }
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }
            }
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    internal void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage((uint)_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _threadId = 0;
        _thread = null;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0
            && !Open
            && !Suppressed
            && wParam == (IntPtr)NativeMethods.WM_MOUSEMOVE
            && Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam).pt.Y <= ThresholdPx)
        {
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_TOPWAKE, IntPtr.Zero, IntPtr.Zero);
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
