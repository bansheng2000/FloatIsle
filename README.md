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
| 点在胶囊外（哪怕在 176×60 窗口内） | 直接落到下面那个窗口 |
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
- 设置面板「岛内容」：每个模块一行 = 显示开关 + 上移 + 下移；贴顶工作台里的卡片还能直接按住拖动换位（两种方式改同一份顺序，不写两套逻辑）。顺序与开关落 `settings.json`：`Modules` 是唯一真源，`ModuleOrder` 只在保存时从它回写、读回时作为兼容入口（缺失或全是非法 id 时回落默认顺序，不抛）。
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
  "TasksJumpEndpoint": "127.0.0.1:47822",
  "GlowColor": "#4DA3FF"
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
| `Mode` | 形态：`capsule`（默认，顶部胶囊 + 悬停向四周对称展开）/ `top`（贴顶工作台：胶囊常驻顶部、悬停向下长出工作台面板）。改这一项立即生效，不用重启 |
| `GlowColor` | **「在跑」状态的呼吸辉光颜色** `#RRGGBB`，默认 `#4DA3FF`；非法值回默认，设置面板会标出来。改这一项立即生效。琥珀（等你）与绿（完成）是代码常量，不在此项内 |

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

**贴顶工作台**（`Mode=top`）：胶囊**常驻**在屏幕顶部（默认居中，尺寸与胶囊模式完全一致 176×60 窗口 / 159×43 区域，显示同一行收起文案，辉光/呼吸/三态换色/完成闪烁照旧）。光标进到胶囊上 → **上沿不动、只向下**长出工作台面板（宽度按卡片内容自适应、上限是工作区宽的一半，圆角 16，一次 `SetWindowPos` 落位，不再四向对称）；面板顶部紧接胶囊下沿、不留缝；鼠标离开面板与胶囊 150 ms 后收回并逐像素复原。展开只有两条入口：悬停胶囊（`Island_MouseEnter` → `_hoverTimer` → `OpenTopWorkbench()`）、以及已展开时在面板内移动；`_topOpen` 闸门防重复起动画（`HoverDelayMs=0` 时也不会跑成胶囊展开）。**碰顶唤醒那条链已整条删除**：用户报「鼠标都没碰到胶囊就把工作台打开了」，根因是 `WH_MOUSE_LL` 钩子只看 `y ≤ 4 DIP`、完全不判 x。

两套排版分开渲染：胶囊模式用紧凑列表（主行/副行/任务行/提示行），贴顶工作台用卡片网格（今日用量 / 余额额度 / 任务 / 媒体四块，每块标题 + 主行 + 若干明细行，每行一条信息，不再用 ` · ` 串长串）。卡片高度一律 `Auto`（不设 `Height`/`MinHeight`，自检会断言 `Height` 是 `NaN`），面板高 = 各卡高 + 行间距 + 上下内边距；隐藏模块直接不建卡、不占位。列数不设档位：按可用宽度自动推导（够 3 列就 3 列、不够 2 列、再不够 1 列；1 列时卡片保持最小宽 180 DIP，不拉成整宽），4 张卡排成 3+1 时主动退成 2 列，避免最后一行空大半。卡片可**拖拽换序**：按住卡片拖动时同格让位（拖动中的卡片半透明、`ZIndex` 抬高），松手把新顺序写进 `ModuleOrder`；拖拽用 `TopTiles.CaptureMouse()` 独占鼠标，岛的展开/收起与拖动窗口都被 `_draggingTile` 闸门挡住。贴顶模式下胶囊**永远只显示收起行**（`IslandExpanded => _expanded && !IsTopMode`），否则展开态四行会挤进 44 DIP 的胶囊里溢出。

区域是**并集**：`UpdateWindowRegion()` 在贴顶模式把胶囊与面板两块圆角区域用 `CombineRgn(..., RGN_OR)` 合起来，中间帧不会出现「区域是展开的、窗口还是收起的」；胶囊左侧的空白仍然穿透。

实测（100% 档、100% 缩放、单屏 2560×1552、端口 47831 推 heartbeat＝2 个 running）：

