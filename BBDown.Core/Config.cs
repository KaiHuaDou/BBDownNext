using System;
using System.Collections.Frozen;
using System.Linq;

namespace BBDown.Core;

public static class Config
{
    //日志级别（进程级 ambient，仅由 SetDebugLog 在启动时设置一次；日志调用遍及全局，不适合逐层透传）
    public static bool DebugLog { get; private set; }

    // 杜比视界清晰度标识，下载时需据此判断 ffmpeg 版本是否支持并切换 mp4box 封装
    public const string DolbyVisionQn = "126";

    // 智能修复（AI 超分）档位。仅番剧/课程下发，需登录且大会员
    public const string AiRepairQn = "100";

    // playurl 能力位掩码：16 dash|64 HDR|128 4K|256 杜比音|512 杜比视界|1024 8K|2048 AV1
    public const int Fnval = 4048;

    // 8192 位 = 智能修复。仅 PGC(/pgc/、/pugv/) 端点接受；
    // UGC 的 /x/player/wbi/playurl 带上该位会直接返回 -400，故必须按端点分发
    public const int FnvalPgc = Fnval | 8192; // 12240

    // 按画质从高到低排列，MaxQn 依赖该顺序；B 站新增档位时按档位插入正确位置
    private static readonly (string Qn, string Name)[] Qualities =
    [
        ("127", "8K 超高清"),
        ("126", "杜比视界"),
        ("125", "HDR 真彩"),
        ("120", "4K 超清"),
        ("116", "1080P 高帧率"),
        ("112", "1080P 高码率"),
        ("80", "1080P 高清"),
        // 智能修复是 AI 超分产物，源分辨率常低于原生 1080P，但 qn 数值(100)大于 80。
        // 默认不抢占原生 1080P，需要时以 -q "智能修复" 显式指定
        ("100", "智能修复"),
        ("74", "720P 高帧率"),
        ("64", "720P 高清"),
        ("48", "720P 高清"),
        ("32", "480P 清晰"),
        ("16", "360P 流畅"),
        ("6", "240P 流畅"),
        ("5", "144P 流畅"),
    ];

    private static readonly FrozenDictionary<string, string> QualityNames =
        Qualities.ToFrozenDictionary(q => q.Qn, q => q.Name);

    // Qualities 的 qn 顺序缓存为数组，供 QualityRank 在每次轨道排序比较时 O(1) 查下标，
    // 避免对每对比较都重新投影一次 Qualities
    private static readonly string[] QualityOrder = [.. Qualities.Select(q => q.Qn)];

    public static string MaxQn => Qualities[0].Qn;

    // B 站随时可能下发未收录的 qn，回落到原始值而不是抛 KeyNotFoundException
    public static string GetQualityName(string qn)
    {
        return QualityNames.TryGetValue(qn, out var name) ? name : $"未知清晰度(qn={qn})";
    }

    // 轨道排序权重（越小越优先），以 Qualities 的排列为准；取代原先隐式的 qn 数值降序。
    // 未收录的新档位按 qn 数值算插入位，与同位次的已知档位并列（再由码率决胜），
    // 不会被一律甩到末尾——B 站新增档位时不至于被当成最低画质
    public static int QualityRank(string qn)
    {
        var index = Array.IndexOf(QualityOrder, qn);
        if (index >= 0)
        {
            return index;
        }

        return int.TryParse(qn, out var value)
            ? Qualities.Count(q => int.Parse(q.Qn) > value)
            : Qualities.Length;
    }

    //设置一次日志级别（仅允许在启动装配阶段调用，避免任意代码点改动全局状态）
    public static void SetDebugLog(bool on)
    {
        DebugLog = on;
    }
}
