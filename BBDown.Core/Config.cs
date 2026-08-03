using System.Collections.Frozen;

namespace BBDown.Core;

public static class Config
{
    //日志级别（进程级 ambient，仅由 SetDebugLog 在启动时设置一次；日志调用遍及全局，不适合逐层透传）
    public static bool DebugLog { get; private set; }

    // 杜比视界清晰度标识，下载时需据此判断 ffmpeg 版本是否支持并切换 mp4box 封装
    public const string DolbyVisionQn = "126";

    // 按画质从高到低排列，MaxQn 依赖该顺序；B 站新增档位时按档位插入正确位置
    private static readonly (string Qn, string Name)[] Qualities =
    [
        ("127", "8K 超高清"),
        ("126", "杜比视界"),
        ("125", "HDR 真彩"),
        ("120", "4K 超清"),
        ("116", "1080P 高帧率"),
        ("112", "1080P 高码率"),
        ("100", "智能修复"),
        ("80", "1080P 高清"),
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

    public static string MaxQn => Qualities[0].Qn;

    // B 站随时可能下发未收录的 qn，回落到原始值而不是抛 KeyNotFoundException
    public static string GetQualityName(string qn)
    {
        return QualityNames.TryGetValue(qn, out var name) ? name : $"未知清晰度(qn={qn})";
    }

    //设置一次日志级别（仅允许在启动装配阶段调用，避免任意代码点改动全局状态）
    public static void SetDebugLog(bool on)
    {
        DebugLog = on;
    }
}