| 检查 | 读数 |
| --- | --- |
| 贴顶启动（收起） | `RECT=1228,0,176x60`（= 胶囊模式收起态），`REGION type=3 box=8,8,167,51 → 159x43`；UIA 只有一行 `2.3 亿 tok · 余0.95`（1256,21 120x18） |
| 悬停展开 | `RECT=1036,0,560x263`；20 帧采样 `TOPSET=0`（上沿恒 0）、`BOTMIN=60 BOTMAX=263`（只有下边变） |
| 展开区域 | `REGION type=3 box=0,8,559,262 → 559x254`（并集包围盒） |
| 展开内容（UIA） | 胶囊仍是 `2.3 亿 tok · 余0.95`（单行不溢）；面板内 `今日用量`、`今日 2.3 亿 token / 1382 次 / 约0.95 CNY`、`任务`、`2 个在跑`、`Chief · 在跑`、`FloatIsle · 在跑`、`媒体`、`未播放` |
| 命中（`WindowFromPoint` 比 PID） | 胶囊中心 (1316,30) ✓本进程；胶囊左侧空白 (1046,30) → 别的进程（穿透）；面板内 (1316,120) ✓；面板圆角外 (1039,63) → 别的进程 |
| 离开后收回 | `RECT=1228,0,176x60`（与展开前逐像素相同） |
| 胶囊模式对照 | 收起 176×60、展开 `1148,0,336x166`，行为未变 |
| CPU（3 任务、光标在岛外、只推一次心跳） | 30 s 两轮 **12.00 / 2.00 个时间片**（15.625 ms 一片）；第二轮 2 片是 25 s 心跳超时后呼吸已停，与胶囊模式基线（中位 12–17 片）同档，**没有新增常驻定时器** |

截图（只存盘、未读）：`C:\Users\12576\AppData\Local\Temp\floatisle-j\top-mode-collapsed.png`、`top-mode-expanded.png`、`capsule-mode-collapsed.png`、`capsule-mode-expanded.png`。

**工作台卡片布局 / 自动列数 / 拖拽换序 / 设置窗拖动**（100% 档、单屏 2560×1552、工作区宽 2560）：

| 检查 | 读数 |
| --- | --- |
| 4 卡 / 全宽 → 2 列 | 窗口 `1114,0 404x293`，区域 `box 0,8 → 403x284`；卡 `(1131,77,180x87)`、`(1323,77,180x102)`、`(1131,191,180x57)`、`(1323,191,180x87)`；卡高−内容高 = **22**（= 2×(Padding 10 + 边框 1)）；行间距 **12**；面板高 233 = 16+102+12+87+16；窗口高 293 = 60（胶囊 44+2×8）+233 |
| 3 卡（隐藏任务）/ 全宽 → 3 列 | 窗口 `1018,0 596x194`；卡顶全 77；面板宽 596 = 3×180+2×12+2×16；窗口高 194 = 60+16+102+16 |
| 3 卡 / 可用宽压到 1000 → 2 列 | 窗口 `1114,0 404x263`；卡顶 77,77,191 |
| 3 卡 / 可用宽压到 500 → 1 列 | 窗口 `1210,0 212x362`；卡 `(1227,77,180x87)`、`(1227,176,180x102)`、`(1227,290,180x57)`；面板宽 212 = 180+2×16，卡片**没被拉成全宽** |
| 4 卡 / 宽 500 → 1 列 4 行 | 窗口 `1210,0 212x461`，卡顶 77,176,290,359 |
| 关一个模块的高度差 | 同一宽度下 4 卡 461 → 3 卡 362，差 **99** = 任务卡高 87 + 间距 12 |
| 拖「任务」卡到第一张 | 拖前/按下/拖动中/松开四帧 `RECT` 全是 `1210,0 212x461`（岛没被拖走、没触发收起），松手后光标离开才收成 `1228,0 176x60`；`ModuleOrder` `["usage","quota","media","tasks"]` → `["tasks","usage","quota","media"]` |
| 重启后顺序 | 卡顶 77,176,275,389，内容顺序 任务→今日用量→余额/额度→媒体；第二次读 `ModuleOrder` 仍是 `["tasks","usage","quota","media"]` |
| 设置面板上移 | 改前 `ModuleOrder=["usage","quota","media","tasks"]`、卡顶 77,77,191,191（任务在 `1323,191`）→ 对「任务」行按上移 → `["usage","quota","tasks","media"]`、任务卡到 `(1131,191)`、媒体卡到 `(1323,191)`，与列表顺序一致 |
| 设置窗拖动 | 从 `1080,244` 按住空白拖 (−200,+120) → `881,364`（窗口没被拖成半截、内容不抖） |
| 设置窗位置记忆 | 关窗写 `SettingsLeftPx=881 SettingsTopPx=364`；重启后设置窗开在 `881,364`（改前是 `1080,244`） |
| 记忆位置在屏外 | 手改 `SettingsLeftPx=SettingsTopPx=99999` 后重启 → 回落居中 `1080,244` |
| 面板宽上限 | 三档实测 404 / 596 / 212，均 ≤ 工作区宽一半（1280） |

