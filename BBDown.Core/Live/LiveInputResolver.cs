using System;

namespace BBDown.Core.Live;

/// <summary>
/// 直播间定位符。<see cref="RoomId"/> 可能是短号，需经 room_init 换取真实房间号。
/// </summary>
public readonly record struct LiveTarget(string RoomId);

public static class LiveInputResolver
{
    private const string LiveHost = "live.bilibili.com";
    private const string MobileLiveHost = "m.live.bilibili.com";

    /// <summary>
    /// 把用户输入归一化为直播间定位符。仅识别直播间地址，形如：
    /// <list type="bullet">
    ///   <item>https://live.bilibili.com/123456（支持 // 协议相对、m. 前缀、带 query/fragment）</item>
    ///   <item>https://live.bilibili.com/h5/123456、/blanc/123456、/blackboard/123456</item>
    ///   <item>live123456</item>
    /// </list>
    /// 不接受裸数字：根命令下裸数字属于 <see cref="IdPrefix.EpColon"/> 链路。
    /// </summary>
    public static bool TryParse(string input, out LiveTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim( );

        if (s.Length > IdPrefix.Live.Length
            && s.StartsWith(IdPrefix.Live, StringComparison.OrdinalIgnoreCase)
            && char.IsAsciiDigit(s[IdPrefix.Live.Length]))
        {
            return TryTakeRoomId(s[IdPrefix.Live.Length..], out target);
        }

        if (s.StartsWith("//", StringComparison.Ordinal))
        {
            s = "https:" + s;
        }
        else if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // 允许省略协议的裸域名形态：live.bilibili.com/123456
            if (!s.StartsWith(LiveHost + "/", StringComparison.OrdinalIgnoreCase)
                && !s.StartsWith(MobileLiveHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            s = "https://" + s;
        }

        // 必须精确比对 Host。用 Contains 会让 evil.com/?x=live.bilibili.com/1 蒙混过关
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, LiveHost, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, MobileLiveHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');
        foreach (var prefix in (ReadOnlySpan<string>) ["h5/", "blanc/", "blackboard/"])
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[prefix.Length..];
                break;
            }
        }

        // 只取第一段，容忍 live.bilibili.com/123456/ 之类的尾随路径
        var slash = path.IndexOf('/');
        if (slash >= 0)
        {
            path = path[..slash];
        }

        return TryTakeRoomId(path, out target);
    }

    private static bool TryTakeRoomId(string s, out LiveTarget target)
    {
        target = default;
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        // 房间号 0 不存在，B 站以此作为「无短号」的占位值
        if (s.TrimStart('0').Length == 0)
        {
            return false;
        }

        target = new LiveTarget(s);
        return true;
    }
}
