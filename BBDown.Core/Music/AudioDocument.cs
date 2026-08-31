namespace BBDown.Core.Music;

/// <summary>音频投稿元信息（song/info 的解析结果，不含播放流地址与歌词文本）。</summary>
public sealed record AudioInfo(
    long AuId,
    string Title,
    string Author,
    string Cover,
    long Duration,
    long PublishTime);

/// <summary>音频播放流。Type 为 -1 表示试听片段（付费 / 大会员曲目未登录时）。</summary>
public sealed record AudioPlayUrl(string Url, int Type);
