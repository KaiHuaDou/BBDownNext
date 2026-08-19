using Microsoft.AspNetCore.Authentication;

namespace BBDown.Serve.Http;

/// <summary>
/// serve 令牌认证选项：ExpectedToken 由 SetUpServer 在最终鉴权判定后注入。
/// </summary>
internal sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";

    public string? ExpectedToken { get; set; }
}
