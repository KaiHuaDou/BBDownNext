namespace BBDown.DRM;

/// <summary>
/// 主程序落盘的请求协议（PascalCase 与主仓库 BBDown.Core.Download.PostProcessRequest 对齐）。
/// 只含轨道定位与本地路径，不含任何加密特征与凭据——本插件自行获取。
/// </summary>
internal sealed record PostProcessRequest(
    string Aid,
    string Cid,
    string Kind,
    string TrackPath,
    string DestPath,
    string Ffmpeg);
