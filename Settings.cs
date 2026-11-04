using System;
using System.IO;
using System.Text.Json;

namespace FloatIsle;

/// <summary>
/// 落盘的配置。真源只有这一份；开机自启不在这里存（真源是注册表，见 AutoStart）。
/// 字段名就是 JSON 键名，改字段等于改文件格式，README 里写着了。
/// </summary>
internal sealed class AppSettings
{
    /// <summary>窗口左上角（DIP）。null = 用默认的顶部居中。</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>上次所在的显示器设备名（如 \\.\DISPLAY1）。多屏还原未实现，只做记录。</summary>
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

    /// <summary>手改文件是信任边界：越界值夹回来，别让 NaN/负数传到动画里。</summary>
    internal void Normalize()
    {
        if (double.IsNaN(Opacity) || double.IsInfinity(Opacity))
        {
            Opacity = 1.0;
        }
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        HoverDelayMs = Math.Clamp(HoverDelayMs, 0, 2000);
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