截图（只存盘、未读，均在 `C:\Users\12576\AppData\Local\Temp\floatisle-j\`）：`c2.png`（4 卡 2 列）、`c3.png`（3 卡 3 列）、`c1.png`（3 卡 1 列）、`u0.png`~`u3.png`（上移/下移前后）。

**未实测**：150% 档的贴顶工作台与卡片网格（自检里那条 150% 是 `×1.5` 构造用例，用例名已标注「未实机」）；真双屏下贴顶模式的展开落位；物理鼠标（只有注入事件）；卡片拖拽与设置窗拖动也只用注入鼠标测过。

## 毛玻璃（系统 Acrylic 判不可用 → 自绘假磨砂）

结论：**不可用**。判据用同一个 exe、同一块屏、100% 缩放、胶囊态窗口 `RECT=1228,0,176x60`（区域 `8,8,159x43`），只切 `SetWindowCompositionAttribute` 的 accent state：

| 判据 | Acrylic ON（`ACCENT_ENABLE_ACRYLICBLURBEHIND`） | Acrylic OFF |
| --- | --- | --- |
| API 返回 | `rc=1 err=0`，`WS_EX_LAYERED` 在位，`exstyle=0x08080088`，OS `10.0.26300.0` | 同 |
| 区域外 29,003 个像素的差分 | `meanDiff=30.11 maxDiff=244 over10=3723` | `meanDiff=0.00 over10=0` |
| 被改像素的颜色 | `R=G=B=12.4`（近黑不透明，铺满整块矩形） | 桌面原样 `247,247,247` |
| 影响范围 | `bbox=1228,0,1403,59` = **整个窗口矩形** | 无 |

原因：DWM 的 accent 合成在**矩形**上做，不认 `SetWindowRgn` 的异形裁剪，圆角外与「点击穿透」的透明区会被一起糊掉，异形与穿透同时失效。所以不走系统 Acrylic。

替代是自绘假磨砂，全部画在胶囊形状内：

- 底：65% 半透明深色 `#A60E1014`（悬停/展开不变色）
- 边缘高光：内侧 1px `#26FFFFFF`，外加 Island 自身的 1px `#3CFFFFFF` 边框
- 区域外仍然全透明（判据：同一画面「显示 vs 隐藏窗口」两张截图的差分，`OUTSIDE meanDiff=0.00 over10=0`，29,003 个像素一个都没变）

噪点纹理试过三种实现（烘进背景 128×128、32 DIP 整数平铺、512×256 1:1 直贴），5 fps 呼吸时 **14–17 片/30 s**（预算 10 片），所以**不做**——代价是「磨砂」只有色阶、没有颗粒感，且是固定深色，不跟系统浅色主题走。

**展开/收起换成弹簧缓动**：`BackEase(EaseOut, Amplitude=0.19)`、200 ms。幅度是标定出来的，不是拍的：

| Amplitude | 最大过冲（160 DIP 行程） |
| --- | --- |
| 0.12 | 0.99 DIP（几乎看不出「弹」） |
| 0.19 | **3.06 DIP**（采用） |
| 0.35 | 11.07 DIP（超出 4 DIP 要求） |

实测（10 ms 采样 `GetWindowRgn`）：`OVERSHOOT width=3px height=1px`，峰值在 90–130 ms，140 ms 已回稳态 `319×119`；序列 `0:159x43 → 40:270x96 → 70:315x117 → 90:322x120 → 100:322x120 → 140:319x119`。弹簧只在展开/收起那 200 ms 里跑，跑完动画时钟结束、不常驻。

悬停质感：`MouseEnter` 只把边框从 `#3CFFFFFF` 提到 `#66FFFFFF`（改 Brush，不改尺寸，不新增动画），离开由 150 ms 定时器确认后还原——之前直接改尺寸会让窗口每帧重设 region，触发补发 `MouseEnter` 的死循环。完成脉冲的缓动加了 `SineEase`，**时长不变**，600 ms 三连峰时序与之前一致。

## 呼吸辉光（有活在跑才亮）

`TasksHub` 里还有会话在跑（在跑 / 等你回答 / 等你批准）时，胶囊内侧画**两层同心描边**并整圈同步呼吸：内层 3 px「芯」（`Opacity` 0.28↔1.0）、外层 7 px「晕」（0.02↔0.60），`SineEase` 缓入缓出，只动两层的 `Opacity`（胶囊本体和文字不参与动画）。没有任务在跑 → 两层整条隐藏，**不留常驻透明动画**。

**颜色按状态换（优先级从高到低；高者覆盖低者）**

| 状态 | 颜色 | 节奏 |
| --- | --- | --- |
| 任一会话在**等你批准 / 等你回答**（`approval` / `question`） | 琥珀 `#F5A623`（常量，不开放改） | 呼吸 **1.2 秒**一个来回（比蓝更急），振幅同蓝 |
| 有会话**在跑** | `GlowColor`（设置面板唯一可改的辉光色，默认 `#4DA3FF`） | 呼吸 **1.6 秒** |
| **完成**（`turn/end` 且 `reason=completed`，且没有别的会话在跑/在等） | 绿 `#3DBE6B`（常量，不开放改） | 闪 3 次（每次 300 ms）后常亮绿 |
| 空闲 / 无任务 | 不画 | — |

