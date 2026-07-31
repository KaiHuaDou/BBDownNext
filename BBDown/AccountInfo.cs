namespace BBDown;

/// <summary>
/// 账号登录态信息，由 nav 接口解析得到。
/// </summary>
internal readonly record struct AccountInfo(bool IsLogin, string UserName, int Level, bool IsVip, string VipLabel);
