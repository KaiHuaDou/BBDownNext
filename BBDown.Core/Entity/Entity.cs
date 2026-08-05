using System;
using System.Collections.Generic;

using BBDown.Core.Util;

namespace BBDown.Core.Entity;

public static class Entity
{
    public class Page
    {
        public required int index { get; set; }
        public required string aid { get; set; }
        public required string cid { get; set; }
        public required string epid { get; set; }
        public required string title { get; set; }
        public required int dur { get; set; }
        public required string res { get; set; }
        public required long pubTime { get; set; }
        public string? cover { get; set; }
        public string? desc { get; set; }
        public string? ownerName { get; set; }
        public string? ownerMid { get; set; }
        // 番剧/课程等场景 aid 可能为空或非数字, 此时没有对应 BV 号, 不应连累文件名模板与元数据写入
        public string bvid => long.TryParse(aid, out var avid) && avid > 0 ? BilibiliBvConverter.Encode(avid) : "";
        // CA1002: 保持 List<T>，调用方（BBDown 主项目）会对该集合执行 Add/整体替换
        public List<ViewPoint> points { get; set; } = [];

        // 沿用原拷贝构造语义：desc/points 不随源 Page 复制
        public Page CopyWith(int index)
        {
            return new( )
            {
                index = index,
                aid = aid,
                cid = cid,
                epid = epid,
                title = title,
                dur = dur,
                res = res,
                pubTime = pubTime,
                cover = cover,
                ownerName = ownerName,
                ownerMid = ownerMid,
            };
        }

        // 等值仅看 aid/cid/epid（跨列表去重用），勿改为 record 全成员等值
        public override bool Equals(object? obj)
        {
            return obj is Page page &&
                   aid == page.aid &&
                   cid == page.cid &&
                   epid == page.epid;
        }

        public override int GetHashCode( )
        {
            return HashCode.Combine(aid, cid, epid);
        }
    }

    public record ViewPoint
    {
        public required string title { get; set; }
        public required int start { get; set; }
        public required int end { get; set; }
    }

    public class Video
    {
        public required string id { get; set; }
        public required string dfn { get; set; }
        public required string baseUrl { get; set; }
        public string? res { get; set; }
        public string? fps { get; set; }
        public required string codecs { get; set; }
        public long bandwidth { get; set; }
        public int dur { get; set; }
        public double size { get; set; }

        public override bool Equals(object? obj)
        {
            return obj is Video video &&
                   id == video.id &&
                   dfn == video.dfn &&
                   res == video.res &&
                   fps == video.fps &&
                   codecs == video.codecs &&
                   bandwidth == video.bandwidth &&
                   dur == video.dur;
        }

        public override int GetHashCode( )
        {
            return HashCode.Combine(id, dfn, res, fps, codecs, bandwidth, dur);
        }
    }

    public class Audio
    {
        public required string id { get; set; }
        public required string dfn { get; set; }
        public required string baseUrl { get; set; }
        public required string codecs { get; set; }
        public required long bandwidth { get; set; }
        public required int dur { get; set; }

        // E-AC-3 => EAC3
        public string shortCodecs => codecs.ToUpper( ).Replace("-", string.Empty);

        public override bool Equals(object? obj)
        {
            return obj is Audio audio &&
                   id == audio.id &&
                   dfn == audio.dfn &&
                   codecs == audio.codecs &&
                   bandwidth == audio.bandwidth &&
                   dur == audio.dur;
        }

        public override int GetHashCode( )
        {
            return HashCode.Combine(id, dfn, codecs, bandwidth, dur);
        }
    }

    public record Subtitle
    {
        public required string lan { get; set; }
        public required string url { get; set; }
        public required string path { get; set; }
    }

    public record Clip
    {
        public required int index { get; set; }
        public required long from { get; set; }
        public required long to { get; set; }
    }

    public record AudioMaterial
    {
        public required string title { get; set; }
        public required string personName { get; set; }
        public required string path { get; set; }
    }

    public record AudioMaterialInfo
    {
        public required string title { get; set; }
        public required string personName { get; set; }
        public required string path { get; set; }
        // CA1002: 保持 List<T>，调用方（BBDown 主项目）会整体替换该集合（排序后回写）
        public required List<Audio> audio { get; set; }
    }
}
