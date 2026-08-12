using System.Collections.Generic;

using BBDown.Core;
using BBDown.Core.Entity;

namespace BBDown.Core.Download;

/// <summary>
/// 一次下载任务的不可变上下文快照。
/// 聚合 <see cref="BBDown.Core.Pipeline.WorkSetup.Build"/> 解析出的运行参数（<see cref="RunConfig"/>）
/// 与 <see cref="BBDown.Core.Pipeline.VideoInfo.FetchAsync"/> 解析出的视频信息（<see cref="FetchResult"/>），
/// 通过 record 的 with 表达式在阶段之间非破坏性传递，取代原先散落的元组/多参数。
/// <see cref="SavePathFormat"/> 由 <see cref="BBDown.Core.Pipeline.PageQueue.RunAsync"/> 在启动阶段一次性算出。
/// </summary>
public sealed record WorkContext(
    RunConfig Run,
    FetchResult Fetch,
    string SavePathFormat);
