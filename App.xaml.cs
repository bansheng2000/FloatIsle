using System;
using System.Windows;

namespace FloatIsle;

// WinForms 的隐式 using 会和 WPF 的 Application 撞名，这里写全名
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 位置解析自检：不开窗口、纯函数，跑完直接退出。
        // 用法：FloatIsle.exe --selftest <报告文件>；全过退出码 0，有失败 1。
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            Shutdown(PlacementSelfTest.Run(e.Args[1]));
            return;
        }

        // App.xaml 里不再写 StartupUri：要先把 --selftest 拦下来，再决定开不开主窗口
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