规则细节：`approval` / `question` 到达后**在同一帧内**换成琥珀（实测 POST 返回后第一帧 `+4 ms` 已是琥珀，上限一个 UI 帧 16 ms）；多任务时只要有一个在等你，整体就琥珀。`turn/end` 时**若还有别的会话在跑或在等，不出绿**，直接回到对应颜色的呼吸（绿只属于「全部干完」）；新 `turn/start` 覆盖绿常亮回蓝。琥珀与绿是代码常量，设置面板仍只有「呼吸辉光颜色」一项（`GlowColor`）。

两层都画在胶囊内侧（外圈用 `Margin=3.5` + 粗细 7，外沿正好压在胶囊边缘），因为窗口被 `SetWindowRgn` 裁成胶囊形状，画到形状外会被切掉；也**不用 `Effect.Blur`**——分层窗口下 Blur 会把每帧成本翻几倍。

收到 `turn/end` 且 `reason=completed`（且没有别的会话在跑/在等）：**900 ms 内脉冲 3 次**（每次 150 ms 上 + 150 ms 下，芯顶满、晕顶到 0.75，绿色），随后**常亮绿**（芯 1.0、晕 0.35 静态）；**只有点开该会话（跳转 202）或按「清除全部已完成」才清那一条**，不自己超时消失；新任务开始跑则常亮让位给蓝色呼吸。`aborted`/`error` 不做特殊样式（TBD）。

**窗口贴合内容**：收起态窗口 = 胶囊 160×44 + 两侧 8 DIP 描边余量 = `176×60`（原来固定 `380×180`，九成面积是透明空气，`AllowsTransparency` 的分层窗口每帧都要软件合成它）。展开时窗口先长到 `336×136` 再跑动画，收起动画完成后才收窗口；区域永远装在窗口内，拖动期间不收窗口。

实测（100% 缩放、亮屏、单屏 2560×1552，按 15.625 ms 一个时间片）：

| 检查 | 读数 |
| --- | --- |
| 收起 / 展开窗口 | `RECT=1192,66,1368,126`（176×60）/ `RECT=1112,28,1448,164`（336×136），窗口中心恒 1280,96 |
| 区域 | 收起 `box=8,8,167,51 → 159×43`；展开 `box=8,8,327,127 → 319×119` |
| 面积 | 收起 10,560 px² vs 旧 68,400 px² = **1/6.5** |
| 形状命中（收起态） | 胶囊中心 (1280,96)、左内缘 (1230,96)、上内缘 (1280,76) → 岛；圆角外 (1205,80)、胶囊下方 (1280,122)、窗口左上 (1192,66) → 别的窗口 |
| 呼吸亮度（圆环带 0–7 DIP 平均 B，`PrintWindow`，40 ms 采样） | **双层改造前** `MIN=63.82 MAX=117.89 RATIO=1.85`；**改造后** `MIN=48.22 MAX=153.06 RATIO=3.17`（外圈带 `MIN=54.18 MAX=208.38 RATIO=3.85`），周期 1.6 s，`ZERO_FRAMES=0` |
| 呼吸亮度（同口径，若按 0.85↔1.0 / 0.12↔0.55 的温和上下限） | `MIN=110.52 MAX=150.15 RATIO=1.36` —— 芯一直接近饱和会把整带比值封顶在 1.x，达不到 ≥3，所以下限压到 0.28 / 0.02 |
| 区域外蓝像素 | 两层同时呼吸时 `OUTSIDE_BLUE=0`（每帧 0），外圈没有越过窗口内缩边界 |
| 完成时序（同法 40 ms 采样，岛区平均 B） | `90.9 → 60.0 → 97.2 → 107.8 → 97.2 → 60.0 → 97.2 → 107.8 → 97.2 → 60.0 → 97.2 → 107.8 → 97.2`，峰值在 192 / 516 / 809 ms（3 个峰，间隔 ~300 ms）→ **933 ms 起恒定 83.3 = 常亮（芯 1.0 / 晕 0.35）** |
| 呼吸 5 fps CPU（双层改造后，3 个任务在跑） | **203.1 / 234.4 / 281.3 ms = 13 / 15 / 18 个时间片**（30 秒三连测，中位数 15；单层时代同口径中位数 17） |
| 呼吸 5 fps CPU（单层时代，历史读数） | **125 / 171.9 / 140.6 ms = 8 / 11 / 9 个时间片**（30 秒） |
| 呼吸 10 fps CPU（同条件） | **546.9 / 328.1 / 281.2 ms = 35 / 21 / 18 片**（超预算，所以定 5 fps） |
| 三态主导色（环带 0–7 DIP 最亮像素，`PrintWindow`） | 蓝 **(77,163,255)**、琥珀 **(245,166,35)**、绿 **(61,190,107)** —— 与目标 `#4DA3FF` / `#F5A623` / `#3DBE6B` 逐通道一致（0 偏差），三态两两可区分 |
| 区域外彩色像素（三态各 40+ 帧） | `out=0`（每帧 0）：琥珀与绿同样没有越过窗口内缩边界 |
| 切琥珀延迟（推 `approval` 后 16 ms 一采） | `+4 ms` 首帧已是琥珀 `(222,151,33)`（呼吸暗相），`+92 ms` 到峰值 `(245,166,35)`；上限一个 UI 帧 |
| 完成 → 常亮（20 ms 采样，绿色最亮像素） | 峰在 `182 / 475 / 753 ms`（3 个峰，间隔 ~300 ms），`938 ms` 起恒定 `(61,190,107)` = 常亮绿 |
| 绿常亮 → 新任务开跑回蓝 | `STEADY (61,190,107)` → `turn/start` 后 400 ms 采样为蓝 `(60,125,195)`（呼吸暗相，色相已回蓝） |
| 三态切换 CPU（3 任务 30 s ×3，蓝色呼吸） | **187.5 / 140.6 / 312.5 ms = 12 / 9 / 20 个时间片**，中位数 12（上限 25） |
| 常亮静态 CPU | **15.6 / 31.2 ms = 1 / 2 片**（静态描边，不跑动画） |
| 无任务空闲 CPU | **265.6 ms / 17 片（呼吸收尾那一段）→ 0 ms / 0 片** |

