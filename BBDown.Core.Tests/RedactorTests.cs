using System.Collections.Generic;

namespace BBDown.Core.Tests;

public class RedactorTests
{
    [Fact]
    public void Headers_MasksSecretKeysInDictionary( )
    {
        var headers = new Dictionary<string, string>
        {
            ["authorization"] = "identify_v1 secret-token",
            ["Host"] = "api.bilibili.com"
        };

        var redacted = Redactor.Headers(headers);

        Assert.Contains("authorization: [redacted]", redacted);
        Assert.Contains("Host: api.bilibili.com", redacted);
        Assert.DoesNotContain("secret-token", redacted);
    }

    // TV / APP 通道以 access_key 查询参数携带令牌，与 access_token 是两个键，漏掉即随 debug 日志落盘
    [Fact]
    public void Text_MasksAccessKeyInUrlQuery( )
    {
        var redacted = Redactor.Text("https://api.bilibili.com/x/v2/dm?oid=1&access_key=supersecret&platform=android");

        Assert.DoesNotContain("supersecret", redacted);
        Assert.Contains("access_key=[redacted]", redacted);
        Assert.Contains("oid=1", redacted);
    }

    // access_key 与 access_token 是不同凭据键，同一串文本里各自独立打码
    [Fact]
    public void Text_MasksAccessKeyAndAccessTokenIndependently( )
    {
        var redacted = Redactor.Text("?access_key=key-value&access_token=token-value&refresh_token=refresh-value");

        Assert.DoesNotContain("key-value", redacted);
        Assert.DoesNotContain("token-value", redacted);
        Assert.DoesNotContain("refresh-value", redacted);
    }

    // JSON 形态（响应体 / 落盘调试转储）同样要打码
    [Fact]
    public void Text_MasksSecretKeysInJsonBody( )
    {
        var redacted = Redactor.Text("""{"access_key":"ak-value","SESSDATA":"sess-value","csrf":"csrf-value"}""");

        Assert.DoesNotContain("ak-value", redacted);
        Assert.DoesNotContain("sess-value", redacted);
        Assert.DoesNotContain("csrf-value", redacted);
    }

    // 非凭据参数不受脱敏影响
    [Fact]
    public void Text_KeepsNonSecretParameters( )
    {
        const string url = "https://api.bilibili.com/x/player?cid=123&aid=456&fnval=4048";

        Assert.Equal(url, Redactor.Text(url));
    }
}
