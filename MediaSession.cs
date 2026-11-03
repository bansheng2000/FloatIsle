using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace FloatIsle;

/// <summary>一次媒体快照。Title 为 null 表示没有会话。</summary>
internal readonly record struct MediaInfo(string? Title, string? Artist, string? Status);

/// <summary>
/// 只读媒体会话：订阅 SMTC 的三个事件（当前会话变化 / 媒体属性变化 / 播放状态变化），
/// 把标题、艺人、播放状态送到 UI。不做播放控制。
/// </summary>
internal sealed class MediaSession : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _refreshing;

    internal event Action<MediaInfo>? Updated;

    internal MediaSession(Dispatcher dispatcher) => _dispatcher = dispatcher;

    internal async void Start()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            AttachSession(_manager.GetCurrentSession());
            QueueRefresh();
        }
        catch
        {
            // 拿不到 SMTC（老系统 / 被策略禁掉 / 没有会话）：按"无媒体"处理，不弹窗、不抛
            Publish(new MediaInfo(null, null, null));
        }
    }

    // ---- SMTC 事件（三个都订；回调在后台线程，换订阅必须切回 UI 线程）----

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        _dispatcher.BeginInvoke(() =>
        {
            AttachSession(sender.GetCurrentSession());
            QueueRefresh();
        });
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => QueueRefresh();

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => QueueRefresh();

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    /// <summary>事件会连着来，合并成一次读取，避免一串并发的 TryGetMediaPropertiesAsync。</summary>
    private void QueueRefresh()
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_refreshing)
            {
                return;
            }
            _refreshing = true;
            _ = RefreshAsync();
        });
    }

    private async Task RefreshAsync()
    {
        GlobalSystemMediaTransportControlsSession? session = _session;
        try
        {
            if (session is null)
            {
                Publish(new MediaInfo(null, null, null));
                return;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties props = await session.TryGetMediaPropertiesAsync();
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
            Publish(new MediaInfo(props.Title, props.Artist, StatusText(info.PlaybackStatus)));
        }
        catch
        {
            // 会话在读的过程中被关掉：按无媒体处理，别把上一首留在界面上
            Publish(new MediaInfo(null, null, null));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Publish(MediaInfo info) => Updated?.Invoke(info);

    private static string StatusText(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "播放中",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "已暂停",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "已停止",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => "切换中",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => "已打开",
        _ => "未知",
    };

    public void Dispose()
    {
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
        AttachSession(null);
    }
}
