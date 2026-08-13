namespace BBDown.GUI;

public enum LoginChannel { Web, Tv, App }

/// <summary>登录结果：通道 + 凭据；WEB 的 refresh_token 可能为空，TV/APP 恒为 null。</summary>
public sealed record LoginResult(LoginChannel Channel, string Credential, string? RefreshToken);
