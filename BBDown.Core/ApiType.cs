namespace BBDown.Core;

/// <summary>API 解析通道。单值选择，取代 WEB / TV / APP / INTL 的多布尔开关。</summary>
public enum ApiType
{
    Web = 0,
    Tv = 1,
    App = 2,
    Intl = 3,
}

/// <summary>API 通道名解析，CLI 与 serve 共用，忽略大小写。</summary>
public static class ApiTypeUtil
{
    /// <summary>解析 API 通道名，忽略大小写；未知值返回 null，由调用方决定报错或回落。</summary>
    public static ApiType? TryParse(string? value)
    {
        return value?.Trim( ).ToUpperInvariant( ) switch
        {
            "WEB" => ApiType.Web,
            "TV" => ApiType.Tv,
            "APP" => ApiType.App,
            "INTL" => ApiType.Intl,
            _ => null,
        };
    }
}
