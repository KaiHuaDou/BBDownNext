namespace BBDown.Core;

/// <summary>
/// B 站接口地址集中表。只放地址常量，不承载任何请求逻辑。
/// 可被 --host / --ep-host / --tv-host 代理的接口在此只登记路径，主机由 <see cref="AppConfig"/> 提供。
/// </summary>
public static class BiliApi
{
    public const string MainHost = "api.bilibili.com";
    public const string PassportHost = "passport.bilibili.com";
    public const string TvHost = "api.snm0516.aisee.tv";
    public const string IntlAppHost = "api.bilibili.tv";
    public const string IntlWebHost = "api.biliintl.com";
    public const string LiveApiHost = "api.live.bilibili.com";

    // 主机可被代理，故只登记路径
    public const string PlayUrlWebPath = "/x/player/wbi/playurl";
    public const string PlayUrlPgcPath = "/pgc/player/web/v2/playurl";
    public const string PlayUrlTvPath = "/x/tv/playurl";
    public const string PlayUrlPgcTvPath = "/pgc/player/api/playurltv";
    public const string SeasonPgcPath = "/pgc/view/web/season";
    public const string IntlPlayUrlPath = "/intl/gateway/v2/ogv/playurl";
    public const string IntlSeasonAppPath = "/intl/gateway/v2/ogv/view/app/season";
    public const string IntlSubtitleWebPath = "/intl/gateway/web/v2/subtitle";

    // 主机固定的接口
    public const string View = $"https://{MainHost}/x/web-interface/view";
    public const string ViewWbi = $"https://{MainHost}/x/web-interface/wbi/view";
    public const string FingerSpi = $"https://{MainHost}/x/frontend/finger/spi";
    public const string Nav = $"https://{MainHost}/x/web-interface/nav";
    public const string PlayerWbiV2 = $"https://{MainHost}/x/player/wbi/v2";
    public const string PlayerSo = $"https://{MainHost}/x/player.so";
    public const string EdgeInfo = $"https://{MainHost}/x/stein/edgeinfo_v2";
    public const string SeasonPugv = $"https://{MainHost}/pugv/view/web/season";
    public const string ReviewUser = $"https://{MainHost}/pgc/review/user";
    public const string FavFolderList = $"https://{MainHost}/x/v3/fav/folder/created/list-all";
    public const string FavResourceList = $"https://{MainHost}/x/v3/fav/resource/list";
    public const string MediaListInfo = $"https://{MainHost}/x/v1/medialist/info";
    public const string MediaListResource = $"https://{MainHost}/x/v2/medialist/resource/list";
    public const string SpaceArcSearch = $"https://{MainHost}/x/space/wbi/arc/search";
    public const string OpusDetail = $"https://{MainHost}/x/polymer/web-dynamic/v1/opus/detail";
    public const string ArticleView = $"https://{MainHost}/x/article/view";
    public const string ReplyWbiMain = $"https://{MainHost}/x/v2/reply/wbi/main";
    public const string ReplyReply = $"https://{MainHost}/x/v2/reply/reply";
    public const string ToviewList = $"https://{MainHost}/x/v2/history/toview";

    // 直播（均无需 Cookie 与 WBI 签名）
    public const string LiveRoomInit = $"https://{LiveApiHost}/room/v1/Room/room_init";
    public const string LiveRoomBaseInfo = $"https://{LiveApiHost}/xlive/web-room/v1/index/getRoomBaseInfo";
    public const string LiveRoomPlayInfo = $"https://{LiveApiHost}/xlive/web-room/v2/index/getRoomPlayInfo";

    // grpc / protobuf 端点
    public const string GrpcPlayView = "https://grpc.biliapi.net/bilibili.app.playurl.v1.PlayURL/PlayView";
    public const string GrpcPgcPlayView = "https://app.bilibili.com/bilibili.pgc.gateway.player.v2.PlayURL/PlayView";
    public const string GrpcDmView = "https://app.biliapi.net/bilibili.community.service.dm.v1.DM/DmView";

    // 登录
    public const string QrCodeGenerate = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate";
    public const string QrCodePoll = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll";
    public const string TvQrCodeAuth = "https://passport.snm0516.aisee.tv/x/passport-tv-login/qrcode/auth_code";
    public const string TvQrCodePoll = "https://passport.bilibili.com/x/passport-tv-login/qrcode/poll";

    // 站点页面
    public const string Site = "https://www.bilibili.com";
    public const string VideoPage = $"{Site}/video";
    // 播放页主机随 --ep-host 走（镜像站提供同样的路径），故只登记路径
    public const string BangumiPlayPath = "/bangumi/play";
    public const string CheesePlayPath = "/cheese/play";
    public const string SpacePage = "https://space.bilibili.com";
    public const string AnimePage = "https://bangumi.bilibili.com/anime";
    public const string DanmakuXml = "https://comment.bilibili.com";
    public const string OpusPage = $"{Site}/opus";
    public const string ReadPage = $"{Site}/read";

    /// <summary>直播站点页。部分 CDN 节点校验 Referer，缺失会直接 403，拉流时必须带。</summary>
    public const string LiveSite = "https://live.bilibili.com";
}
