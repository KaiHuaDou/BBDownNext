using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BBDown.Serve.Http;

/// <summary>
/// 令牌认证：X-BBDown-Token 头始终接受；WebSocket 握手（/hubs/tasks，浏览器无法自定义请求头）例外接受
/// ?token= 查询参数。比较走恒定时间，避免时序侧信道。
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync( )
    {
        var token = ReadToken( );
        if (token is null || !TokenEquals(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("无效或缺失令牌"));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "serve-client")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    private string? ReadToken( )
    {
        if (Request.Headers.TryGetValue("X-BBDown-Token", out var headerToken))
        {
            return headerToken.ToString( );
        }

        // 仅握手路径例外接受 query 令牌：浏览器 WebSocket 无法自定义请求头
        return Request.Path.StartsWithSegments("/hubs/tasks")
            && Request.Query.TryGetValue("token", out var queryToken)
                ? queryToken.ToString( )
                : null;
    }

    private bool TokenEquals(string token)
    {
        var expected = Options.ExpectedToken;
        if (expected is null)
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(token);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return provided.Length == expectedBytes.Length
               && CryptographicOperations.FixedTimeEquals(provided, expectedBytes);
    }
}