判据：预算 10 片/30 秒，10 fps 超了，5 fps 落在 8–11 片（脉动看着仍然连续，因为周期是 2 秒）。

**150% 档（真机切到 150%，`GetDpiForWindow=144`）**：

| 检查 | 读数 |
| --- | --- |
| 窗口 `RECT` | 收起 `1148,9,1412,99`（264×90 = 176×60 ×1.5），中心 `1280,54` |
| 区域 `GetWindowRgn` | `type=3 box=12,12,251,77` → 239×65（159×43 ×1.5，比例 1.503 / 1.512） |
| 描边内缩（扫蓝像素，相对区域） | 左 4 px / 右 2 px / 上 4 px / 下 2 px；区域外蓝像素 **0**（没被 region 切掉） |
| 描边 3 DIP | = 4.5 px，落在 2–4 px 内缩后仍全在形状内 |
| 呼吸振幅（法线探针） | 无辉光 44.2；呼吸亮相 190.4 / 201.6 |
| 完成闪采样（每 ~33 ms） | 修前：600 ms 内只有一段慢斜坡、随后回到 2 秒明暗——根因是**每次窗口尺寸变化都补发 `MouseEnter`，把 `Animate()` 从当前帧重开 180 ms**，辉光动画被反复打断（周期恰好等于动画时长）。修后给 `Animate()` 加「同方向进行中直接返回」守卫，一次悬停 5 s 内只记到 2 次调用（第二条被守卫返回），悬停展开态蓝像素 3 s 采样 `MIN=3238 MAX=4448`、无 0，2 s 周期稳定 |

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
| 单屏默认 / 单屏存过位置 | `(1192,6,176x60)` / `(240,420,176x60)` |
| 双屏左右：落在第二块屏 / 坐标越界夹回 | `(3000,100)` / `99999 → (4304,100)` |
| 双屏上下：落在下方屏 | `(100,2000)` |
| 负坐标显示器（第二块在左） | `(-1900,10)` 保持为负，不折回 0 |
| 保存的显示器已拔掉 | 回落主屏顶部居中 `(1192,6)` |
| 窗口比工作区还大 | 贴该屏左上角 `(2560,0)` |
| 150% 缩放（第一条是实机读数） | 默认 `(1148,9,264x90)`（与 150% 掉位实测一致）；存过的像素坐标不换算 `(240,420,264x90)`（构造用例） |
| 展开尺寸 `336x136` 传入 | 夹紧后 `(1112,6)`；仅验证夹紧，展开态不参与落位 |

11 / 11 全过（`ALL PASS (11)`）。窗口尺寸从 `MainWindow` 的常量派生（收起 `176×60`、展开 `336×136`），常量一改自检就会红——不再有「测试还在跑、验的是旧尺寸」的假绿灯。**但这台机器只有一块屏，上表是自检不是真机多屏——「真双屏未实测」。**

### 高 DPI 实测（100% 档）

