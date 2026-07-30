using BBDown.Core.Util;

namespace BBDown.Core.Entity;

public static class Entity
{
    public class Page
    {
        public required int index;
        public required string aid;
        public required string cid;
        public required string epid;
        public required string title;
        public required int dur;
        public required string res;
        public required long pubTime;
        public string? cover;
        public string? desc;
        public string? ownerName;
        public string? ownerMid;
        public string bvid => BilibiliBvConverter.Encode(long.Parse(aid));
        public List<ViewPoint> points = [];

        // 沿用原拷贝构造语义：desc/points 不随源 Page 复制
        public Page CopyWith(int index) => new( )
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

    public class ViewPoint
    {
        public required string title;
        public required int start;
        public required int end;
    }

    public class Video
    {
        public required string id;
        public required string dfn;
        public required string baseUrl;
        public string? res;
        public string? fps;
        public required string codecs;
        public long bandwith;
        public int dur;
        public double size;

        public override bool Equals(object? obj)
        {
            return obj is Video video &&
                   id == video.id &&
                   dfn == video.dfn &&
                   res == video.res &&
                   fps == video.fps &&
                   codecs == video.codecs &&
                   bandwith == video.bandwith &&
                   dur == video.dur;
        }

        public override int GetHashCode( )
        {
            return HashCode.Combine(id, dfn, res, fps, codecs, bandwith, dur);
        }
    }

    public class Audio
    {
        public required string id;
        public required string dfn;
        public required string baseUrl;
        public required string codecs;
        public required long bandwith;
        public required int dur;

        // E-AC-3 => EAC3
        public string shortCodecs => codecs.ToUpper( ).Replace("-", string.Empty);

        public override bool Equals(object? obj)
        {
            return obj is Audio audio &&
                   id == audio.id &&
                   dfn == audio.dfn &&
                   codecs == audio.codecs &&
                   bandwith == audio.bandwith &&
                   dur == audio.dur;
        }

        public override int GetHashCode( )
        {
            return HashCode.Combine(id, dfn, codecs, bandwith, dur);
        }
    }

    public class Subtitle
    {
        public required string lan;
        public required string url;
        public required string path;
    }

    public class Clip
    {
        public required int index;
        public required long from;
        public required long to;
    }

    public class AudioMaterial
    {
        public required string title;
        public required string personName;
        public required string path;
    }

    public class AudioMaterialInfo
    {
        public required string title;
        public required string personName;
        public required string path;
        public required List<Audio> audio;
    }
}