using System;
using System.Threading;
using System.Windows;

namespace FloatIsle;

// WinForms 的隐式 using 会和 WPF 的 Application 撞名，这里写全名
public partial class App : System.Windows.Application
{
    // 单实例互斥：第二个实例直接退出（不做「激活已有实例」的 IPC）。
    // 两个实例共享 %APPDATA%\FloatIsle\settings.json，会互相盖位置、抢端口，用户已经看到过怪行为。
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 位置解析自检：不开窗口、纯函数，跑完直接退出。
        // 用法：FloatIsle.exe --selftest <报告文件>；全过退出码 0，有失败 1。
        // 自检不拿互斥，装了正在跑的实例时也能跑。
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            Shutdown(PlacementSelfTest.Run(e.Args[1]));
            return;
        }

        _mutex = new Mutex(true, @"Global\FloatIsle.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        // App.xaml 里不再写 StartupUri：要先把 --selftest 拦下来，再决定开不开主窗口
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
            _mutex = null;
        }

        base.OnExit(e);
    }
}
