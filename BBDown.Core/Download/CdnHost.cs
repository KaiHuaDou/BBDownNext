using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Download;

public static partial class CdnHost
{
    private const string BACKUP_HOST = "upos-sz-mirrorcoso1.bilivideo.com";

    /// <summary>
    /// 按优先级处理下载域名替换：
    /// 1. --upos-host 显式指定，无条件替换并结束；
    /// 2. PCDN 域名规避，除非 --allow-pcdn；
    /// 3. 海外 akamaized 源规避（仅在指定 --area 时）；
    /// 4. 默认强制替换为备用 host，除非 --no-force-host。
    /// </summary>
    internal static void Apply(DownloadRequest myOption, Video? selectedVideo, Audio? selectedAudio, AppConfig cfg)
    {
        if (selectedVideo != null)
        {
            selectedVideo.BaseUrl = ApplyCdnHostPolicy(selectedVideo.BaseUrl, myOption, cfg, "视频流");
        }

        if (selectedAudio != null)
        {
            selectedAudio.BaseUrl = ApplyCdnHostPolicy(selectedAudio.BaseUrl, myOption, cfg, "音频流");
        }
    }

    // FLV 走分段直链，同样需要按 upos-host / PCDN / 海外源策略换域名（P1-21）
    internal static void Apply(DownloadRequest myOption, List<string> clips, AppConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(clips);
        for (var i = 0; i < clips.Count; i++)
        {
            clips[i] = ApplyCdnHostPolicy(clips[i], myOption, cfg, i == 0 ? "FLV 分段" : null);
        }
    }

    /// <summary>
    /// 按优先级替换下载域名。<paramref name="label"/> 为 null 时静默处理（批量分段只提示一次）。
    /// </summary>
    private static string ApplyCdnHostPolicy(string url, DownloadRequest myOption, AppConfig cfg, string? label)
    {
        // 1. --upos-host 显式指定：无条件替换并结束
        if (myOption.UposHost is { Length: > 0 })
        {
            return Replace(UposRegex( ), myOption.UposHost, "强制替换为用户指定服务器");
        }

        // 2. PCDN 域名规避，除非 --allow-pcdn
        if (!myOption.AllowPcdn)
        {
            url = Replace(PcdnRegex( ), BACKUP_HOST, "检测到 PCDN，替换");
        }

        // 3. 海外 akamaized 源规避（仅在指定 --area 时）
        if (cfg.Area is { Length: > 0 })
        {
            url = Replace(AkamRegex( ), BACKUP_HOST, "检测到海外源，替换");
        }

        // 4. 默认强制替换为备用 host，除非 --no-force-host。
        //    但若 --allow-pcdn 且当前 URL 仍是 PCDN 域名（带显式端口），则不再强行覆盖，
        //    否则 --allow-pcdn 必须在 --no-force-host 同时存在时才生效，等于死选项（P0-7）
        if (!myOption.NoForceHost && !(myOption.AllowPcdn && PcdnRegex( ).IsMatch(url)))
        {
            url = Replace(UposRegex( ), BACKUP_HOST, "默认强制替换");
        }

        return url;

        string Replace(Regex pattern, string host, string reason)
        {
            if (!pattern.IsMatch(url))
            {
                return url;
            }

            if (label is not null)
            {
                LogWarn($"{label}：{reason}为 {host}……");
            }

            return pattern.Replace(url, $"://{host}/", 1);
        }
    }

    [GeneratedRegex("://[^/]*akamaized\\.net/")]
    private static partial Regex AkamRegex( );

    [GeneratedRegex("://[^/]+/")]
    private static partial Regex UposRegex( );
}
