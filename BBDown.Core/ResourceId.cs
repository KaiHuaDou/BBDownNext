namespace BBDown.Core;

/// <summary>
/// 各类资源 id 的统一类型，替代字符串形态的内部 id（如 "ep:ss2539"、favId:123:456）。
/// 值相等性天然支持去重键；每个子类型自行 override ToString 保持旧字符串格式，兼容日志输出与 serve 契约字段。
/// 注意：record 的合成 ToString 会遮蔽基类 override，故契约格式必须写在各子类型上。
/// </summary>
public abstract record ResourceId
{
    /// <summary>普通视频 av 号</summary>
    public sealed record Av(long Aid) : ResourceId
    {
        public override string ToString( )
        {
            return Aid.ToString( );
        }
    }

    /// <summary>单集番剧 ep_id</summary>
    public sealed record Ep(long EpId) : ResourceId
    {
        public override string ToString( )
        {
            return $"ep:{EpId}";
        }
    }

    /// <summary>整季番剧 season_id（原 "ep:ss{id}" 打标形态）</summary>
    public sealed record Season(long SeasonId) : ResourceId
    {
        public override string ToString( )
        {
            return $"ep:ss{SeasonId}";
        }
    }

    /// <summary>课程单集 ep_id</summary>
    public sealed record CheeseEp(long EpId) : ResourceId
    {
        public override string ToString( )
        {
            return $"cheese:{EpId}";
        }
    }

    /// <summary>整季课程 season_id（原 "cheese:ss{id}" 打标形态）</summary>
    public sealed record CheeseSeason(long SeasonId) : ResourceId
    {
        public override string ToString( )
        {
            return $"cheese:ss{SeasonId}";
        }
    }

    /// <summary>收藏夹（Fid 为 0 表示取默认收藏夹）</summary>
    public sealed record Fav(long Fid, long Mid) : ResourceId
    {
        public override string ToString( )
        {
            return $"favId:{Fid}:{Mid}";
        }
    }

    /// <summary>合集 biz_id</summary>
    public sealed record MediaList(long BizId) : ResourceId
    {
        public override string ToString( )
        {
            return $"listBizId:{BizId}";
        }
    }

    /// <summary>系列 biz_id</summary>
    public sealed record Series(long BizId) : ResourceId
    {
        public override string ToString( )
        {
            return $"seriesBizId:{BizId}";
        }
    }

    /// <summary>UP 主空间 mid</summary>
    public sealed record Space(long Mid) : ResourceId
    {
        public override string ToString( )
        {
            return $"spaceMid:{Mid}";
        }
    }

    /// <summary>稍后再看列表</summary>
    public sealed record WatchLater : ResourceId
    {
        public override string ToString( )
        {
            return "watchLater:";
        }
    }
}
