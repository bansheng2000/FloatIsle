using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace FloatIsle;

/// <summary>
/// 开机自启：只在 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 下写一个字符串值。
/// 关掉时直接删值（不是写空字符串），否则 Run 里会留一个空项。
/// </summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FloatIsle";

    /// <summary>命中的是当前进程的 exe 路径（Release 产物）；换目录后旧值就失效，README 写了怎么修。</summary>
    internal static string? ExecutablePath => Environment.ProcessPath;

    internal static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            string? value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>true = 写入自启项；false = 删除该项。返回是否操作成功。</summary>
    internal static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                string? exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe))
                {
                    return false;
                }
                key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
