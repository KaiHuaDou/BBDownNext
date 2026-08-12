using BBDown.Core.Entity;

namespace BBDown.Core.Download;

/// <summary>
/// <see cref="BBDown.Core.Pipeline.VideoInfo.FetchAsync"/> 解析出的「跑中才得到」的结果：视频信息、运行配置、aid、api 类型。
/// 由调用方（<see cref="BBDown.Core.Pipeline.PageQueue.RunAsync"/>）与 <see cref="RunConfig"/> 组装进 <see cref="WorkContext"/>，不作为上下文字段回填。
/// </summary>
public sealed record FetchResult(
    VInfo VInfo,
    AppConfig Cfg,
    ResourceId FetchedId,
    ApiType ApiType);
