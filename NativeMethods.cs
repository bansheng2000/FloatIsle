using System;
using System.Runtime.InteropServices;

namespace FloatIsle;

/// <summary>Win32 互操作：窗口扩展样式与全局热键。</summary>
internal static class NativeMethods
{
    // GetWindowLong / SetWindowLong 的索引：读取/写入窗口扩展样式（WS_EX_*）
    internal const int GWL_EXSTYLE = -20;

    // ---- 扩展样式位 ----
    internal const int WS_EX_TRANSPARENT = 0x00000020; // 鼠标穿透：点击落到下面的窗口（需与 LAYERED 同时存在）
    internal const int WS_EX_TOOLWINDOW = 0x00000080;  // 工具窗口：不进 Alt+Tab、不占任务栏按钮
    internal const int WS_EX_LAYERED = 0x00080000;     // 分层窗口：WPF 透明窗口自己就在用，穿透也依赖它
    internal const int WS_EX_NOACTIVATE = 0x08000000;  // 显示/点击都不激活窗口，不抢前台焦点

    // ---- SetWindowPos 行为标志 ----
    internal const uint SWP_NOSIZE = 0x0001;       // 保持宽高不变
    internal const uint SWP_NOMOVE = 0x0002;       // 保持位置不变
    internal const uint SWP_NOZORDER = 0x0004;     // 保持 Z 序不变
    internal const uint SWP_NOACTIVATE = 0x0010;   // 不激活窗口（关键：否则会抢走正在输入的焦点）
    internal const uint SWP_FRAMECHANGED = 0x0020; // 让刚写入的扩展样式立即生效

    // ---- RegisterHotKey 参数 ----
    internal const uint MOD_ALT = 0x0001;     // Alt 修饰键
    internal const uint MOD_CONTROL = 0x0002; // Ctrl 修饰键
    internal const uint VK_I = 0x49;          // 虚拟键码 0x49 = 字母 I

    // 窗口消息：注册的热键被按下
    internal const int WM_HOTKEY = 0x0312;

    // ---- 全屏 / 锁屏判定（全屏让位用）----
    // SHQueryUserNotificationState 返回 QUERY_USER_NOTIFICATION_STATE（shellapi.h，见下方常量）
    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern int SHQueryUserNotificationState(out int state);

    internal const int QUNS_NOT_PRESENT = 1;             // 屏保/锁屏/非活动快速用户切换会话
    internal const int QUNS_BUSY = 2;                    // 有全屏应用在跑，或开了演示设置（实测无边框全屏窗口=2）
    internal const int QUNS_RUNNING_D3D_FULL_SCREEN = 3; // 独占全屏 D3D 应用（游戏）
    internal const int QUNS_PRESENTATION_MODE = 4;       // 用户手动开了演示模式
    internal const int QUNS_ACCEPTS_NOTIFICATIONS = 5;   // 正常桌面
    internal const int QUNS_QUIET_TIME = 6;              // 新用户首小时的安静时段
    internal const int QUNS_APP = 7;                     // 有商店应用在前台

    // ---- 前台窗口变化事件钩子 ----
    internal const int EVENT_SYSTEM_FOREGROUND = 0x0003; // 前台窗口发生变化
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;  // 回调投递到安装钩子的那个线程的消息队列
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // 本进程自己的窗口事件不回调

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WinEventProc(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ---- 前台窗口几何（用来复核"全屏应用"是否真存在）----
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002; // MonitorFromWindow：窗口不在任何显示器上时取最近的

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;      // 结构大小，调用前必须填
        public RECT rcMonitor;  // 显示器整块区域（含任务栏）
        public RECT rcWork;     // 工作区（不含任务栏）
        public uint dwFlags;    // 1 = 主显示器
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    // ---- 窗口区域（异形命中）----
    // CreateRoundRectRgn 的 (x1,y1,x2,y2) 用窗口客户区坐标，(w,h) 是圆角椭圆的宽高（不是半径）

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    // 把窗口裁成给定区域：区域内可点、区域外鼠标穿透，同时会裁掉绘制内容（阴影也在其中）
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    // 只有 SetWindowRgn 失败时才需要我们释放区域；成功后 region 归系统所有
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);


    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- 高 DPI（Per-Monitor V2）----
    // 显示器缩放变化（或窗口移到别的缩放显示器）时发来这条，lParam 指向建议窗口矩形（物理像素）
    internal const int WM_DPICHANGED = 0x02E0;

    // 窗口所在显示器的 DPI（96 = 100%）。PMv2 下这就是"当前缩放"的唯一来源
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    // 系统/主屏 DPI：句柄还没建时常它做初始换算（老配置的 DIP 坐标迁移用）
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    // ---- 拖动（胶囊上按住左键拖）----
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    // 光标位置：PMv2 进程里返回物理像素（与 GetWindowRect 同一坐标空间）
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetCursorPos(out POINT pt);

    // ---- 贴顶模式：全局低级鼠标钩子 ----
    internal const int WH_MOUSE_LL = 14;      // 低级鼠标钩子（不需要 DLL，回调在本进程）
    internal const int WM_MOUSEMOVE = 0x0200; // 钩子里的鼠标移动
    internal const int WM_TOPWAKE = 0x8001;   // WM_APP + 1：钩子叫 UI 线程"该开面板了"

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;           // 屏幕坐标（物理像素）
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // hMod 传自己模块的句柄（低级钩子不需要注入别的进程，但文档要求非零更稳）
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // ---- 钩子专用消息泵（低级钩子必须在装了钩子的那个线程上泵消息）----
    internal const int WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    internal const uint PM_REMOVE = 0x0001;

    [DllImport("user32.dll")]
    internal static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool WaitMessage();

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    // 钩子回调里不能直接动界面（会卡住全系统鼠标输入），只投递一条消息回 UI 线程
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- 贴顶模式：光标在哪块屏、那块屏的缩放 ----
    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    // Shcore.GetDpiForMonitor 的第二个参数：0 = MDT_EFFECTIVE_DPI（生效缩放，不是系统建议值）
    [DllImport("Shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
