using System;
using System.Linq;

namespace BBDown.Core.Opus;

/// <summary>
/// 专栏定位符。OpusId 与 CvId 是同一篇文章的两个 ID：opus id 是动态 ID（雪花 ID，约 18-19 位），
/// cv id 是专栏 ID（目前通常 7-9 位）。二者至少有一个非空。
/// </summary>
public readonly record struct OpusTarget(string OpusId, string CvId)
{
    public bool HasCv => !string.IsNullOrEmpty(CvId);
    public bool HasOpus => !string.IsNullOrEmpty(OpusId);
}

public static class OpusInputResolver
{
    /// <summary>
    /// 把用户输入归一化为专栏地址。识别以下形态：
    /// <list type="bullet">
    ///   <item>https://www.bilibili.com/opus/123... （支持 // 协议相对、m.、带 query）</item>
    ///   <item>https://www.bilibili.com/read/cv12345 或 /read/mobile/12345</item>
    ///   <item>cv12345 / CV12345</item>
    ///   <item>opus123... / opus:123...</item>
    /// </list>
    /// </summary>
    /// <param name="allowBareId">
    /// 是否允许把无前缀的纯数字当作 opus/cv id。子命令入口传 <c>true</c>；根命令自动识别传 <c>false</c>，
    /// 否则 <c>bbdown 12345</c>（av 号简写）会被误判为专栏。纯数字按长度分界：≥15 视为 opus id，否则视为 cv id。
    /// </param>
    public static bool TryParse(string input, out OpusTarget target, bool allowBareId = false)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim( );

        // URL 形态（含 // 协议相对与 http/https）
        if (s.StartsWith("//", StringComparison.Ordinal)
            || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var url = s.StartsWith("//", StringComparison.Ordinal) ? "https:" + s : s;
            var path = StripQueryAndFragment(url);

            var opusIdx = path.IndexOf("/opus/", StringComparison.OrdinalIgnoreCase);
            if (opusIdx >= 0 && TryTakeDigits(path[(opusIdx + 6)..], out var opusId))
            {
                target = new OpusTarget(opusId, "");
                return true;
            }

            var readIdx = path.IndexOf("/read/", StringComparison.OrdinalIgnoreCase);
            if (readIdx >= 0)
            {
                var rest = path[(readIdx + 6)..];
                if (rest.StartsWith("mobile/", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest[7..];
                }

                if (rest.StartsWith("cv", StringComparison.OrdinalIgnoreCase) && TryTakeDigits(rest[2..], out var cvFromPrefix))
                {
                    target = new OpusTarget("", cvFromPrefix);
                    return true;
                }

                if (TryTakeDigits(rest, out var cvBare))
                {
                    target = new OpusTarget("", cvBare);
                    return true;
                }
            }

            return false;
        }

        // opus: 前缀
        if (s.StartsWith("opus:", StringComparison.OrdinalIgnoreCase) && TryTakeDigits(s[5..], out var opusColon))
        {
            target = new OpusTarget(opusColon, "");
            return true;
        }

        // opus + 纯数字（如 opus123...）；opus 后须紧跟数字，避免误吞其它以 opus 开头的串
        if (s.StartsWith("opus", StringComparison.OrdinalIgnoreCase) && s.Length > 4 && char.IsDigit(s[4]) && TryTakeDigits(s[4..], out var opusBare))
        {
            target = new OpusTarget(opusBare, "");
            return true;
        }

        // cv + 纯数字
        if (s.StartsWith("cv", StringComparison.OrdinalIgnoreCase) && s.Length > 2 && char.IsDigit(s[2]) && TryTakeDigits(s[2..], out var cvId))
        {
            target = new OpusTarget("", cvId);
            return true;
        }

        // 裸数字（仅子命令入口允许）：≥15 位视为 opus 雪花 id，否则视为 cv id
        if (allowBareId && s.All(char.IsDigit))
        {
            target = s.Length >= 15 ? new OpusTarget(s, "") : new OpusTarget("", s);
            return true;
        }

        return false;
    }

    private static string StripQueryAndFragment(string url)
    {
        var q = url.IndexOf('?');
        var h = url.IndexOf('#');
        var end = url.Length;
        if (q >= 0 && q < end)
        {
            end = q;
        }

        if (h >= 0 && h < end)
        {
            end = h;
        }

        return url[..end];
    }

    private static bool TryTakeDigits(string s, out string digits)
    {
        digits = "";
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i]))
        {
            i++;
        }

        if (i == 0)
        {
            return false;
        }

        digits = s[..i];
        return true;
    }
}