| 检查 | 读数 |
| --- | --- |
| DPI 上下文 | `AreDpiAwarenessContextsEqual(ctx, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` = **True**，与 PMv1 比较 = False；`GetProcessDpiAwareness` = 2；`GetDpiForWindow` = 96 |
| 窗口 `RECT` | `1192,66,1368,126`（176×60 窗口 = 胶囊 160×44 + 两侧 8 DIP 描边余量，2560×1600 屏） |
| 区域 `GetWindowRgn` | `type=3 box=8,8,167,51` → 159×43（相对窗口） |
| `EXSTYLE` | `0x08080088`（`WS_EX_NOACTIVATE` 仍在，没抢焦点） |
| 形状命中 | 胶囊中心 (1280,96)、胶囊左内缘 (1230,96)、胶囊上内缘 (1280,76) → 岛；窗口内圆角外 (1205,80)、胶囊下方 (1280,122)、窗口左上角 (1192,66) → 别的窗口 |
| 空闲 10 s CPU | **0 ms** |
| 前台窗口 | 启动前后同一句柄（2429132），未抢焦点 |

### 高 DPI 实测（150% 档，真机切换后）

系统设置 → 显示 → 缩放 → **150%（预设）**，`AppliedDPI=144`、物理工作区 `0,0,2560,1528`（任务栏 72 px）。岛未重启、由程序重新落位（清空 `LeftPx/TopPx` 后重启，程序写回 `1148,9`）：

| 检查 | 读数 |
| --- | --- |
| `GetDpiForWindow` | **144** |
| 收起 `RECT` | `1148,9,1412,99`，264×90（176×60 ×1.5），中心 `1280,54` |
| 收起区域 | `type=3 box=12,12,251,77` → 239×65（159×43 ×1.5） |
| 展开 `RECT` | `1028,9,1532,258`… 实测 `1028,9,504×249`：**上边 9 不动、左右居中向下长**（修掉「展开越出屏幕顶」后） |
| 展开区域 | 稳态 `type=3 box=12,12,491,236` → **479×224**（= 319.3×149.3 DIP，100% 是 319×149）；动画中途取样会是 442×199，不能当稳态 |
| 收回 | `1148,9,1412,99`、区域 239×65（与展开前逐像素一致） |
| 形状命中 | 胶囊中心 (1280,55)、左内缘 (1168,55)、上内缘 (1280,29) → 岛；圆角外 (1164,25)、窗口左上 (1150,11) → 别的窗口 |
| `EXSTYLE` | `0x08080088`（不抢焦点） |
| 设置面板 | 窗口 `980,76,600×1375`（400×916.7 DIP），内容右边界 1569 < 1580 → 无水平越界；内容底 1657 进滚动条（viewport 84.5%），滚到底「关闭」按钮可见 |
| 设置面板文本 | `TextTrimming` 未设，文本块高度 23/45/46/69 px 是换行不是省略号 |

**150% 暴露、已修的两处几何问题**：
1. **展开态曾越出屏幕上方约 70 px**：旧实现以窗口中心为锚放大，收起顶边只有 9 px 时窗口 top 跑到 -70。改为「上边不动、向下长」（`Placement.GrowDown`：水平居中、纵向从当前 top 向下扩，超出工作区才 `ClampInto`），实测展开后上边仍是 `9`，收回逐像素回到 `1148,9`。
2. **展开区域曾被读成 442×199**：那是动画中途取样（两条轴进度 p≈0.84 一致）。等 1.2 s 动画结束后稳态是 **479×224**，与 100% 的 319×149 同比例。

**DPI 虚拟化的读法**：未声明 DPI 感知的探针进程读同一个岛窗口会得到 `176×60 @765,6`（`765×1.5=1147.5`、`6×1.5=9`），DPI 感知进程读到的是 `264×90 @1148,9`，`GetDpiForWindow(岛)=144`。所有坐标类读数必须用 DPI 感知进程，否则会被系统按 96 DPI 虚拟化，看起来像「窗口尺寸不对/位置没生效」。

