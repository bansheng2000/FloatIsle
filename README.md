# FloatIsle

Windows 桌面「灵动岛」外壳（v0.2）：透明、无边框、置顶、**不抢焦点**的胶囊窗口。鼠标悬停展开，**只有胶囊形状本身可点，形状外一律穿透**；展开后显示当前 SMTC 媒体的标题/艺人/播放状态；**全屏应用或锁屏时自动让位**；托盘菜单管显示、穿透、自启、设置、退出；配置持久化在 `%APPDATA%\FloatIsle\settings.json`。

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
| 右键托盘图标 | 隐藏岛 / 点击穿透 / 开机自启 / 设置… / 退出 |
| 托盘菜单「设置…」 | 打开普通设置窗口（有边框、能拖、正常获得焦点）：透明度、默认穿透、媒体开关、全屏让位开关、悬停延迟、位置复位、岛内容顺序 |

展开态三行：按「岛内容」顺序排（默认 今日用量 → 余额/额度 → 媒体 → 任务），第三行永远是穿透状态。收起态主行由设置里的「收起时主行显示」决定（媒体标题 / 今日用量 / 任务数）。

收起态胶囊只有 160 DIP（边框内 158），**放不下完整用量行**：完整行实测 184 DIP，100%/150% 都会在「/ 1…」处被省略号切断（150% 下用户截图就是 `今日 2.7 亿 token / 1…`）。所以收起态只显示 token 总数（`2.8 亿 tok`，实测 66 DIP），调用次数、5 小时/周/月百分比都在展开态显示。展开态沿用原来的 280 DIP 文字上限，完整行 184 DIP 落在里面（余量 96 DIP）。

## 用量与额度（只读账本）

- 数据源：`%USERPROFILE%\.dsh\storages\cost-meter\ledger.json`（成本插件写的账本）。FloatIsle **只读**，从不写它，也不写同目录任何文件。
- 只挑三个位置：`days[本地今天]`（token 与调用次数）、`balanceRef`（余额与币种）、`planSamples.go`（5 小时 / 周 / 月已用百分比，取最后一条）。用 `Utf8JsonReader` 流式跳过其余键，不把 2 MB 的账本建成对象树。
- **订阅计划下 `cost`/`apiCost` 恒为 0**，所以默认显示 token 量与调用次数（`今日 2.6 亿 token / 1045 次`），不显示 `¥0.00`；只有当某天 `cost > 0` 时才追加金额并标币种。收起态行更短（`2.8 亿 tok`），理由见上一节。
- 刷新是**事件驱动**：`FileSystemWatcher` 盯账本目录（`Changed`/`Created`/`Renamed`），加 2 秒去抖（账本是批量写的，一次刷新会连写多次）。**没有任何定时器去读文件**，所以空闲 CPU 仍是 0。
- 账本缺失、被删、JSON 写坏、键缺失：一律显示 `今日用量：无数据` / `余额：无数据`，进程不崩、不弹窗、不写回。
- 设置面板「岛内容」：每个模块一行 = 显示开关 + 上移 + 下移（不做拖拽，这个尺寸里拖拽是给自己找 bug）。顺序和开关直接落 `settings.json` 的 `Modules` 数组。
- 「联动」栏是给任务面板占位用的：地址默认 `127.0.0.1:47821`，可改；状态目前恒为「未连接」，任务模块因此整体隐藏，不留空壳。

实测（`FLOATISLE_LEDGER` 指向副本，避免碰真账本）：

| 检查 | 读数 |
| --- | --- |
| 账本真值 → 界面 | `input=8435998 output=801903 cacheRead=243072384 cacheWrite=0 calls=1020` → `今日 2.5 亿 token / 1028 次`；`balanceRef.total=37.55767144 CNY` → `余额 ¥37.56`；`fiveHour/weekly/monthly.p` = 3 / 25 / 61 → `5时 3% · 周 25% · 月 61%` |
| 改副本 → 界面变化 | 副本 `calls+700`、`cacheRead+50M` 后 **2074 ms** 界面刷新（2 秒去抖 + 一次读取） |
| 收起态文字宽度（150%） | 可用 158 DIP：旧文案 `今日 2.8 亿 token / 1187 次` 实测 **184 DIP** → 被截成 `…/ 1…`；新文案 `2.8 亿 tok` 实测 **66 DIP**（占 42%，余量 92 DIP），无省略号 |
| 展开态文字宽度（150%） | `今日 2.8 亿 token / 1187 次` 实测 184 DIP，可用 280 DIP |
| 空闲 CPU（watcher 待机） | 30 秒 **31–62 ms**（2–4 个 15.6 ms 时间片）；账本频繁写入时更高。早先 100% 档测到过 0 ms/30 s，**不写成恒为 0** |
| 账本写坏（36 字节残缺 JSON） | 岛展开显示 `今日用量：无数据`、`余额：无数据`，进程存活 |
| 账本被删 | 同上，进程存活，文件未被重建 |
| 面板换序 + 关一个模块 | `上移` 把「媒体信息」从第 3 位挪到第 2 位、取消「余额/额度」→ 落盘 `usage, media, quota, tasks` 且 `quota.Visible=false` |
| 杀进程重启 | 顺序与开关仍在；收起态主行跟着可见模块走（媒体关掉后回落到今日用量） |


## 媒体信息（只读）

