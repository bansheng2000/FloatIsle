using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FloatIsle;

/// <summary>
/// cost-meter 账本里我们用到的三块：今天的用量、余额、套餐额度百分比。全部只读，绝不写回。
/// 金额字段在订阅计划下恒为 0，所以只有 cost &gt; 0 时才显示钱（见 README）。
/// </summary>
internal sealed record UsageSnapshot(
    string Day,
    long Input,
    long Output,
    long CacheRead,
    long CacheWrite,
    int Calls,
    double Cost,
    string? Currency,
    double? Balance,
    int? FiveHour,
    int? Weekly,
    int? Monthly)
{
    // token 总量只加 input/output/cacheRead/cacheWrite：reasoning 通常已含在 output 里，再加会双计
    internal long Tokens => Input + Output + CacheRead + CacheWrite;

    internal bool HasCost => Cost > 0;
}

/// <summary>
/// 读 %USERPROFILE%\.dsh\storages\cost-meter\ledger.json。
/// 用 Utf8JsonReader 流式扫，只挑 days[今天] / balanceRef / planSamples.go，其余一律 Skip——
/// 账本 2MB 且里面有一堆无关的大块（价格表、历史），整棵解析成对象树纯属浪费。
/// </summary>
internal static class UsageLedger
{
    private const long MaxBytes = 64L * 1024 * 1024;

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dsh", "storages", "cost-meter", "ledger.json");

    /// <summary>真实账本路径。测试时用环境变量 FLOATISLE_LEDGER 指到副本，永远不碰真文件。</summary>
    internal static string LedgerPath =>
        Environment.GetEnvironmentVariable("FLOATISLE_LEDGER") is { Length: > 0 } p ? p : DefaultPath;

