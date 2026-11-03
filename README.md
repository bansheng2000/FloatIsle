# FloatIsle

Windows 桌面「灵动岛」外壳（v0.2）：透明、无边框、置顶、**不抢焦点**的胶囊窗口。鼠标悬停展开，**只有胶囊形状本身可点，形状外一律穿透**；展开后显示当前 SMTC 媒体的标题/艺人/播放状态；托盘菜单管显示、穿透、自启、退出。

## 环境要求

- Windows 10 2004+ / Windows 11（SMTC 的 `GlobalSystemMediaTransportControlsSessionManager` 需要 10.0.19041 以上）
- .NET SDK 10.0，目标框架 `net10.0-windows10.0.19041.0`
- 零 NuGet 依赖（`nuget.config` 清空了包源）；托盘用框架自带 WinForms `NotifyIcon`，媒体信息用框架 + Windows SDK 投影

### 首次构建要联网一次

目标框架带了 Windows SDK 版本号（为了拿到 `Windows.Media.Control` 的 WinRT 投影），它会引一个**编译期**引用包 `Microsoft.Windows.SDK.NET.Ref`（约 75 MB）。这个包不在本机 SDK 里，所以：

```powershell
# 只在「首次构建 / 清过 nuget 缓存 / 换机器」时跑一次（本机需走代理时先设 HTTPS_PROXY）
$env:HTTPS_PROXY = 'http://127.0.0.1:7890'
dotnet restore --configfile nuget.online.config
```

跑完以后包进全局缓存 `%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.net.ref\<版本>`，**之后完全离线**（默认 `nuget.config` 是清空源的，正常 `dotnet build` 不会再碰网络）。

注意这只是一个**编译期**引用包：用户机器不需要装 Windows SDK，也不需要这个包。随程序出去的只有两个投影 DLL（见下）。

## 构建与运行

```powershell
dotnet build -c Release
dotnet run -c Release
# 或直接跑产物
.\bin\Release\net10.0-windows10.0.19041.0\FloatIsle.exe
```

输出目录比 v0.1 多两个 DLL（都随程序走，不是安装前置）：

| 文件 | 大小 | 用途 |
| --- | --- | --- |
| `Microsoft.Windows.SDK.NET.dll` | 24.8 MB | Windows SDK 的 CsWinRT 投影（含 SMTC 类型） |
| `WinRT.Runtime.dll` | 0.5 MB | CsWinRT 运行时 |

产物目录从 v0.1 的 202 KB 变成约 25 MB。`FloatIsle.runtimeconfig.json` 里只要求 `Microsoft.NETCore.App` + `Microsoft.WindowsDesktop.App`，没有任何 Windows SDK 依赖。

## 怎么用

| 操作 | 效果 |
| --- | --- |
| 鼠标移到胶囊上 | 从 160×44 展开到 320×120（180ms 缓动） |
| 鼠标移出 | 收回，收起态只显示第一行（媒体标题 / `FloatIsle`） |
| `Ctrl`+`Alt`+`I`（全局热键） | 切换整窗点击穿透；状态写在第三行 |
| 点在胶囊上 | 岛收下点击，但**不激活窗口、不抢焦点** |
| 点在胶囊外（哪怕在 380×180 舞台内） | 直接落到下面那个窗口 |
| 右键托盘图标 | 隐藏岛 / 点击穿透 / 开机自启 / 退出 |

展开态三行：媒体标题 → `艺人 · 播放状态`（无会话时是 `无媒体`）→ 穿透状态。

## 媒体信息（只读）

- 数据源：`GlobalSystemMediaTransportControlsSessionManager`（SMTC），只读标题、艺人、播放状态。
- 感知方式：**事件**，不是轮询。订阅 `CurrentSessionChanged`（换播放器）、`MediaPropertiesChanged`（换歌/改元数据）、`PlaybackInfoChanged`（播放/暂停），三者都切回 UI 线程处理（`Dispatcher.BeginInvoke`）。事件连着来时会合并成一次读取。
- 没有会话、读取过程中会话被关、拿不到 SMTC：一律显示 `无媒体`，不弹窗、不抛异常。
- 实测空闲 CPU（10 秒采样，`TotalProcessorTime` 差值）：播放中 0 ms、暂停 0 ms、无会话 0 ms。

## 开机自启

菜单里的「开机自启」只做一件事：在
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下写一个名为 `FloatIsle` 的 `REG_SZ`，内容是**当前 exe 的绝对路径并加引号**。

- 关掉时是**删除这个值**（`DeleteValue`），不是写空字符串。
- 程序退出**不会**删这个值。
- 手工核对：`reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FloatIsle`

