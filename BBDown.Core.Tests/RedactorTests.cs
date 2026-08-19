using System.Collections.Generic;

using BBDown.Core.Util;

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
}