- 数据源：`GlobalSystemMediaTransportControlsSessionManager`（SMTC），只读标题、艺人、播放状态。
- 感知方式：**事件**，不是轮询。订阅 `CurrentSessionChanged`（换播放器）、`MediaPropertiesChanged`（换歌/改元数据）、`PlaybackInfoChanged`（播放/暂停），三者都切回 UI 线程处理（`Dispatcher.BeginInvoke`）。事件连着来时会合并成一次读取。
- 没有会话、读取过程中会话被关、拿不到 SMTC：一律显示 `无媒体`，不弹窗、不抛异常。
- **可关**：设置面板取消「显示媒体信息（SMTC 只读）」后不再创建会话管理器，岛上第二行显示 `媒体信息已关闭`。
- 实测空闲 CPU（10 秒采样，`TotalProcessorTime` 差值）：播放中 0 ms、暂停 0 ms、无会话 0 ms。

## 打包（Inno Setup，不签名）

发布形态是 **self-contained**（用户零前置，不需要装 .NET 运行时）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

实测体积对照：

| 形态 | 字节 | 文件数 | 前置 |
| --- | --- | --- | --- |
| self-contained | 204,905,215（195.41 MB） | 475 | 无 |
| framework-dependent | 25,621,558（24.43 MB） | 7 | 需 .NET 10 桌面运行时 |

`PublishTrimmed=true` **不可用**：WPF/WinForms 直接报 `NETSDK1175`（SDK 明确不支持裁剪这些框架），与 WinRT 投影无关。

编译安装包（Inno Setup 6.7.3）：

```powershell
& "C:\Users\12576\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "installer\FloatIsle.iss"
# 产物 installer\Output\FloatIsle-Setup.exe（本次 57,154,023 字节 / 54.51 MB，md5 9D4EA4D3BE7A3B47330EB5CD79E0D352；lzma2/max）
```

**版本号只有一个来源**：`FloatIsle.csproj` 里的 `<Version>0.2.0</Version>`。SDK 把它写进 exe 的版本资源，`installer/FloatIsle.iss` 用 `#define AppVersion GetStringFileInfo(...)` 从 publish 出来的 exe 里读，安装脚本里不再手写第二份（以前写死 `1.0.0`，跟 README 自称的 v0.2 对不上，装完「应用和功能」显示 `FloatIsle 1.0.0`）。

**改版本号只改 `csproj` 的 `<Version>`**，然后重新 publish + 编译安装包即可；「应用和功能」里显示的就是 `FloatIsle <Version>`（实测 `FloatIsle 0.2.0`）。`IncludeSourceRevisionInInformationalVersion=false` 是为了不让 git 提交号混进 `ProductVersion`。

`publish/` 实测 475 文件 / 204,905,215 字节；装完 `%LOCALAPPDATA%\Programs\FloatIsle` 是 477 文件 / 209,750,197 字节（多 `unins000.exe` + `unins000.dat`）。安装包的字节数每次 publish + 编译会因文件时间戳略有浮动（本次 57,154,023，上一次 57,139,450）。

安装包行为：装到 `%LOCALAPPDATA%\Programs\FloatIsle`（per-user，不需要管理员、不写 HKLM）、开始菜单快捷方式、自带卸载器 `unins000.exe`、可选「开机自启」（只写 HKCU 的 Run 值）。卸载会删 Run 值、删安装目录、删快捷方式。

**没有代码签名**：首次运行 Windows 会拦，需要点「更多信息」→「仍要运行」（安装界面第一页也写了这句）。

> 本机环境的一个坑（**不是安装包的问题**）：DSH 的工作区目录 `E:\文档\deepseek-harness\FloatIsle` 上挂了**低完整性标签**（`icacls` 能看到 `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`，以及 `Everyone:(CI)(DENY)(DC)`、一个 `S-1-4-...` 沙箱 SID 的 `(OI)(CI)(W,D,DC)`）。同一份 exe（md5 相同、无 ADS）拷进工作区就带这个标签、拷到工作区外就没有；`Chief`、`dsh-plot-clone` 这些工作区目录上同样有，还没跑过会话的目录则干净。**从该目录里启动的 exe 会拿到受限上下文**：往 `%TEMP%` 写文件被拒、Inno Setup 建不了自己的临时目录后静默退出、`SHQueryUserNotificationState` 恒返回 2。低完整性 + no-write-up 能解释前两件事；**第三个只确认了相关性，机制未验明**。同一份 exe 拷到工作区外（`E:\文档\big-cjk\`、`C:\...\Temp`）一切正常：安装 2 秒完成、状态读数正常。装完之后从 `%LOCALAPPDATA%\Programs\FloatIsle` 启动（在工作区外），不受影响。**要测装包/跑程序，先把 exe 拷到工作区外。**

## 全屏让位

玩游戏或看全屏视频时岛自动淡出隐藏，退出全屏后淡入恢复。

- **判定**：`SHQueryUserNotificationState` 返回 `QUNS_NOT_PRESENT (1)`（锁屏/屏保）直接让位；返回 `QUNS_BUSY (2)`（全屏应用，实测无边框全屏窗口就是 2）或 `QUNS_RUNNING_D3D_FULL_SCREEN (3)`（独占全屏游戏）时，**再复核前台窗口是否真盖满它所在显示器**，两条都成立才让位。
- **触发**：主路是 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`，前台窗口一变就查一次（实测从全屏窗口出现到岛隐藏 12 ms）；兜底是 1 秒一次的 `DispatcherTimer`，因为微软文档明说"用户启动/结束全屏应用时不会发出通知"，同一窗口里切全屏（比如浏览器 F11）不会有前台变化事件。
- **过渡**：淡出 140 ms 后 `Hide()`，恢复时 `Show()` + 淡入 160 ms（实测恢复 130 ms，远小于 1 秒）。
- **恢复条件**：只有"因为全屏才隐藏"的才自动恢复；用户手动隐藏的不会被弹出来。
- **可关**：设置面板取消「全屏 / 锁屏时自动让位」后，`FullscreenWatcher.Stop()` 会卸钩子、停定时器，岛不再自动隐藏。
- **钩子清理**：`FullscreenWatcher.Dispose()` 里 `UnhookWinEvent`，窗口关闭时调用；实测进出全屏 3 轮后句柄数 1180 → 1176，无增长。

