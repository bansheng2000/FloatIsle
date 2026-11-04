using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FloatIsle;

/// <summary>岛上可排序的模块。Id 就是 settings.json 里的字符串，改它等于改文件格式。</summary>
internal sealed class IslandModule
{
    public string Id { get; set; } = "";

    public bool Visible { get; set; } = true;
}

/// <summary>模块 id 与默认顺序的唯一真相源（岛的顺序、设置面板的行顺序都从这里来）。</summary>
internal static class IslandModuleIds
{
    internal const string Usage = "usage";
    internal const string Quota = "quota";
    internal const string Media = "media";
    internal const string Tasks = "tasks";

    /// <summary>新装用户看到的顺序。任务面板在最下面（没接上心跳时它整体不显示）。</summary>
    internal static readonly string[] DefaultOrder = { Usage, Quota, Media, Tasks };

    internal static string Label(string id) => id switch
    {
        Usage => "今日用量",
        Quota => "余额 / 额度",
        Media => "媒体信息",
        Tasks => "任务面板",
        _ => id
    };

    internal static bool IsKnown(string id) => DefaultOrder.Contains(id);
}

/// <summary>
/// 落盘的配置。真源只有这一份；开机自启不在这里存（真源是注册表，见 AutoStart）。
/// 字段名就是 JSON 键名，改字段等于改文件格式，README 里写着了。
/// </summary>
internal sealed class AppSettings
{
    /// <summary>窗口左上角，<b>设备像素</b>（不是 DIP：多屏缩放不同时 DIP 没法跨屏比较）。null = 默认顶部居中。</summary>
    public double? LeftPx { get; set; }
    public double? TopPx { get; set; }

    /// <summary>v0.2-D 及更早存的 DIP 坐标，只在读旧文件时迁移一次；存新位置时清成 null。</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>上次所在的显示器设备名（如 \\.\DISPLAY1）。多屏还原按设备名找屏，找不到回落主屏顶部居中。</summary>
    public string? Screen { get; set; }

    /// <summary>岛的不透明度，0.2–1.0。</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>整窗点击穿透的默认状态（热键/托盘切换时也会写回这里）。</summary>
    public bool ClickThrough { get; set; }

    /// <summary>是否显示 SMTC 媒体信息。</summary>
    public bool MediaEnabled { get; set; } = true;

    /// <summary>全屏/锁屏时是否自动让位。</summary>
    public bool FullscreenSuppress { get; set; } = true;

    /// <summary>鼠标停在岛上多久后展开，0 = 立即。</summary>
    public int HoverDelayMs { get; set; }

    /// <summary>岛上模块的显示开关与顺序（数组顺序就是显示顺序）。</summary>
    public List<IslandModule> Modules { get; set; } = new();

    /// <summary>收起时主行显示哪一项：media / usage / tasks。</summary>
    public string CollapsedLine { get; set; } = IslandModuleIds.Media;

    /// <summary>任务面板的插件侧地址（占位：监听端还没接）。</summary>
    public string TasksEndpoint { get; set; } = DefaultTasksEndpoint;

    internal const string DefaultTasksEndpoint = "127.0.0.1:47821";

    /// <summary>手改文件是信任边界：越界值夹回来，别让 NaN/负数传到动画里。</summary>
    internal void Normalize()
    {
        if (double.IsNaN(Opacity) || double.IsInfinity(Opacity))
        {
            Opacity = 1.0;
        }
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        HoverDelayMs = Math.Clamp(HoverDelayMs, 0, 2000);

        NormalizeModules();

        if (CollapsedLine is not (IslandModuleIds.Media or IslandModuleIds.Usage or IslandModuleIds.Tasks))
        {
            CollapsedLine = IslandModuleIds.Media;
        }

        TasksEndpoint = string.IsNullOrWhiteSpace(TasksEndpoint) || TasksEndpoint.Length > 64
            ? DefaultTasksEndpoint
            : TasksEndpoint.Trim();
    }

    /// <summary>
    /// 手写的文件可能缺模块、多模块、重复：只认已知 id，去掉重复，缺的按默认顺序补在后面。
    /// 这样新增模块时老配置也能自动长出来，不用写迁移代码。
    /// </summary>
    private void NormalizeModules()
    {
        var ordered = new List<IslandModule>(IslandModuleIds.DefaultOrder.Length);
        foreach (IslandModule? module in Modules ?? new List<IslandModule>())
        {
            string id = module?.Id ?? "";
            if (!IslandModuleIds.IsKnown(id) || ordered.Any(m => m.Id == id))
            {
                continue;
            }
            ordered.Add(new IslandModule { Id = id, Visible = module!.Visible });
        }

        foreach (string id in IslandModuleIds.DefaultOrder)
        {
            if (!ordered.Any(m => m.Id == id))
            {
                ordered.Add(new IslandModule { Id = id, Visible = true });
            }
        }

        Modules = ordered;
    }
}

/// <summary>读取结果：坏文件被改名后要把 .bak 路径报给用户/设置面板。</summary>
internal readonly record struct SettingsLoadResult(AppSettings Settings, string? BackupPath, string? Error);

/// <summary>
/// settings.json 的读写：%APPDATA%\FloatIsle\settings.json。
/// 读坏了不崩：改名成 settings.json.bak（不静默删），返回默认值。
/// 写用 .tmp + 替换，避免中途被杀留下半个 JSON。
/// </summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatIsle");

    internal static string FilePath => Path.Combine(FolderPath, "settings.json");

    internal static string BackupPath => FilePath + ".bak";

    internal static SettingsLoadResult Load()
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return new SettingsLoadResult(new AppSettings(), null, null);
        }

        try
        {
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            if (settings is null)
            {
                throw new JsonException("settings.json 解析出 null");
            }
            settings.Normalize();
            return new SettingsLoadResult(settings, null, null);
        }
        catch (Exception ex)
        {
            // 坏文件不删，改名留证；改名失败也不能崩，直接用默认值起来
            string? backup = null;
            try
            {
                File.Move(path, BackupPath, overwrite: true);
                backup = BackupPath;
            }
            catch
            {
                backup = null;
            }
            return new SettingsLoadResult(new AppSettings(), backup, ex.Message);
        }
    }

    internal static bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch
        {
            // 写不进去（权限/磁盘）不该让程序崩；设置只是没落盘
            return false;
        }
    }
}
