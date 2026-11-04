using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FloatIsle;

/// <summary>任务行状态。顺序也是显示优先级（等人比在跑更需要看见）。</summary>
internal enum TaskState
{
    Question,
    Approval,
    Running,
    Ended
}

/// <summary>会话表里的一行。只在 UI 线程读写。</summary>
internal sealed class TaskRow
{
    public string Sid { get; init; } = "";

    public string Workspace { get; set; } = "";

    public TaskState State { get; set; } = TaskState.Running;

    public string Detail { get; set; } = "";

    /// <summary>turn/end 的时刻，用来算「刚结束」还要留几秒。</summary>
    public DateTime EndedAt { get; set; }

    public string StateText => State switch
    {
        TaskState.Question => "等你回答",
        TaskState.Approval => "等你批准",
        TaskState.Ended => "刚结束",
        _ => "在跑"
    };
}

/// <summary>
/// 任务面板的接收端：只绑 127.0.0.1，只收 POST /event。
/// 用 TcpListener 手写最小 HTTP/1.1，不用 HttpListener —— 后者要 URL ACL，普通用户装不上。
/// 空闲时没有任何轮询：只有一条「最后事件 + 25 秒」的一次性定时器和 5 秒淡出定时器。
/// </summary>
internal sealed class TasksHub : IDisposable
{
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan EndedLinger = TimeSpan.FromSeconds(5);

    /// <summary>请求体上限（Chief 定的 64 KB），超了回 413。</summary>
    private const int MaxBodyBytes = 64 * 1024;

    private const int MaxHeaderBytes = 8 * 1024;

    private static readonly HttpClient JumpClient = new() { Timeout = TimeSpan.FromSeconds(1) };

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, TaskRow> _rows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timeoutTimer;
    private readonly DispatcherTimer _lingerTimer;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private DateTime _lastSeen = DateTime.MinValue;
    private DateTime _lastHeartbeat = DateTime.MinValue;
    private bool _sawAnyEvent;
    private bool _timedOut;
    private bool _listening;
    private string _endpoint = "";
    private string _listenStatus = "未启动";
    private bool _disposed;
    private string? _notice;

    /// <summary>会话表/连接状态变化（UI 线程回调，MainWindow 据此重排岛上文字）。</summary>
    internal event Action? Changed;

    /// <summary>有会话以 <c>completed</c> 结束（完成闪烁/常亮用）。</summary>
    internal event Action? Completed;

    internal TasksHub(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        // 「刚结束」留 5 秒的定时器（一次扫掉所有到期行，不是每个会话一个）
        _lingerTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = EndedLinger };
        _lingerTimer.Tick += (_, _) =>
        {
            _lingerTimer.Stop();
            SweepEnded();
        };