## 开机自启

菜单里的「开机自启」只做一件事：在
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下写一个名为 `FloatIsle` 的 `REG_SZ`，内容是**当前 exe 的绝对路径并加引号**。

- 关掉时是**删除这个值**（`DeleteValue`），不是写空字符串。
- 程序退出**不会**删这个值。
- 手工核对：`reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FloatIsle`

**换目录/重装后自启会失效**（值里还是旧路径）。两种修法：

1. 在新位置启动一次，托盘菜单里把「开机自启」关掉再打开，值会被当前路径覆盖；
2. 或 `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FloatIsle /f`，再在新位置打开一次。

## 设置与配置持久化（`%APPDATA%\FloatIsle\settings.json`）

托盘菜单「设置…」打开的是**普通 WPF 窗口**：有边框、能拖动、正常获得焦点（没有 `WS_EX_NOACTIVATE`、不是穿透窗口、不置顶）。改一项**立即生效并落盘**，不需要「保存」按钮。

```json
{
  "LeftPx": 1090,
  "TopPx": 6,
  "Left": null,
  "Top": null,
  "Screen": "\\\\.\\DISPLAY1",
  "Opacity": 1,
  "ClickThrough": false,
  "MediaEnabled": true,
  "FullscreenSuppress": true,
  "HoverDelayMs": 0,
  "Modules": [
    { "Id": "usage", "Visible": true },
    { "Id": "quota", "Visible": true },
    { "Id": "media", "Visible": true },
    { "Id": "tasks", "Visible": true }
  ],
  "CollapsedLine": "media",
  "Mode": "capsule",
  "TasksEndpoint": "127.0.0.1:47821",
  "TasksJumpEndpoint": "127.0.0.1:47822"
}
```

| 字段 | 含义 |
| --- | --- |
| `LeftPx` / `TopPx` | 窗口左上角，**设备像素**（不是 DIP：两块屏缩放不同时，DIP 坐标没法跨屏比较） |
| `Left` / `Top` | v0.2-D 的旧字段（DIP）。读到老文件时按主屏缩放折成像素迁移一次，随后写回 `null` |
| `Screen` | 上次所在显示器设备名。还原时先按它找同名屏，找不到才回落主屏顶部居中 |
| `Opacity` | 岛的不透明度，0.2–1.0 |
| `ClickThrough` | 整窗穿透的当前状态（热键/托盘切换也会写回，所以"默认穿透"= 最后一次的状态） |
| `MediaEnabled` | 关掉后不再读 SMTC，岛上第二行显示 `媒体信息已关闭` |
| `FullscreenSuppress` | 关掉后全屏/锁屏也不再自动隐藏 |
| `HoverDelayMs` | 鼠标停在岛上的展开延迟（0 = 立即，上限 2000）。收起不受延迟影响 |
| `Modules` | 岛内容的顺序与显示开关，元素 `{Id, Visible}`；已知 id 只有 `usage`/`quota`/`media`/`tasks`，未知 id 会被丢掉、缺的按默认顺序补回来 |
| `CollapsedLine` | 收起态主行显示哪一项：`media`（默认）/ `usage` / `tasks` |
| `TasksEndpoint` | 任务面板心跳地址，默认 `127.0.0.1:47821`（空或超过 64 字回默认） |
| `TasksJumpEndpoint` | 点任务行时把 `sid` POST 过去的跳转地址，默认 `127.0.0.1:47822`（同上限） |
| `Mode` | 形态：`capsule`（默认，顶部胶囊 + 悬停展开）/ `top`（贴顶工作台，光标碰屏顶才展开）。改这一项立即生效，不用重启 |

- **开机自启不在这里**：真源是注册表，设置面板只读回显，避免两份真相。
- 位置可以直接拖：在胶囊上按住左键拖，松手写回 `LeftPx/TopPx`（细节见下一节）。面板里还有「复位到顶部居中」。
- 坏文件不崩、不删：解析失败时把原文件改名成 `settings.json.bak`（覆盖上一次的 .bak），用默认值启动，设置面板顶部红字给出 .bak 路径。
- 写入走 `settings.json.tmp` + 替换，避免中途被杀留下半个 JSON。

实测（用的是**装好的 exe**，从 `%LOCALAPPDATA%\Programs\FloatIsle` 启动，配置在 `%APPDATA%`；不要从仓库 `bin\Release\...` 里跑，那条路径带低完整性标签会写不进去）：

