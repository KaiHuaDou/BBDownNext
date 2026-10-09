using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core;

/// <summary>
/// buvid3/buvid4/b_nut 设备标识的懒加载缓存。B 站风控（code -352）会核查这些 Cookie，缺失时下载更易被限流
/// 值由首次 <see cref="InitAsync"/> 从 /x/frontend/finger/spi 拉取；失败则留空，行为与改造前一致（不附加设备标识）
/// </summary>
public static class Buvid
{
    // 写者只有 InitCoreAsync（拉取完成后），读者是任意线程（BiliHeaders 拼 Cookie、AppHelper 取 buvid 参数）
    // 三个字段都经 Volatile 发布：写在锁外而 InitAsync 的锁不构成跨线程屏障
    private static string fragment = "";
    private static string value = "";
    private static int initFailed;

    public static string Fragment => Volatile.Read(ref fragment);

    public static string Value => Volatile.Read(ref value);

    private static Task? initTask;
    private static readonly Lock gate = new( );

    /// <summary>
    /// 缓存初始化任务，保证只发起一次成功拉取；拉取失败则标记，下次调用可重试
    /// </summary>
    // 进程内共享的初始化任务不捕获调用方令牌：抢到锁者的 ct 一旦取消或超时，
    // 其余并发任务会一直 await 一个已死任务，故取消只在 await 处生效
    public static Task InitAsync(CancellationToken ct = default)
    {
        lock (gate)
        {
            if (initTask is null || Volatile.Read(ref initFailed) != 0)
            {
                Volatile.Write(ref initFailed, 0);
                initTask = InitCoreAsync( );
            }

            return initTask.WaitAsync(ct);
        }
    }

    private static async Task InitCoreAsync( )
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
                Volatile.Write(ref value, buvid3);
                Volatile.Write(ref fragment, $"buvid3={buvid3};buvid4={buvid4};b_nut={bNut}");
                LogDebug("buvid 已生成");
            }
            else
            {
                Volatile.Write(ref initFailed, 1);
                LogDebug("获取 buvid 失败：返回结构缺少 b_3/b_4");
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref initFailed, 1);
            LogDebug("获取 buvid 失败（将不附加设备标识）: {0}", ex.Message);
        }
    }
}