        // 一次性定时器：收到心跳/事件才重置到期时间，不是轮询
        _timeoutTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = HeartbeatTimeout };
        _timeoutTimer.Tick += (_, _) =>
        {
            _timeoutTimer.Stop();
            _timedOut = true;
            _rows.Clear();
            _lingerTimer.Stop();
            RaiseChanged();
        };
    }

    /// <summary>25 秒内有心跳/事件才算连着（任务面板与设置里的联动栏共用这个判定）。</summary>
    internal bool Connected => _sawAnyEvent && !_timedOut;

    internal bool IsListening => _listening;

    internal DateTime? LastSeenAt => _sawAnyEvent ? _lastSeen : null;

    /// <summary>插件给的用户可见提示（心跳 notice），null 表示没有；点击成功后会清掉。</summary>
    internal string? Notice => _notice;

    internal void ClearNotice()
    {
        if (_notice is null)
        {
            return;
        }
        _notice = null;
        RaiseChanged();
    }

    internal string Endpoint => _endpoint;

    /// <summary>人话状态：绑定失败、25 秒超时、正常都在这里，设置面板与岛上共用同一份文案。</summary>
    internal string StatusText
    {
        get
        {
            if (!_listening)
            {
                return _listenStatus;
            }
            if (!_sawAnyEvent)
            {
                return $"未连接 · {_listenStatus} 上还没收到心跳";
            }
            if (_timedOut)
            {
                return $"DSH 未运行 · {(int)(DateTime.Now - _lastSeen).TotalSeconds} 秒无心跳（{_listenStatus}）";
            }
            int seconds = Math.Max(0, (int)(DateTime.Now - _lastSeen).TotalSeconds);
            string what = _lastHeartbeat > DateTime.MinValue ? "最后心跳" : "最后事件";
            return $"已连接 · {what} {seconds} 秒前（{_listenStatus}）";
        }
    }

    /// <summary>岛上状态行用的短文案（280 DIP 要装得下）：只报「连不上/端口被占」，连上了不占地方。</summary>
    internal string ShortStatus
    {
        get
        {
            if (!_listening)
            {
                return "任务面板不可用";
            }
            // 从没连上过时不挂在岛上（否则每次开机都顶一句「未连接」）；连过又静默才算 DSH 未运行
            return _timedOut ? "DSH 未运行" : "";
        }
    }

    /// <summary>排在前面的是更需要人看的：等你回答 → 等你批准 → 在跑 → 刚结束。</summary>
    internal IReadOnlyList<TaskRow> Snapshot() => _rows.Values
        .OrderBy(r => r.State)
        .ThenBy(r => r.Workspace, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>在跑的会话数（「刚结束」不算，收起态就显示这个数）。</summary>
    internal int ActiveCount => _rows.Values.Count(r => r.State != TaskState.Ended);

    /// <summary>（重新）在 endpoint 上监听。端口被占用不抛异常，写进状态文字。</summary>
    internal void Start(string endpoint)
    {
        StopListening();

        _endpoint = (endpoint ?? "").Trim();
        _rows.Clear();
        _sawAnyEvent = false;
        _timedOut = false;
        _timeoutTimer.Stop();
        _lingerTimer.Stop();

        if (!TryParsePort(_endpoint, out int port))
        {
            _listening = false;
            _listenStatus = $"地址无效（{_endpoint}），任务面板不可用";
            RaiseChanged();
            return;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start(16);
            _listener = listener;
            _cts = new CancellationTokenSource();
            _listening = true;
            _listenStatus = $"监听 127.0.0.1:{port}";
            _ = AcceptLoopAsync(listener, _cts.Token);
        }
        catch (Exception ex)
        {
            _listener = null;
            _listening = false;
            _listenStatus = ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                ? $"{port} 被占用，任务面板不可用"
                : $"绑定 127.0.0.1:{port} 失败（{ex.Message}），任务面板不可用";
        }

        RaiseChanged();
    }

    /// <summary>点任务行：把 sid 发给插件的跳转端口。1 秒超时，失败静默（插件没跑也不该弹窗）。</summary>
    internal static void Jump(string endpoint, string sid)
    {
        if (!TryParsePort(endpoint, out int port) || string.IsNullOrEmpty(sid))
        {
            return;
        }

        string json = JsonSerializer.Serialize(new { sid });
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage _ = await JumpClient
                    .PostAsync($"http://127.0.0.1:{port}/jump", content).ConfigureAwait(false);
            }
            catch
            {
                // 跳转只是锦上添花：端口不通/插件没装都当无事发生
            }
        });
    }

    internal static bool TryParsePort(string endpoint, out int port)
    {
        port = 0;
        string text = (endpoint ?? "").Trim();
        int colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            text = text[(colon + 1)..];
        }
        return int.TryParse(text, out port) && port is > 0 and < 65536;
    }

    // ---------- 接收 ----------

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }

            _ = HandleAsync(client, token);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            CancellationToken ct = timeout.Token;
            NetworkStream stream = client.GetStream();
            try
            {
                (HttpRequest? request, int status) = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
                if (request is not { } req)
                {
                    await WriteStatusAsync(stream, status, ct).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteStatusAsync(stream, 405, ct).ConfigureAwait(false);
                    return;
                }
                if (req.Path != "/event")
                {
                    await WriteStatusAsync(stream, 404, ct).ConfigureAwait(false);
                    return;
                }

                TaskEvent? ev = Parse(req.Body);
                if (ev is null)
                {
                    await WriteStatusAsync(stream, 400, ct).ConfigureAwait(false);
                    return;
                }

                _ = _dispatcher.BeginInvoke(new Action(() => Apply(ev.Value)));
                await WriteStatusAsync(stream, 200, ct).ConfigureAwait(false);
            }
            catch
            {
                // 客户端半路断开/超时/畸形请求：一律回 400（写不出去就算了），绝不弹窗刷日志。
                // 空回复会让发送端误以为端口没开，所以能回就回一个状态码。
                try
                {
                    await WriteStatusAsync(stream, 400, ct).ConfigureAwait(false);
                }
                catch
                {
                    // 连接已经断了
                }
            }
        }
    }

    private readonly record struct HttpRequest(string Method, string Path, byte[] Body);

    /// <summary>
    /// 读一条请求。返回 (null, 状态码) 表示直接回错误；(请求, 200) 才继续分发。
    /// 异步方法不能带 out 参数，所以用元组返回。
    /// </summary>
    private static async Task<(HttpRequest? Request, int Status)> ReadRequestAsync(
        NetworkStream stream, CancellationToken ct)
    {
        // 按字节收请求头：可能一次读到一部分正文，剩下的要当成 body 的开头
        var head = new List<byte>(1024);
        var buf = new byte[1024];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
            if (n <= 0)
            {
                return (null, 400);
            }
            for (int i = 0; i < n; i++)
            {
                head.Add(buf[i]);
            }
            if (head.Count > MaxHeaderBytes)
            {
                return (null, 400);
            }
            headerEnd = FindHeaderEnd(head);
        }

        string headerText = Encoding.ASCII.GetString(head.ToArray(), 0, headerEnd);
        List<byte> rest = head.GetRange(headerEnd + 4, head.Count - headerEnd - 4);

        string[] lines = headerText.Split("\r\n");
        string[] requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3 || !requestLine[2].StartsWith("HTTP/", StringComparison.Ordinal))
        {
            return (null, 400);
        }
        string method = requestLine[0];
        string path = requestLine[1];

        int contentLength = 0;
        bool expectContinue = false;
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            string name = lines[i].Substring(0, colon).Trim();
            string value = lines[i].Substring(colon + 1).Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out contentLength) || contentLength < 0)
                {
                    return (null, 400);
                }
            }
            else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase)
                     && value.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
            {
                expectContinue = true;
            }
        }

        if (contentLength > MaxBodyBytes)
        {
            return (null, 413);
        }

        if (expectContinue)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), ct)
                .ConfigureAwait(false);
        }

        byte[] body = new byte[contentLength];
        int have = Math.Min(rest.Count, contentLength);
        rest.CopyTo(0, body, 0, have);
        while (have < contentLength)
        {
            int n = await stream.ReadAsync(body.AsMemory(have, contentLength - have), ct).ConfigureAwait(false);
            if (n <= 0)
            {
                return (null, 400);
            }
            have += n;
        }

        return (new HttpRequest(method, path, body), 200);
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == 13 && bytes[i - 2] == 10 && bytes[i - 1] == 13 && bytes[i] == 10)
            {
                return i - 3;
            }
        }
        return -1;
    }

    private static async Task WriteStatusAsync(NetworkStream stream, int code, CancellationToken ct)
    {
        string text = code switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            _ => "Error"
        };
        string payload = code == 200 ? "ok" : text;
        string response = $"HTTP/1.1 {code} {text}\r\n"
                          + "Content-Type: text/plain; charset=us-ascii\r\n"
                          + $"Content-Length: {payload.Length}\r\n"
                          + "Connection: close\r\n\r\n"
                          + payload;
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct).ConfigureAwait(false);
    }

    // ---------- 事件 ----------

    private readonly record struct TaskEvent(
        string Type,
        string Sid,
        string Workspace,
        string Reason,
        string Brief,
        string Tool,
        string[]? Running,
        Dictionary<string, string>? Names,
        string? Notice);

    private static TaskEvent? Parse(byte[] body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string type = Str(root, "type");
            if (type == "heartbeat")
            {
                var running = new List<string>();
                if (root.TryGetProperty("running", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in arr.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } sid)
                        {
                            running.Add(sid);
                        }
                    }
                }
                return new TaskEvent("heartbeat", "", "", "", "", "", running.ToArray(), ParseNames(root), NullableStr(root, "notice"));
            }

            if (type is not ("turn/start" or "turn/end" or "question" or "approval"))
            {
                return null;
            }

            string id = Str(root, "sid");
            if (id.Length == 0)
            {
                return null;
            }

            return new TaskEvent(type, id, Str(root, "ws"), Str(root, "reason"),
                Str(root, "brief"), Str(root, "tool"), null, null, null);
        }
        catch (Exception)
        {
            // 坏 JSON（含非法 UTF-8）都算 400；这是信任边界，不允许任何解析异常冒到连接层
            return null;
        }
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>可能为 null 的字符串字段（心跳的 <c>notice</c>）：null/缺省/非字符串都当没有。</summary>
    private static string? NullableStr(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>心跳里的可选 <c>names</c>：<c>{"sid": "工作区名"}</c>，只给还没有 ws 的会话兜底。</summary>
    private static Dictionary<string, string>? ParseNames(JsonElement root)
    {
        if (!root.TryGetProperty("names", out JsonElement names) || names.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in names.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                property.Value.GetString() is { Length: > 0 } ws)
            {
                result[property.Name] = ws;
            }
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>UI 线程：落一条事件并重排任务表。</summary>
    private void Apply(TaskEvent ev)
    {
        DateTime now = DateTime.Now;
        _sawAnyEvent = true;
        _timedOut = false;
        _lastSeen = now;
        if (ev.Type == "heartbeat")
        {
            _lastHeartbeat = now;
            _notice = ev.Notice; // null 就清掉：插件说清原因，我们只负责显示
        }
        _timeoutTimer.Stop();
        _timeoutTimer.Start();

        if (ev.Type == "heartbeat")
        {
            // running 是权威：不在里面的会话立刻移除（「刚结束」按 5 秒淡出，不受心跳影响）
            var running = new HashSet<string>(ev.Running ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (string sid in _rows
                         .Where(p => !running.Contains(p.Key) && p.Value.State != TaskState.Ended)
                         .Select(p => p.Key)
                         .ToList())
            {
                _rows.Remove(sid);
            }
            foreach (string sid in running)
            {
                if (_rows.TryGetValue(sid, out TaskRow? row))
                {
                    if (row.State == TaskState.Ended)
                    {
                        row.State = TaskState.Running;
                    }
                    // turn/start 没带 ws 的会话，用心跳 names 里工作区名兜底
                    if (row.Workspace.Length == 0)
                    {
                        row.Workspace = WorkspaceOf(ev.Names, sid);
                    }
                }
                else
                {
                    _rows[sid] = new TaskRow { Sid = sid, Workspace = WorkspaceOf(ev.Names, sid) };
                }
            }
            RaiseChanged();
            return;
        }

        TaskRow target = Upsert(ev.Sid, ev.Workspace);
        switch (ev.Type)
        {
            case "turn/start":
                target.State = TaskState.Running;
                target.Detail = "";
                break;
            case "turn/end":
                target.State = TaskState.Ended;
                target.Detail = ev.Reason;
                target.EndedAt = now;
                _lingerTimer.Stop();
                _lingerTimer.Start();
                if (ev.Reason == "completed")
                {
                    Completed?.Invoke();
                }
                break;
            case "question":
                target.State = TaskState.Question;
                target.Detail = FirstLine(ev.Brief);
                break;
            case "approval":
                target.State = TaskState.Approval;
                target.Detail = ev.Tool;
                break;
        }

        RaiseChanged();
    }

    private TaskRow Upsert(string sid, string workspace)
    {
        if (!_rows.TryGetValue(sid, out TaskRow? row))
        {
            row = new TaskRow { Sid = sid };
            _rows[sid] = row;
        }
        if (workspace.Length > 0)
        {
            row.Workspace = workspace;
        }
        return row;
    }

    private static string WorkspaceOf(Dictionary<string, string>? names, string sid) =>
        names is not null && names.TryGetValue(sid, out string? ws) ? ws : "";

    private static string FirstLine(string text)
    {
        int index = text.IndexOfAny(new[] { '\r', '\n' });
        string line = index >= 0 ? text[..index] : text;
        return line.Length > 60 ? line[..60] : line;
    }

    /// <summary>「刚结束」留 5 秒再消失（一次定时器扫掉所有到期的，不是每个会话一个）。</summary>
    private void SweepEnded()
    {
        DateTime now = DateTime.Now;
        foreach (string sid in _rows
                     .Where(p => p.Value.State == TaskState.Ended && now - p.Value.EndedAt >= EndedLinger)
                     .Select(p => p.Key)
                     .ToList())
        {
            _rows.Remove(sid);
        }
        if (_rows.Values.Any(r => r.State == TaskState.Ended))
        {
            _lingerTimer.Start();
        }
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();

    // ---------- 生命周期 ----------

    private void StopListening()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // 已释放
        }
        _cts?.Dispose();
        _cts = null;
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // 已停止
        }
        _listener = null;
        _listening = false;
        _listenStatus = "未启动";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        StopListening();
        _timeoutTimer.Stop();
        _lingerTimer.Stop();
        _rows.Clear();
        Changed = null;
    }
}



