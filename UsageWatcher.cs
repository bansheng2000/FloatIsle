using System;
using System.IO;
using System.Windows.Threading;

namespace FloatIsle;

/// <summary>
/// 账本变化的唯一入口：FileSystemWatcher + 2 秒去抖。
/// 不用定时器轮询——空闲时它一个 Tick 都不会跑，岛的 0ms CPU 指标才守得住。
/// 账本是批量写的，一次保存会连发多个 Changed，所以必须去抖成一次读。
/// </summary>
internal sealed class UsageWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>去抖结束后触发一次（UI 线程）。</summary>
    internal event Action? Changed;

    internal string Path { get; }

    /// <summary>目录不存在/无权监视时为 false：此时只能显示启动时读到的快照。</summary>
    internal bool IsWatching => _watcher is not null;

    internal UsageWatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Path = UsageLedger.LedgerPath;

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke();
        };

        try
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }

            _watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(Path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                    | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnAny;
            _watcher.Created += OnAny;
            _watcher.Renamed += OnAny;
            // 目录被删/缓冲溢出：只是以后读不到变化，不能让它把进程带崩
            _watcher.Error += (_, _) => { };
        }
        catch
        {
            _watcher = null;
        }
    }

    private void OnAny(object sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }
        // 事件在线程池线程上来，DispatcherTimer 只能在 UI 线程碰
        _dispatcher.BeginInvoke(new Action(Restart), DispatcherPriority.Background);
    }

    private void Restart()
    {
        if (_disposed)
        {
            return;
        }
        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _debounce.Stop();
        Changed = null;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnAny;
            _watcher.Created -= OnAny;
            _watcher.Renamed -= OnAny;
            _watcher.Dispose();
        }
    }
}