| 检查 | 读数 |
| --- | --- |
| 首次启动生成默认配置 | `Left=1090, Top=6, Screen="\\\\.\\DISPLAY1", Opacity=1, ClickThrough=false, MediaEnabled=true, FullscreenSuppress=true, HoverDelayMs=0` |
| 设置窗口属性 | `EXSTYLE=0x00040100`（无 NOACTIVATE / 无 TRANSPARENT）、`STYLE` 有 CAPTION+SYSMENU、`GetForegroundWindow()==设置窗口` |
| 拖动窗口标题栏 | `rect` 1080,567 → 1140,637 |
| 面板勾选穿透 | 岛 `EXSTYLE` 0x08080088 → **0x080800A8** |
| 面板改透明度 0.6 | 胶囊体像素均值 **60.59 → 138.39**（桌面透上来了） |
| 面板改悬停延迟 0 → 300 ms | 展开耗时 **61 ms → 341 ms** |
| 杀进程 → 重启 | `EXSTYLE` 仍 0x080800A8；面板读回 `ClickThrough=On, Opacity=0.6, Hover=300, Media=Off` |
| 手写坏 JSON → 启动 | 进程活着、**只有 1 个窗口**（无错误对话框）、`.bak` 里是原文、配置回默认值、面板红字显示 .bak 路径 |
| 位置 | JSON 写 `240,420` → `RECT=240,420,620,600`；点「复位」→ `1090,6` 并写回 JSON |
| 全屏让位开关 | `false`：全屏窗口盖上来岛仍可见；`true`：全屏后 **20 ms** 隐藏，退出后 **21 ms** 恢复 |
| 空闲 CPU（稳定后） | **0 ms / 10 s**、**0 ms / 30 s**（启动后头 10 s 有一次性初始化，实测 31.2 ms，之后归零） |
| 内存 | uptime 108 s：WSP **23.6 MB**、PrivateBytes 49.79 MB、17 线程、1175 句柄 |

## 拖动与贴顶工作台

**拖动胶囊**：在胶囊上按住左键拖，窗口跟手（`SetWindowPos` 循环；`WS_EX_NOACTIVATE` 的窗口不能 `DragMove`）。按下瞬间先切回收起态并停掉悬停定时器，所以拖动期间不会触发展开/收起动画；松手用 `Placement.ClampInto` 夹进当前显示器工作区，再写 `LeftPx/TopPx`。穿透开启时鼠标事件到不了窗口，先按 `Ctrl+Alt+I` 关掉。

实测（100% 档，工作区 2560×1552）：

| 检查 | 读数 |
| --- | --- |
| 拖前 | `RECT=1090,6,1470,186`，`REGION 159x43`（收起） |
| 拖动中 | `RECT` 依次 1190,81 → 1290,156，**区域始终 `159x43`**（没有被悬停展开），`EXSTYLE=0x08080088`，`GetForegroundWindow()` 拖动前后同一句柄 |
| 拖后 | `RECT=1290,156,1670,336`；`settings.json` 写回 `LeftPx=1290 TopPx=156` |
| 重启 | `RECT=1290,156,1670,336`（原位） |
| 拖出屏幕 | 拖到 (2555,1595) → 松手夹回 `2180,1372,2560,1552`，正好贴右/下边，无半截在屏外 |

**贴顶工作台**（`Mode=top`）：空闲时**没有任何可见常驻窗口**。`WH_MOUSE_LL` 光标钩子常驻，光标进入屏幕顶 4 DIP（按该屏缩放折算成物理像素）才 `PostMessage` 叫醒 UI 线程，面板从顶边 180ms 滑下，宽 560 DIP、圆角 16，按唤醒时光标 X 居中并夹进工作区（左右各留 24 DIP）；鼠标离开面板后滑回并隐藏。

钩子装在一个**独立后台线程**上（低级钩子的回调只在该线程被调用，且它必须自己泵消息：`WaitMessage` + `PeekMessage`）。回调里只有「判 `nCode`/`y` → `PostMessage`」，不建控件、不读文件、不写日志。另有两个实用点：`SetCursorPos` **不会**产生输入事件，低层钩子收不到，测唤醒必须用 `mouse_event`/`SendInput` 注入；面板开着时钩子置 `Open`，避免鼠标停在顶边时消息洪水。

实测（100% 档，100% 缩放、单屏）：

| 检查 | 读数 |
| --- | --- |
| 面板切到贴顶（不重启） | `settings.json` `Mode=top`；岛窗口立刻 `IsWindowVisible=False`，无可见常驻窗 |
| 唤醒延迟 | 22 / 41 / 60 ms（轮询粒度 4 ms + 180 ms 下滑动画不计入可见时刻） |
| 唤醒位置（夹紧） | X=200 → `RECT_L=24`（工作区左+24）；X=1280 → `1000`（居中）；X=2500 → `1976`（右-560-24） |
| 面板尺寸与区域 | `RECT=1000,0,1560,184`，`REGION type=3 box=0,0,559,184 → 559x184`（三卡） |
| 面板内容（UIA 读回） | `用量 | 今日 3.8 亿 token / 1752 次 | 余额 ¥37.56 · 5时 7% · 周 29% · 月 63% | 任务 | 1 个在跑 | 未命名工作区 · 在跑 | 媒体 | 无媒体` |
| 鼠标离开面板 | 滑回耗时 **197–216 ms**（动画 180 ms + 轮询），随后 `IsWindowVisible=False` |
| 钩子空闲 30 s | **31.2 ms / 2 个时间片**（15.625 ms 一片） |
| 5 秒内 313 次注入移动 | **0 ms / 0 个时间片** |
| 内存（uptime 104 s） | Private **82.64 MB**、WS 153.01 MB、25 线程、1275 句柄 |

**未实测**：150% 档的贴顶面板（本机无法程序化改缩放，要用户手动切）；真双屏下的跨屏唤醒与夹紧（只有一块屏，位置逻辑走的是 `Placement` 纯函数单测）。

## 任务面板（心跳接收端，`127.0.0.1:47821` / 跳转 `47822`）