程序化改缩放（`DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE` / `SetDisplayConfig+SDC_APPLY` / `SPI_SETLOGICALDPIOVERRIDE`）在这台机器上仍然全部无效，所以上面这组读数是**手动切缩放**拿到的；`WM_DPICHANGED` 在真实切换时的重排结果**未单独实测**（本轮是重启后落位，不是运行中跨屏/改缩放）。

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
14. **贴顶工作台**：`MainWindow.ApplyMode/OpenTopWorkbench/CloseTopWorkbench/RefreshTopTiles/LayoutTiles/MeasureWorkbenchHeight`，卡片是 `Grid`（列数由 `Placement.WorkbenchLayout` 按可用宽度自动推导）+ `Tile()` 现拼；旧的 `TopEdgeHook.cs`（`WH_MOUSE_LL` 碰顶唤醒）与 `OnTopWake` 已整条删除，展开只由悬停胶囊触发。`UpdateWindowRegion()` 在贴顶模式把胶囊与面板两块圆角区域用 `CombineRgn(RGN_OR)` 合起来，胶囊模式只取岛。

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
- 托盘图标用仓库里 `assets/floatisle.ico`（九档 16/20/24/32/40/48/64/128/256），按 `GetDpiForWindow` 选档（`16×dpi/96`，100% → 16 px、150% → 24 px），同时作为 exe 的 `ApplicationIcon` 与嵌入资源，不依赖 exe 旁的散文件。
- **拖动只在胶囊模式**：贴顶模式下胶囊常驻但不可拖（位置由工作台的贴顶规则决定），面板横向位置跟胶囊走；拖岛时穿过窗口的鼠标事件需要穿透关闭（`Ctrl+Alt+I`）。
- **工作台卡片拖拽只在面板内生效**：`TopTiles.CaptureMouse()` 独占鼠标，`_draggingTile` 期间 `ScheduleCollapse()` 直接 return，所以拖卡片既不会把岛拖走、也不会触发展开/收起、更不会触发任务行跳转（任务行在胶囊模式，两者不重叠）。代价是拖拽期间鼠标离开面板也保持展开，松手才恢复正常判定。
- **卡片高度不设固定值**：`Height`/`MinHeight` 一律不写（自检断言 `Height` 是 `NaN`、`MinHeight == 0`），面板高由内容撑开；行内明细每条独立成行，不再用 ` · ` 拼串，长文案用 `CharacterEllipsis` 截断而不是换行压到下一行。
- **不做自由坐标摆放（卡片拖到任意 x/y）**：那会直接破掉「自动列数」和「最后一行按实际高度收口」两条——卡片可以叠在一起、也可以在半空留洞，等于把布局责任推给用户。现在的「拖拽换序 + 自动列数」已经满足「我怎么拖就怎么排」；真要任意摆放得单开一套自由画布布局，不在这个网格上硬改。
- **面板宽上限是工作区宽的一半**：卡片最小 180 DIP、最大 420 DIP，1 列时保持最小宽不拉整宽；所以内容特别短时面板会留白（180 宽），内容特别长时明细先被省略号截断。这个上限是刻意定的（岛是浮在别人窗口上的，不该占半屏以上）。
- **`ModuleOrder` 与 `Modules` 双写**：内存里只有 `Modules` 一个真源，`ModuleOrder` 是给外部/手改留的兼容字段，只在 `SettingsStore.Save` 时回写。手改 `ModuleOrder` 后要保证 `Modules` 为空才会被采用；两者不一致时以 `Modules` 为准。
- **设置窗整窗拖动用 `PreviewMouseLeftButtonDown`**：普通 `MouseLeftButtonDown` 会被 `ScrollViewer` 吃掉（实测 Window 上收不到事件、窗口拖不动），所以挂在隧道事件上；命中 `ButtonBase/TextBoxBase/ComboBox/Slider/ScrollBar/Thumb/PasswordBox` 时不抢拖动。设置窗位置按设备像素存 `SettingsLeftPx/TopPx`，记忆位置不在任何工作区内（含拔掉显示器）回落居中。
- **完成状态按会话记账，不会自己消失**（用户要求：「我点进去那个会话就清除掉完成，没点进去过的就一直显示已完成」）：`turn/end` 且 `reason=completed` 时，该 `sid` 从在跑表移进「已完成未查看」账本（`DoneLedger`），岛展开后任务区**最上面**显示 `工作区名 · 已完成`（绿色系 + 明确的「已完成」三个字，不只靠颜色），最多 4 行，超出用 `还有 N 个已完成` 兜住。
  清除只认三件事：① 点这一行且 `POST /jump` 返回 202（跳转被受理才算看过，失败会提示且**不清**）；② 该 `sid` 又来 `turn/start`（重新开跑）；③ 点任务区最后一行的 **`清除全部已完成`**。工作台（贴顶形态）的任务卡片里同样显示这些行并可点，卡片底部也有同一个清除入口。
  **没有超时、没有轮询**：绿色常亮 = 「账本非空」这个事实的可视化，账本空了才灭；辉光优先级仍是 `等你确认（琥珀）> 有任务在跑（蓝呼吸）> 有未查看的完成（绿常亮）> 不发光`。展开岛**不再**算已读（旧行为是展开即清，与「点进会话才清」冲突，已删除）。
  已知取舍：账本只活在内存里，重启 FloatIsle 或 DSH 心跳 25 秒超时后账本仍在（不自动消失），但**进程重启就丢**——没做持久化，因为「未查看」是即时状态，落盘反而会在下次开机时冒出早已看过的绿。
