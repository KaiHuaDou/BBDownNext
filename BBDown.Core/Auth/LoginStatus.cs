namespace BBDown.Core.Auth;

/// <summary>
/// 单通道登录态。<see cref="Verified"/> 为 null 表示探测未完成（网络异常 / 响应不可解析）
/// 与「服务端明确否认」是两种状态，不可合并
/// </summary>
/// <param name="Channel">通道名：WEB / TV / APP</param>
/// <param name="Saved">本地是否持有该通道凭据</param>
/// <param name="Verified">服务端是否确认登录；null = 探测失败</param>
/// <param name="Account">账号信息，仅在 <paramref name="Verified"/> 为 true 时有意义</param>
/// <param name="IssueTs">凭据签发时间戳，未记录时为 null</param>
/// <param name="RefreshPending">服务端是否要求刷新 Cookie；仅 WEB 通道查询，其余通道恒 false</param>
public readonly record struct LoginStatus(
    string Channel,
    bool Saved,
    bool? Verified,
    AccountInfo Account,
    long? IssueTs,
    bool RefreshPending = false
);