通知插件每 10 秒推一次心跳，本程序只做**接收端**（不改插件代码）。用 `TcpListener` 手写最小 HTTP/1.1 而不是 `HttpListener`：后者在 Windows 上要预先注册 URL ACL（`netsh http add urlacl`），普通用户跑不起来。

```
POST http://127.0.0.1:47821/event      # 只接受这一个路径、只接受 POST
Content-Type: application/json
{"type":"turn/start","sid":"<sessionId>","ws":"<工作区目录名>","turn":12,"ts":"..."}
{"type":"turn/end","sid":"...","ws":"...","turn":12,"reason":"completed","ts":"..."}
{"type":"question","sid":"...","ws":"...","brief":"<第一行问题>"}
{"type":"approval","sid":"...","ws":"...","tool":"bash"}
{"type":"heartbeat","sid":null,"ws":null,"running":["sid1","sid2"],"ts":"..."}
```

| 行为 | 规则 |
| --- | --- |
| 心跳权威 | `running` 里的 sid 才是「在跑」；不在里面且不是「刚结束」的行**立刻移除**（会话重启/崩溃不会留僵尸任务） |
| `turn/end` | 状态「刚结束」保留 **5 秒**再消失（实测 5 秒整） |
| 心跳超时 | **25 秒**没有心跳/事件 → 任务区整体隐藏、状态行显示 `DSH 未运行`（实测面板消失 26 秒，含 0.5 秒轮询粒度） |
| 展开态 | 最多 4 行 `工作区名 · 状态`，多的收成「还有 N 个」；状态为 在跑 / 等你回答 / 等你批准 / 刚结束 |
| 收起态 | 主行选 `tasks` 时显示 `N 个在跑`（`3 个在跑` 实测 55 px、`12 个在跑` 62 px，可用 158 DIP） |
| 点一行 | `POST http://127.0.0.1:47822/jump`，body `{"sid":"..."}`，1 秒超时、失败静默（插件没起也不弹窗） |
| 异常输入 | 坏 JSON 400、超 64 KB 413、`GET /event` 405、其它路径 404 |
| 端口被占 | 不崩不弹框：岛上状态行 `任务面板不可用`，设置「联动」栏写 `47823 被占用，任务面板不可用` |
| 任务区空 | 连上但没有任何会话时整块隐藏（不显示「任务 0 个在跑」） |

实测（100% 档，`E:\temp-fi-g` 副本；发送端是我自己的 `curl`/临时接收器，**真通知插件未联调**）：

| 检查 | 读数 |
| --- | --- |
| `POST turn/start` | 200；展开态 `任务 1 个在跑` + `demo · 在跑` |
| `question`（UTF-8 body） | 200；`demo · 等你回答` |
| `heartbeat running=[]` | 200；行消失，任务区整块隐藏 |
| 停发心跳 | **26 秒**后任务区消失，状态行 `DSH 未运行 · 点击穿透：关 · Ctrl+Alt+I 开启`（202 px < 280） |
| 点任务行 | 47822 临时接收器收到 `POST /jump` + `{"sid":"jump2"}`，回 200 |
| 端口占用（47823 起占位监听） | 岛上 `任务面板不可用 · 点击穿透：关 · Ctrl+Alt+I 开启`；设置栏 `状态：47823 被占用，任务面板不可用` |
| 空闲 CPU（无任务） | **0 个时间片 / 30 s** |
| 空闲 CPU（3 个任务 + 3 次心跳） | 5 个时间片 / 30 s（78.1 ms，含心跳触发的状态刷新） |

注意：`curl -d '{"brief":"中文"}'` 在 Git Bash / cmd 里会把中文按 GBK 送出去，本程序按 UTF-8 解析就回 400——这不是接口 bug，真实发送端（.NET/Node）都是 UTF-8。要手测中文请把 body 写进 UTF-8 文件再 `curl --data-binary @file`。

## 多屏与高 DPI（Per-Monitor V2）

- `app.manifest` 声明 `<dpiAwareness>PerMonitorV2</dpiAwareness>`（并留 `<dpiAware>true/pm</dpiAware>` 给不认新键的系统）。不是代码里 `SetProcessDpiAwareness`，也不是老的 system 级 `dpiAware`。
- **像素量只有一个来源**：`MainWindow.CurrentScale` = `GetDpiForWindow(hwnd) / 96`。需要交给 Win32 的数字（`SetWindowRgn` 的圆角矩形、位置、舞台像素宽高）全部从它派生；字号/边距保持 DIP，由 WPF 自己乘缩放——在 WPF 里再乘一次会变成双倍缩放。
- 缩放变化（系统改缩放，或岛被挪到缩放不同的屏）会收到 `WM_DPICHANGED`：用 lParam 里的建议矩形 `SetWindowPos`（带 `SWP_NOACTIVATE`），然后在 `DispatcherPriority.Loaded` 上重算区域并落盘位置——**不重启**。动画目标用的是 DIP 常量，天然跟随新缩放。
- 位置存「设备名 + 设备像素」。`Placement.Resolve()`：先按 `Screen` 找同名显示器，把坐标 `Math.Clamp` 进**那块屏**的工作区；找不到同名屏（被拔掉/换了机器）才回落主屏顶部居中。逻辑是纯函数，可直接自检：

```powershell
# 不开窗口，跑完退出码 0/1，并把逐条结果写进报告文件
E:\temp-fi2\e\FloatIsle.exe --selftest "$env:TEMP\placement-report.txt"
```

