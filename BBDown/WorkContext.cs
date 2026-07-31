using System.Collections.Generic;

using BBDown.Core;
using BBDown.Core.Entity;

namespace BBDown;

/// <summary>
/// 一次下载任务的不可变上下文快照。
/// 聚合 SetUpWork 解析出的运行参数与 GetVideoInfoAsync 解析出的视频信息，
/// 通过 record 的 with 表达式在阶段之间非破坏性传递，取代原先散落的元组/多参数。
/// </summary>
internal sealed record WorkContext(
    Dictionary<string, byte> EncodingPriority,
    Dictionary<string, int> DfnPriority,
    string FirstEncoding,
    bool EncodingFirst,
    bool DownloadDanmaku,
    BBDownDanmakuFormat[] DownloadDanmakuFormats,
    string Input,
    string SavePathFormat,
    string Lang,
    int Delay,
    string FetchedAid,
    VInfo? VInfo,
    string ApiType,
    AppConfig Cfg,
    string WorkDir);
