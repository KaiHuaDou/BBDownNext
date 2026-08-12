using System.Collections.Generic;

using BBDown.Core.Drm;

namespace BBDown.Core.Download;

/// <summary>
/// 一次下载任务在「启动即可确定」的运行参数快照（不可变）。由 <see cref="BBDown.Core.Pipeline.WorkSetup.Build"/> 算清一次，
/// 不含任何「跑中才得到」的值（视频信息、aid、api 类型、保存路径模板）——那些由
/// <see cref="BBDown.Core.Pipeline.VideoInfo.FetchAsync"/> / <see cref="BBDown.Core.Pipeline.PageQueue.RunAsync"/> 作为返回值 / 局部变量回传，
/// 最终在 <see cref="BBDown.Core.Pipeline.PageQueue.RunAsync"/> 里一次性组装进 <see cref="WorkContext"/>，不再有空占位 + with 补全。
/// </summary>
public sealed record RunConfig(
    Dictionary<string, byte> EncodingPriority,
    Dictionary<string, int> DfnPriority,
    string FirstEncoding,
    bool EncodingFirst,
    DownloadContent Content,
    MuxMode Mux,
    IReadOnlyList<DanmakuFormat> DownloadDanmakuFormats,
    int CommentCount,
    bool CommentSortHot,
    IReadOnlyList<CommentFormat> CommentFormats,
    string Input,
    string Lang,
    int Delay,
    ToolPaths Tools,
    // --drm-key 条目在任务启动时解析一次，全任务共享；解析告警只打印一遍
    DrmKeySource DrmKeys,
    string WorkDir);