| 自检项（构造出来的显示器布局） | 结果 |
| --- | --- |
| 单屏默认 / 单屏存过位置 | `(1090,6,380x180)` / `(240,420,380x180)` |
| 双屏左右：落在第二块屏 / 坐标越界夹回 | `(3000,100)` / `99999 → (4100,100)` |
| 双屏上下：落在下方屏 | `(100,2000)` |
| 负坐标显示器（第二块在左） | `(-1900,10)` 保持为负，不折回 0 |
| 保存的显示器已拔掉 | 回落主屏顶部居中 `(1090,6)` |
| 窗口比工作区还大 | 贴该屏左上角 `(2560,0)` |
| 150% 缩放派生 | 默认 `(995,9,570x270)`；存过的像素坐标不换算 `(240,420,570x270)` |

10 / 10 全过（`ALL PASS (10)`）。**但这台机器只有一块屏，上表是自检不是真机多屏——「真双屏未实测」。**

### 高 DPI 实测（100% 档）

| 检查 | 读数 |
| --- | --- |
| DPI 上下文 | `AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` = **True**，与 PMv1 比较 = False；`GetProcessDpiAwareness` = 2；`GetDpiForWindow` = 96 |
| 窗口 `RECT` | `1090,6,1470,186`（380×180 舞台，2560×1600 屏） |
| 区域 `GetWindowRgn` | `type=3 box=110,68,269,111` → 159×43 |
| `EXSTYLE` | `0x08080088`（`WS_EX_NOACTIVATE` 仍在，没抢焦点） |
| 形状命中 | 胶囊中心 (1280,96) → 岛；舞台内胶囊下方 (1280,146) → 别的窗口；舞台左上角 (1100,16) → 桌面；胶囊左内缘 (1210,81) → 岛 |
| 空闲 10 s CPU | **0 ms** |
| 前台窗口 | 启动前后同一句柄（2429132），未抢焦点 |

**150% 档未实测**：这台机器上「程序化改缩放」三条路都失败——`DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE` 返回 0 但只写注册表（`GetDpiForMonitor` 仍 96、逻辑分辨率不变）；`SetDisplayConfig(..., SDC_APPLY)` 返回 `87 ERROR_INVALID_PARAMETER`；`SPI_SETLOGICALDPIOVERRIDE` 返回成功但读回不变。要补这一档需要**手动切一次系统缩放**（设置 → 显示 → 缩放 → 150%）。

**合成 `WM_DPICHANGED` 只算半个证据**：往窗口发一条 `WM_DPICHANGED`（wParam=144，lParam=建议矩形 `995,9,570×270`）后，窗口确实按建议矩形落位、区域读回 `box=308,170,547,235 → 239×65`（正是 159×43 的 1.5 倍），`LeftPx/TopPx` 也写回 `995,9`——证明处理器会执行、不重启就重算。但这条消息没有真的改显示器 DPI（`GetDpiForWindow` 仍 96），窗口在像素上被拉大而 WPF 的布局 DIP 没跟着变，岛在舞台里偏到右下角；**这是该测法的伪影，不代表真实 150% 布局**。真 150% 下窗口像素尺寸与布局 DIP 由系统同时给出，不会出现这种错位。

## 各块怎么实现的

1. **透明异形窗口**：`MainWindow.xaml:6-12`（`WindowStyle=None` + `AllowsTransparency` + 透明背景）；岛形是 `Border` 的 `CornerRadius=22`。
2. **置顶不抢焦点**：`MainWindow.xaml.cs` 在 `OnSourceInitialized` 里加 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，所有 `SetWindowPos` 都带 `SWP_NOACTIVATE`。
3. **异形命中**：`UpdateWindowRegion()` 用 `CreateRoundRectRgn` + `SetWindowRgn`，动画期间 `CompositionTarget.Rendering` 逐帧重设，末帧由 `DoubleAnimation.Completed` 补一次。
4. **悬停展开**：`Animate()`，动 `Border` 的 `Width/Height`，文字不变形；收起时隐藏第二、三行。
5. **托盘**：`TrayIcon.cs`（WinForms `NotifyIcon` + `ContextMenuStrip`），勾选状态从窗口真实状态回读，热键改穿透也会同步。
6. **自启**：`AutoStart.cs`，值名 `FloatIsle`，路径取 `Environment.ProcessPath`，关掉走 `DeleteValue`。
7. **媒体**：`MediaSession.cs`，三个 SMTC 事件 + 合并刷新 + `Dispatcher.BeginInvoke` 切回 UI；`MainWindow.UpdateMedia()` 负责文案（无会话时 `无媒体`）。
8. **退出**：菜单「退出」→ `Application.Shutdown()` → `OnClosed` 里先 `_tray.Dispose()`（先 `Visible=false` 再释放，避免幽灵图标）再 `_media.Dispose()`。
9. **设置**：`Settings.cs`（`AppSettings` + `SettingsStore.Load/Save`）、`SettingsWindow.xaml(.cs)`（普通窗口）、`MainWindow.UpdateSettings()`（唯一入口：改内存 → `Normalize()` → `ApplySettings()` → 落盘）。`ApplySettings()` 可重复调用，把透明度/穿透/媒体/全屏四项一起套上去。
10. **多屏与高 DPI**：`app.manifest`（PerMonitorV2）+ `MainWindow.CurrentScale`（= `GetDpiForWindow / 96`）驱动所有像素换算；`WndProc` 处理 `WM_DPICHANGED`（lParam 建议矩形 + 延迟到 `Loaded` 重算区域）；`Placement.cs` 纯函数定位，`PlacementSelfTest.cs` 10 条用例，入口在 `App.OnStartup` 的 `--selftest`。
11. **用量/额度**：`UsageLedger.cs`（`Utf8JsonReader` 流式挑 `days`/`balanceRef`/`planSamples.go`，失败返回 null）、`UsageWatcher.cs`（`FileSystemWatcher` + 2 秒去抖，事件切回 UI 线程）、`MainWindow.RefreshIslandText()`（按 `Modules` 顺序拼三行文案）、`SettingsWindow` 的「岛内容」行（开关 + 上移/下移）。
12. **任务面板**：`TasksHub.cs`（`TcpListener` 只绑回环 + 手写最小 HTTP/1.1 + 会话表 + 两个一次性 `DispatcherTimer`：25 秒心跳超时、5 秒「刚结束」淡出），`MainWindow.RefreshTaskRows()`（最多 4 行 + 签名缓存，内容没变不重建控件）、`TaskRow_Click` → `TasksHub.Jump`。
13. **拖动**：`MainWindow.Island_MouseLeftButtonDown/MouseMove/MouseLeftButtonUp` → `MoveTo`（`SetWindowPos`，带 `SWP_NOACTIVATE`）→ `EndDrag` 里 `Placement.ClampInto` + `SavePlacement`；`_dragging` 优先级高于悬停定时器，`Island.IsMouseOver` 才重新起悬停。
14. **贴顶工作台**：`TopEdgeHook.cs`（独立后台线程装 `WH_MOUSE_LL`，`WaitMessage`+`PeekMessage` 泵消息，回调只 `PostMessage`）、`MainWindow.ApplyMode/OnTopWake/ShowTopPanel/HideTopPanel/RefreshTopTiles`，面板卡片是 `WrapPanel` 两列 + `Tile()` 现拼；`UpdateWindowRegion()` 按当前形状（岛或面板）取圆角重设区域。

