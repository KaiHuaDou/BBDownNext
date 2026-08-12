using System.Collections.Generic;

using static BBDown.Core.Download.DownloadUtil;
using BBDown.Core.Download;
using BBDown.Core.Entity;

namespace BBDown.Core.Download;

/// <summary>
/// 单个分 P 下载期间恒定的入参集合，DASH 与 FLV 两条分支共用。
/// 随分支推进而变化的 ParsedResult 与 selected 不放进来，仍单独传递。
/// </summary>
public sealed record DownloadSession(
    DownloadRequest Options,
    WorkContext Ctx,
    PageContext PageCtx,
    List<Subtitle> Subtitles,
    DownloadConfig Config,
    PipelineSink Sink);