    /// <summary>读不出来（不存在/被占用/坏 JSON/超大）一律返回 null = 无数据，不抛、不弹框、不写回。</summary>
    internal static UsageSnapshot? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length <= 0 || fs.Length > MaxBytes)
                {
                    return null;
                }
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }

            return Parse(bytes);
        }
        catch
        {
            return null;
        }
    }

    // ---------- 显示用的字符串 ----------

    internal static string FormatTokens(long n) => n switch
    {
        >= 100_000_000 => (n / 100_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + " 亿",
        >= 10_000 => (n / 10_000.0).ToString("F1", CultureInfo.InvariantCulture) + " 万",
        _ => n.ToString(CultureInfo.InvariantCulture)
    };

    private static string Symbol(string? currency) => currency switch
    {
        "CNY" => "¥",
        "USD" => "$",
        "EUR" => "€",
        _ => ""
    };

    /// <summary>「今日 2.4 亿 token / 936 次」。钱只在真的计费（cost &gt; 0）时才出现。</summary>
    internal static string UsageLine(UsageSnapshot? s)
    {
        if (s is null)
        {
            return "今日用量：无数据";
        }
        string line = $"今日 {FormatTokens(s.Tokens)} token / {s.Calls} 次";
        return s.HasCost ? line + $" · {Symbol(s.Currency)}{s.Cost:F2} {s.Currency}" : line;
    }

    /// <summary>「余额 ¥37.56 · 5时 2% · 周 25% · 月 61%」。</summary>
    internal static string QuotaLine(UsageSnapshot? s)
    {
        if (s is null || (s.Balance is null && s.FiveHour is null && s.Weekly is null && s.Monthly is null))
        {
            return "余额：无数据";
        }

        var parts = new System.Collections.Generic.List<string>(4);
        if (s.Balance is double balance)
        {
            parts.Add($"余额 {Symbol(s.Currency)}{balance:F2}");
        }
        if (s.FiveHour is int five)
        {
            parts.Add($"5时 {five}%");
        }
        if (s.Weekly is int week)
        {
            parts.Add($"周 {week}%");
        }
        if (s.Monthly is int month)
        {
            parts.Add($"月 {month}%");
        }
        return string.Join(" · ", parts);
    }

    // ---------- 解析 ----------

    private struct Acc
    {
        public long Input;
        public long Output;
        public long CacheRead;
        public long CacheWrite;
        public int Calls;
        public double Cost;
        public bool SawDay;
        public double? Balance;
        public string? Currency;
        public int? FiveHour;
        public int? Weekly;
        public int? Monthly;
    }

    private static UsageSnapshot? Parse(byte[] bytes)
    {
        var acc = new Acc();
        string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string name = reader.GetString() ?? "";
                if (!reader.Read())
                {
                    break;
                }
                if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
                {
                    continue;
                }

                switch (name)
                {
                    case "days":
                        ReadDays(ref reader, today, ref acc);
                        break;
                    case "balanceRef":
                        ReadBalance(ref reader, ref acc);
                        break;
                    case "planSamples":
                        ReadPlanSamples(ref reader, ref acc);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }
        catch
        {
            // 坏 JSON / 结构不符：不算失败，有什么用什么；一块都没有就是「无数据」
            return null;
        }

        if (!acc.SawDay && acc.Balance is null && acc.FiveHour is null
            && acc.Weekly is null && acc.Monthly is null)
        {
            return null;
        }

        return new UsageSnapshot(today, acc.Input, acc.Output, acc.CacheRead, acc.CacheWrite,
            acc.Calls, acc.Cost, acc.Currency, acc.Balance, acc.FiveHour, acc.Weekly, acc.Monthly);
    }

    private static void ReadDays(ref Utf8JsonReader reader, string today, ref Acc acc)
    {
        int depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string key = reader.GetString() ?? "";
            if (!reader.Read())
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                continue;
            }

            if (key == today)
            {
                ReadDay(ref reader, ref acc);
                acc.SawDay = true;
            }
            else
            {
                reader.Skip(); // 别的日期整块跳过，不建对象
            }
        }
    }

    private static void ReadDay(ref Utf8JsonReader reader, ref Acc acc)
    {
        int depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string name = reader.GetString() ?? "";
            if (!reader.Read())
            {
                return;
            }

            switch (name)
            {
                case "input":
                    acc.Input = ReadLong(ref reader);
                    break;
                case "output":
                    acc.Output = ReadLong(ref reader);
                    break;
                case "cacheRead":
                    acc.CacheRead = ReadLong(ref reader);
                    break;
                case "cacheWrite":
                    acc.CacheWrite = ReadLong(ref reader);
                    break;
                case "calls":
                    acc.Calls = (int)ReadLong(ref reader);
                    break;
                case "cost":
                    acc.Cost = ReadDouble(ref reader);
                    break;
                default:
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        reader.Skip();
                    }
                    break;
            }
        }
    }

    private static void ReadBalance(ref Utf8JsonReader reader, ref Acc acc)
    {
        int depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string name = reader.GetString() ?? "";
            if (!reader.Read())
            {
                return;
            }

            switch (name)
            {
                case "total":
                    acc.Balance = ReadDouble(ref reader);
                    break;
                case "currency":
                    acc.Currency = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                default:
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        reader.Skip();
                    }
                    break;
            }
        }
    }

    private static void ReadPlanSamples(ref Utf8JsonReader reader, ref Acc acc)
    {
        int depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string name = reader.GetString() ?? "";
            if (!reader.Read())
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                continue;
            }

            if (name == "go")
            {
                ReadPlanWindow(ref reader, ref acc);
            }
            else
            {
                reader.Skip();
            }
        }
    }

    /// <summary>planSamples.go 里还有 fiveHour / weekly / monthly 三条时间序列。</summary>
    private static void ReadPlanWindow(ref Utf8JsonReader reader, ref Acc acc)
    {
        int depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                return;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string name = reader.GetString() ?? "";
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                switch (name)
                {
                    case "fiveHour":
                        acc.FiveHour = LastPercent(ref reader);
                        break;
                    case "weekly":
                        acc.Weekly = LastPercent(ref reader);
                        break;
                    case "monthly":
                        acc.Monthly = LastPercent(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            else if (reader.TokenType is JsonTokenType.StartObject)
            {
                reader.Skip();
            }
        }
    }

    /// <summary>序列里最后一条的 p（已用百分比）。400 条只留最后一条，中途不建对象。</summary>
    private static int? LastPercent(ref Utf8JsonReader reader)
    {
        int depth = reader.CurrentDepth;
        int? last = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == depth)
            {
                return last;
            }
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                last = ReadPercent(ref reader) ?? last;
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
        return last;
    }

    private static int? ReadPercent(ref Utf8JsonReader reader)
    {
        int depth = reader.CurrentDepth;
        int? percent = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                return percent;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string name = reader.GetString() ?? "";
            if (!reader.Read())
            {
                return percent;
            }

            if (name == "p")
            {
                percent = (int)ReadLong(ref reader);
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
        return percent;
    }

    private static long ReadLong(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out long l))
            {
                return l;
            }
            if (reader.TryGetDouble(out double d))
            {
                return (long)d;
            }
        }
        return 0;
    }

    private static double ReadDouble(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetDouble(out double d))
            {
                return d;
            }
            if (reader.TryGetInt64(out long l))
            {
                return l;
            }
        }
        return 0;
    }
}