- **已加全局单实例互斥**（`App.OnStartup`：`new Mutex(true, "Global\\FloatIsle.SingleInstance")`）：第二个实例 `createdNew == false` 直接 `Shutdown()` 退出，不做「激活已有实例」的 IPC。互斥不覆盖 `--selftest`（装着的实例在跑时也能跑自检）。
- **琥珀 / 绿两态只在 100% 下测过**：三态色值与切换时序都是 100%、单屏 2560×1552 的读数；150% 下只验过蓝色呼吸（描边不越界、区域外 0），琥珀与绿的 150% 读数未测。
- **呼吸辉光在 150% 下只验到「描边不越界 + 呼吸不灭」**：描边四边内缩 4/2/4/2 px、区域外蓝像素 0、振幅 44.2→201.6；150% 的完成闪 3 连峰时序**没有**与 100% 同套采样（150% 只跑了修前那组），上面修后序列是 100% 档读数。
- **展开越屏已修，但只在 150% + 单屏验证**：`GrowDown` 的上边固定 + 向下长在 `--selftest` 有 100%/150% 两条用例；真双屏/负坐标屏的展开落位仍未实测。
- **呼吸刻意降到 5 fps**：10 fps 实测 18–35 片/30 秒、超出 10 片预算。代价是极端情况下能看到 ~10 级不透明度的台阶（周期 2 秒、正弦缓动，肉眼看仍连续）。要更顺就得改渲染路径（独立合成层），这是已知取舍。
- **5 fps 呼吸的 CPU 成本（已接受，不再优化）**：同口径（3 个任务、光标停在岛外、30 s × 15.625 ms）**双层辉光**实测中位数 **15 片/30 s**、范围 **13–18 片**（单层时代是 17 片 / 13–21）。同一进程任务超时后的空闲是 5–6 片。这是「一个能跑的呼吸动画」的代价，10 片那条线是开发期自定的保守值、不是产品要求；不再靠降帧率、缩短动画或删描边去凑。
- **双层辉光的亮度下限压得比直觉低**：芯 0.28、晕 0.02 时暗相几乎只剩背景，这是为了把「圆环带平均 B」的 MAX/MIN 拉到 3 以上（肉眼可见的呼吸）。如果觉得暗相对太黑，改 `BreathAnimation` 的两个下限即可，但比值会一起降（0.85/0.12 对应 1.36）。
- **空闲底噪 5–6 片/30 s 来自全屏兜底的 1 秒轮询**：`FullscreenWatcher` 每秒一次 `SHQueryUserNotificationState` + 前台窗口几何校验（30 次正好 5–6 片），是「用户启动/结束全屏应用不发通知」这条系统行为逼出来的设计成本，不是回归。早前「空闲 0 片」是短窗口量的，不具可比性。
- **系统 Acrylic 判不可用（只报数不改）**：`SetWindowCompositionAttribute`+`ACCENT_ENABLE_ACRYLICBLURBEHIND` 会把整块矩形窗口糊成近黑（`R=G=B=12.4`、区域外 3,723 像素被改、bbox 覆盖整个窗口），不认 `SetWindowRgn`。假磨砂因此是自绘的、固定深色、无颗粒感，也不跟系统浅色主题。
- **弹簧过冲是标定值不是无限可调**：`BackEase` 的 Amplitude 0.19 → 3.06 DIP（行程 160 DIP），实测最大 3 px 宽/1 px 高；换成更大的行程（展开到有任务行 166 px 高）过冲比例不变，但**只在 100% 的 `GetWindowRgn` 上量过**，150% 的过冲未采样。
- **假磨砂的「磨砂感」只有两级**：65% 半透明底 + 1px 高光，没有噪点（噪点纹理 14–17 片超预算），强光环境下会显得比真磨砂「平」。
- **多屏只有自检，没有真机**：本机只有一块 2560×1600 屏，`Placement` 的 10 条用例是构造布局跑出来的纯函数自检；真实插拔第二块屏、跨屏缩放不同的场景**未实测**（设备名 `\\.\DISPLAY2` 这类在真机上是否稳定也给不出结论）。
- **150% 有真机读数，但运行中改缩放未验**：150% 的 `RECT`/区域/命中/设置面板排版都是手动切缩放后重启落位测的（见上表）；`WM_DPICHANGED` 在**运行中**跨屏/改缩放时的重排仍只有合成消息的证据。150% 以上（175%/200%）没有任何读数。
- **DPI 变化时不主动改字号**：字号与边距是 DIP，交给 WPF 按新 DPI 自己缩放（WPF 里再乘一次会变成双倍）。这条依赖 WPF 行为，未实测验证。
- **设置面板高度封顶到工作区 90%**：内容超出时进滚动条，避免高缩放 + 小屏时窗口长出屏幕。150% 实测窗口 600×1375 px（400×916.7 DIP），内容无水平越界、纵向进滚动条（viewport 84.5%）。
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

