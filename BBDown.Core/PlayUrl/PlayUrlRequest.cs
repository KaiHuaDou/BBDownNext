namespace BBDown.Core.PlayUrl;

using static BBDown.Core.ResourceId;

// 收拢 playurl 请求参数，避免在解析各分支间逐层透传 9 个形参。
// 顶层类型后 Parser 与其 App 端实现不再需要 partial 共享私有嵌套类型。
internal readonly record struct PlayUrlRequest(
    ResourceId AidOri,
    string Aid,
    string Cid,
    string EpId,
    ApiType Api,
    string Encoding,
    AppConfig Cfg)
{
    public bool IsCheese => AidOri is CheeseEp or CheeseSeason;

    public bool IsEpisode => AidOri is Ep or Season;

    public bool IsBangumi => IsCheese || IsEpisode;
}