**换目录/重装后自启会失效**（值里还是旧路径）。两种修法：

1. 在新位置启动一次，托盘菜单里把「开机自启」关掉再打开，值会被当前路径覆盖；
2. 或 `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FloatIsle /f`，再在新位置打开一次。

## 各块怎么实现的

1. **透明异形窗口**：`MainWindow.xaml:6-12`（`WindowStyle=None` + `AllowsTransparency` + 透明背景）；岛形是 `Border` 的 `CornerRadius=22`。
2. **置顶不抢焦点**：`MainWindow.xaml.cs` 在 `OnSourceInitialized` 里加 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，所有 `SetWindowPos` 都带 `SWP_NOACTIVATE`。
3. **异形命中**：`UpdateWindowRegion()` 用 `CreateRoundRectRgn` + `SetWindowRgn`，动画期间 `CompositionTarget.Rendering` 逐帧重设，末帧由 `DoubleAnimation.Completed` 补一次。
4. **悬停展开**：`Animate()`，动 `Border` 的 `Width/Height`，文字不变形；收起时隐藏第二、三行。
5. **托盘**：`TrayIcon.cs`（WinForms `NotifyIcon` + `ContextMenuStrip`），勾选状态从窗口真实状态回读，热键改穿透也会同步。
6. **自启**：`AutoStart.cs`，值名 `FloatIsle`，路径取 `Environment.ProcessPath`，关掉走 `DeleteValue`。
7. **媒体**：`MediaSession.cs`，三个 SMTC 事件 + 合并刷新 + `Dispatcher.BeginInvoke` 切回 UI；`MainWindow.UpdateMedia()` 负责文案（无会话时 `无媒体`）。
8. **退出**：菜单「退出」→ `Application.Shutdown()` → `OnClosed` 里先 `_tray.Dispose()`（先 `Visible=false` 再释放，避免幽灵图标）再 `_media.Dispose()`。

## 已知限制（有意的取舍）

- **系统阴影被裁掉了**。`SetWindowRgn` 按形状裁剪窗口，画在胶囊外的 `DropShadowEffect` 会被切出硬边，所以去掉阴影，只留 1px 描边。
- 区域边缘没有抗锯齿，圆角是硬边（1px 级锯齿）。
- **只认系统 SMTC**：不注册 SMTC 会话的播放器（部分小众播放器、网页里被浏览器接管的情况）读不到；浏览器类通常以浏览器进程为一个会话。
- 没有封面、没有进度条、没有任何播放控制（按 v0.2 范围只读）。
- 输出目录 25 MB，主要来自 24.8 MB 的投影 DLL；要减小体积得裁剪 CsWinRT 投影，v0.2 不做。
- 托盘图标用框架自带的 `SystemIcons.Application`（通用窗口图标），没有自带图标资源。
- 没有设置界面、没有多显示器跟随、没有运行中 DPI 变化处理。
- `Get-Process FloatIsle` 的 `MainWindowHandle` 是 0（窗口带 `WS_EX_TOOLWINDOW`），脚本要拿句柄得自己 `EnumWindows` + `GetWindowThreadProcessId` 按 PID 找。

## 自己验证（不要只信上面的描述）

```powershell
# 扩展样式：期望 0x08080088，开穿透后 0x080800A8
# 位置：期望 RECT 在主屏顶部居中，如 1090,6,1470,186（2560x1600 屏）
Get-Process FloatIsle | Select-Object Id, @{n='PrivateMB';e={[math]::Round($_.PrivateMemorySize64/1MB,2)}}
# 自启项
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FloatIsle
# 岛上实际显示的文字（UI Automation 读回，不是截图猜）
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')
$pid_ = (Get-Process FloatIsle | Select-Object -First 1).Id
$root = [System.Windows.Automation.AutomationElement]::RootElement
$w = $root.FindFirst('Children', (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pid_)))
$w.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) |
    ForEach-Object { $_.Current.ControlType.ProgrammaticName + '=' + $_.Current.Name }
```

形状命中的实测坐标（窗口 RECT=1090,6,1470,186；收起态胶囊占 x1200-1360 / y74-118，展开态占 x1120-1440 / y36-156）：

| 点 | 收起态 | 展开态 |
| --- | --- | --- |
| 胶囊中心 (1280,96) | 岛 | 岛 |
| 舞台内胶囊下方 (1280,126) | 下面窗口 | 岛 |
| 展开后才覆盖 (1130,45) | 下面窗口 | 岛 |
| 舞台左上角 (1100,20) | 下面窗口 | 下面窗口 |

`GetWindowRgn` 读回：收起态 `box=110,68,269,111`（159×43），展开态 `box=30,30,349,149`（319×119）。
