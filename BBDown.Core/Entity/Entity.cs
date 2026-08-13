#pragma warning disable CA2227 // 排序回写/分 P 拷贝/解析替换需整体替换集合，保留 setter

using System;
using System.Collections.Generic;

using BBDown.Core.Util;

namespace BBDown.Core.Entity;

public class Page
{
    public required int Index { get; set; }
    public required string Aid { get; set; }
    public required string Cid { get; set; }
    public required string EpId { get; set; }
    public required string Title { get; set; }
    public required int Dur { get; set; }
    public required string Res { get; set; }
    public required long PubTime { get; set; }
    public string? Cover { get; set; }
    public string? Desc { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerMid { get; set; }
    // 番剧/课程等场景 aid 可能为空或非数字, 此时没有对应 BV 号, 不应连累文件名模板与元数据写入
    public string Bvid => long.TryParse(Aid, out var avid) && avid > 0 ? BilibiliBvConverter.Encode(avid) : "";
    // CA1002: 保持 List<T>，调用方（BBDown 主项目）会对该集合执行 Add/整体替换
    public List<ViewPoint> Points { get; set; } = [];

    // 沿用原拷贝构造语义：Desc/Points 不随源 Page 复制
    public Page CopyWith(int index)
    {
        return new( )
        {
            Index = index,
            Aid = Aid,
            Cid = Cid,
            EpId = EpId,
            Title = Title,
            Dur = Dur,
            Res = Res,
            PubTime = PubTime,
            Cover = Cover,
            OwnerName = OwnerName,
            OwnerMid = OwnerMid,
        };
    }

    // 等值仅看 Aid/Cid/EpId（跨列表去重用），勿改为 record 全成员等值
    public override bool Equals(object? obj)
    {
        return obj is Page page &&
               Aid == page.Aid &&
               Cid == page.Cid &&
               EpId == page.EpId;
    }

    public override int GetHashCode( )
    {
        return HashCode.Combine(Aid, Cid, EpId);
    }
}

public record ViewPoint
{
    public required string Title { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
}

public class Video
{
    public required string Id { get; set; }
    public required string Dfn { get; set; }
    public required string BaseUrl { get; set; }
    public string? Res { get; set; }
    public string? Fps { get; set; }
    public required string Codecs { get; set; }
    public long Bandwidth { get; set; }
    public int Dur { get; set; }
    public double Size { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is Video video &&
               Id == video.Id &&
               Dfn == video.Dfn &&
               Res == video.Res &&
               Fps == video.Fps &&
               Codecs == video.Codecs &&
               Bandwidth == video.Bandwidth &&
               Dur == video.Dur;
    }

    public override int GetHashCode( )
    {
        return HashCode.Combine(Id, Dfn, Res, Fps, Codecs, Bandwidth, Dur);
    }
}

public class Audio
{
    public required string Id { get; set; }
    public required string Dfn { get; set; }
    public required string BaseUrl { get; set; }
    public required string Codecs { get; set; }
    public required long Bandwidth { get; set; }
    public required int Dur { get; set; }

    // E-AC-3 => EAC3
    public string ShortCodecs => Codecs.ToUpper( ).Replace("-", string.Empty);

    public override bool Equals(object? obj)
    {
        return obj is Audio audio &&
               Id == audio.Id &&
               Dfn == audio.Dfn &&
               Codecs == audio.Codecs &&
               Bandwidth == audio.Bandwidth &&
               Dur == audio.Dur;
    }

    public override int GetHashCode( )
    {
        return HashCode.Combine(Id, Dfn, Codecs, Bandwidth, Dur);
    }
}

public record Subtitle
{
    public required string Lan { get; set; }
    public required string Url { get; set; }
    public required string Path { get; set; }
}

public record Clip
{
    public required int Index { get; init; }
    public required long From { get; init; }
    public required long To { get; init; }
}

public record AudioMaterial
{
    public required string Title { get; init; }
    public required string PersonName { get; init; }
    public required string Path { get; init; }
}

public record AudioMaterialInfo
{
    public required string Title { get; set; }
    public required string PersonName { get; set; }
    public required string Path { get; set; }
    // CA1002: 保持 List<T>，调用方（BBDown 主项目）会整体替换该集合（排序后回写）
    public required List<Audio> Audio { get; set; }
}
