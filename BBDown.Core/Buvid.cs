using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core;

/// <summary>
/// buvid3/buvid4/b_nut 设备标识的懒加载缓存。B 站风控（code -352）会核查这些 Cookie，缺失时下载更易被限流。
/// 值由首次 <see cref="InitAsync"/> 从 /x/frontend/finger/spi 拉取；失败则留空，行为与改造前一致（不附加设备标识）。
/// </summary>
public static class Buvid
{
    public static string Fragment { get; private set; } = "";

    public static string Value { get; private set; } = "";

    private static Task? initTask;
    private static bool initFailed;
    private static readonly Lock gate = new( );

    /// <summary>
    /// 缓存初始化任务，保证只发起一次成功拉取；若拉取失败则标记，下次调用可重试（修复 P1-1：原 Interlocked 方案在首次失败后会永久禁用）。
    /// </summary>
    public static Task InitAsync(CancellationToken ct = default)
    {
        lock (gate)
        {
            if (initTask is null || initFailed)
            {
                initFailed = false;
                initTask = InitCoreAsync(ct);
            }

            return initTask;
        }
    }

    private static async Task InitCoreAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var json = await GetWebSourceAsync(BiliApi.FingerSpi, AppConfig.Empty, null, cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("b_3", out var b3) && b3.ValueKind == JsonValueKind.String
                && data.TryGetProperty("b_4", out var b4) && b4.ValueKind == JsonValueKind.String)
            {
                var buvid3 = b3.GetString( )!;
                var buvid4 = b4.GetString( )!;
                var bNut = DateTimeOffset.Now.ToUnixTimeSeconds( ).ToString( );
                Value = buvid3;
                Fragment = $"buvid3={buvid3};buvid4={buvid4};b_nut={bNut}";
                LogDebug("buvid 已生成");
            }
            else
            {
                initFailed = true;
                LogDebug("获取 buvid 失败：返回结构缺少 b_3/b_4");
            }
        }
        catch (Exception ex)
        {
            initFailed = true;
            LogDebug("获取 buvid 失败（将不附加设备标识）: {0}", ex.Message);
        }
    }
}