## 已知限制（有意的取舍）

- **系统阴影被裁掉了**。`SetWindowRgn` 按形状裁剪窗口，画在胶囊外的 `DropShadowEffect` 会被切出硬边，所以去掉阴影，只留 1px 描边。
- 区域边缘没有抗锯齿，圆角是硬边（1px 级锯齿）。
- **只认系统 SMTC**：不注册 SMTC 会话的播放器（部分小众播放器、网页里被浏览器接管的情况）读不到；浏览器类通常以浏览器进程为一个会话。
- **全屏判定踩到一个反常现象**：本机实测，同一份 exe 放在仓库 `bin\Release\...` 目录里，`SHQueryUserNotificationState` 会稳定返回 `QUNS_BUSY(2)`；把同样的文件拷到 `C:\...\Temp` 或 `E:\temp-fi` 下运行则返回正常的 `5`。现象与工作区目录上的**低完整性标签**相关（见上面「打包」一节；机制未验明，只确认相关性），不是程序逻辑问题。因此 2/3 两条路径不单看返回值，还要复核前台窗口是否盖满显示器——这样即使返回值异常也不会误隐藏。**独占全屏 D3D 游戏（state 3）本机没有可测环境，未实测。**
- 没有封面、没有进度条、没有任何播放控制（按 v0.2 范围只读）。
- **用量面板的数字来自成本插件的私有账本格式**：`days`/`balanceRef`/`planSamples.go` 任一改结构，这里就显示「无数据」（不会崩）。订阅计划下 `cost` 恒为 0，所以默认不显示金额。
- **收起态只显示 token 总数**（`2.8 亿 tok`），调用次数与额度百分比只在展开态：完整行 184 DIP 放不进收起态 158 DIP 的可用宽度，硬塞就是省略号。要看全就悬停展开。
- **媒体标题仍可能被省略号截断**：标题长度不可预知，超出收起态 158 DIP / 展开态 280 DIP 就 `CharacterEllipsis`。这是标题本身长，不是排版问题（用量/额度文案已改成放得下的形式）。
- **任务面板只验过自造事件，没和真通知插件联调**：`turn/start`/`question`/`heartbeat`/`jump` 都是我用 `curl` 和临时接收器推的；插件那边的报文是否逐字段一致、10 秒心跳在真实负载下是否稳定，**未实测**。
- **任务面板的跳转依赖插件监听 47822**：插件没起时点任务行静默失败（1 秒超时），岛上不会有任何提示——这是按「跳转只是锦上添花」定的。
- **任务行文字同样可能被省略号截断**：工作区目录名很长时按 280 DIP 宽度 `CharacterEllipsis`；状态部分是固定短词，不会截。
- **任务面板在 150% 下没有真机读数**：本轮系统缩放已切回 100%，任务行与 `N 个在跑` 的宽度只在 100% 下扫过墨迹（DIP 与缩放无关，预期同值，但这是推理不是读数）。
- 账本刷新是 2 秒去抖的事件驱动，**刻意不用定时器轮询**：轮询会把空闲 CPU 从 0 抬起来，这条是硬约束。
- 输出目录 25 MB，主要来自 24.8 MB 的投影 DLL；要减小体积得裁剪 CsWinRT 投影，v0.2 不做。
- 托盘图标用框架自带的 `SystemIcons.Application`（通用窗口图标），没有自带图标资源。
- **拖动只在胶囊模式**：贴顶模式没有常驻窗口可拖，面板位置跟唤醒时的光标 X 走；拖岛时穿过窗口的鼠标事件需要穿透关闭（`Ctrl+Alt+I`）。
- **贴顶模式的唤醒阈值是 4 DIP**，实测靠**注入输入事件**（`mouse_event`）验证，`SetCursorPos` 不产生输入、钩子收不到；真实手指/鼠标路径与注入事件走的是同一套 `WH_MOUSE_LL`，但没有物理鼠标的实测记录。
- **贴顶面板在 150% 下未实测**：本轮系统缩放是 100%，面板宽 560 DIP、圆角 16、卡片间距 12 都是 DIP，理论同值，但没有读数。
- **多屏只有自检，没有真机**：本机只有一块 2560×1600 屏，`Placement` 的 10 条用例是构造布局跑出来的纯函数自检；真实插拔第二块屏、跨屏缩放不同的场景**未实测**（设备名 `\\.\DISPLAY2` 这类在真机上是否稳定也给不出结论）。
- **150% 及以上缩放未实测**：程序化改缩放在本机不可行（三条 API 路径都失败，见上），所以 `WM_DPICHANGED` 的实际重排、150% 下的区域/字号/像素均值、设置面板 150% 是否裁字，**都没有真机读数**。代码按「一个缩放因子派生」写并做了自检，但这不等于实测通过。
- **DPI 变化时不主动改字号**：字号与边距是 DIP，交给 WPF 按新 DPI 自己缩放（WPF 里再乘一次会变成双倍）。这条依赖 WPF 行为，未实测验证。
- **设置面板高度封顶到工作区 90%**：内容超出时进滚动条，避免高缩放 + 小屏时窗口长出屏幕（排版在 150% 下未实测）。
- **开了整窗穿透后悬停不会展开**：`WS_EX_TRANSPARENT` 让鼠标事件根本到不了岛，探测不到 MouseEnter。要展开先按 `Ctrl+Alt+I` 关掉穿透。
- 设置窗口是**普通窗口、不置顶**：可能被别的窗口盖住，这是有意的（它要保持正常焦点/拖动语义）。
- 坏 JSON 只会留**一份** `settings.json.bak`（再次损坏会覆盖），不保留历史。改文件时字段名拼错不会报错，那一项直接回默认值。
- `Get-Process FloatIsle` 的 `MainWindowHandle` 是 0（窗口带 `WS_EX_TOOLWINDOW`），脚本要拿句柄得自己 `EnumWindows` + `GetWindowThreadProcessId` 按 PID 找。

