using System.Collections.Generic;


namespace BBDown.Core.Entity;

public class VInfo
{
    /// <summary>
    /// 视频标题
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// 视频描述
    /// </summary>
    public required string Desc { get; set; }

    /// <summary>
    /// 视频封面
    /// </summary>
    public required string Pic { get; set; }

    /// <summary>
    /// 视频发布时间
    /// </summary>
    public required long PubTime { get; set; }
    // 仅用于 UI 展示（如文件名变量、打印分 P）。
    // 播放地址解析阶段（Parser.PlayUrlRequest）以内部 id 前缀为准重新派生 IsBangumi/IsCheese（见 Parser.cs），
    // 该派生值才是 playurl 分支的权威来源，不要反向依赖此处字段做解析分支。
    public bool IsBangumi { get; set; }
    public bool IsCheese { get; set; }

    /// <summary>
    /// 番剧是否完结
    /// </summary>
    public bool IsBangumiEnd { get; set; }

    /// <summary>
    /// 视频index 用于番剧或课程判断当前选择的是第几集
    /// </summary>
    public string? Index { get; set; }

    /// <summary>
    /// 视频分P信息
    /// </summary>
    public required List<Page> PagesInfo { get; init; }

    /// <summary>
    /// 是否为互动视频
    /// </summary>
    public bool IsSteinGate { get; set; }
}
