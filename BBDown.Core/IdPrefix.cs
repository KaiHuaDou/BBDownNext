namespace BBDown.Core;

/// <summary>
/// B 站各类 id 的统一前缀常量。集中管理以避免前缀字面量与切片长度（如 <c>id[7..]</c>）散落各处，
/// 改前缀时只改此处，切片自动跟随 <see cref="string.Length"/>，避免手工同步数字导致的越界或截断。
/// </summary>
public static class IdPrefix
{
    /// <summary>课程（cheese）前缀，切片长度 7</summary>
    public const string Cheese = "cheese:";

    /// <summary>课程简写链接前缀（cheese/ep123、cheese/ss123），长度 7</summary>
    public const string CheeseSlash = "cheese/";

    /// <summary>收藏夹前缀，切片长度 6</summary>
    public const string FavId = "favId:";

    /// <summary>单集/番剧前缀（带冒号），切片长度 3</summary>
    public const string EpColon = "ep:";

    /// <summary>合集（medialist）业务 id 前缀，切片长度 10</summary>
    public const string ListBizId = "listBizId:";

    /// <summary>系列业务 id 前缀，切片长度 12</summary>
    public const string SeriesBizId = "seriesBizId:";

    /// <summary>UP 主空间投稿列表前缀，切片长度 9</summary>
    public const string SpaceMid = "spaceMid:";

    /// <summary>稍后再看列表前缀，切片长度 11</summary>
    public const string WatchLater = "watchLater:";

    /// <summary>直播间前缀，切片长度 5</summary>
    public const string Live = "live:";

    /// <summary>BV 号前缀，切片长度 3</summary>
    public const string Bv = "BV1";

    /// <summary>av 号前缀，切片长度 2</summary>
    public const string Av = "av";

    /// <summary>简写 ep 前缀（不带冒号），切片长度 2</summary>
    public const string Ep = "ep";

    /// <summary>简写 ss 前缀，切片长度 2</summary>
    public const string Ss = "ss";

    /// <summary>简写 md 前缀，无切片</summary>
    public const string Md = "md";
}
