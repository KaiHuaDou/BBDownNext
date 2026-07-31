using System.Collections.Generic;

namespace BBDown.Core;

public static class Config
{
    //日志级别（进程级 ambient，仅由 SetDebugLog 在启动时设置一次；日志调用遍及全局，不适合逐层透传）
    public static bool DebugLog { get; private set; }

    //质量对照表（纯只读，从不修改）
    public static readonly Dictionary<string, string> qualitys = new( ) {
        {"127","8K 超高清" }, {"126","杜比视界" }, {"125","HDR 真彩" }, {"120","4K 超清" }, {"116","1080P 高帧率" },
        {"112","1080P 高码率" }, {"100","智能修复" }, {"80","1080P 高清" }, {"74","720P 高帧率" },
        {"64","720P 高清" }, {"48","720P 高清" }, {"32","480P 清晰" }, {"16","360P 流畅" },
        {"5","144P 流畅" }, {"6","240P 流畅" }
    };

    //设置一次日志级别（仅允许在启动装配阶段调用，避免任意代码点改动全局状态）
    public static void SetDebugLog(bool on)
    {
        DebugLog = on;
    }
}