## 自己验证（不要只信上面的描述）

```powershell
# 扩展样式：期望 0x08080088，开穿透后 0x080800A8
# 位置：期望 RECT 在主屏顶部居中，如 1090,6,1470,186（2560x1600 屏）
Get-Process FloatIsle | Select-Object Id, @{n='PrivateMB';e={[math]::Round($_.PrivateMemorySize64/1MB,2)}}
# 位置解析自检（10 条用例，不开窗口；从仓库 bin 直接跑可能写不进 %TEMP%，先拷到工作区外再跑）
& "E:\temp-fi2\e\FloatIsle.exe" --selftest "$env:TEMP\placement-report.txt"; Get-Content "$env:TEMP\placement-report.txt"
# DPI 感知：应为 PerMonitorV2 —— 取窗口的 DPI 上下文与 (HANDLE)-4 比较（AreDpiAwarenessContextsEqual），
# 或用 GetProcessDpiAwareness 得到 2（per-monitor）、GetDpiForWindow 得到 96（100% 档）
# 自启项
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FloatIsle
# 配置（目录不存在说明还没启动过）
Get-Content "$env:APPDATA\FloatIsle\settings.json" -Raw
# 坏配置回默认 + 备份：手写一段坏 JSON 后启动，应生成 settings.json.bak 且程序不崩
Test-Path "$env:APPDATA\FloatIsle\settings.json.bak"
# 岛上实际显示的文字（UI Automation 读回，不是截图猜）
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')
$pid_ = (Get-Process FloatIsle | Select-Object -First 1).Id
$root = [System.Windows.Automation.AutomationElement]::RootElement
$w = $root.FindFirst('Children', (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pid_)))
$w.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) |
    ForEach-Object { $_.Current.ControlType.ProgrammaticName + '=' + $_.Current.Name }
# 任务面板接收端：端口在听（期望 127.0.0.1:47821 LISTENING）
netstat -ano | Select-String '127.0.0.1:47821.*LISTENING'
# 推一条事件，期望回 200，展开岛后出现「demo · 在跑」
curl.exe -s -w "%{http_code}`n" -X POST http://127.0.0.1:47821/event -d '{\"type\":\"turn/start\",\"sid\":\"t1\",\"ws\":\"demo\",\"turn\":1}'
# 非法输入四种，期望 400（坏 JSON）/ 413（>64 KB）/ 405（GET /event）/ 404（POST /other）
curl.exe -s -o NUL -w "%{http_code}`n" -X POST http://127.0.0.1:47821/event -d 'not-json'
curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:47821/event
```

形状命中的实测坐标（窗口 RECT=1090,6,1470,186；收起态胶囊占 x1200-1360 / y74-118，展开态占 x1120-1440 / y36-156）：

| 点 | 收起态 | 展开态 |
| --- | --- | --- |
| 胶囊中心 (1280,96) | 岛 | 岛 |
| 舞台内胶囊下方 (1280,126) | 下面窗口 | 岛 |
| 展开后才覆盖 (1130,45) | 下面窗口 | 岛 |
| 舞台左上角 (1100,20) | 下面窗口 | 下面窗口 |

`GetWindowRgn` 读回：收起态 `box=110,68,269,111`（159×43），展开态 `box=30,30,349,149`（319×119）。
