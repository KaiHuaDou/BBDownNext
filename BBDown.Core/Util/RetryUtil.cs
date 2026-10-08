#pragma warning disable CA1068

using System;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Util;

public static class RetryUtil
{
    // 退避上限与 LiveRecorder.Backoff 一致：重试次数较大时避免等待时间无界增长
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(16);

    // 每个下载项独立有界重试：失败只影响该项。退避 2/4/8… 秒（1<<k 节奏，上限 16 秒）
    // shouldRetry 返回 false（用户取消、充电试看）立即上抛，不做无谓退避；耗尽抛最后一次真实异常
    // 由调用方决定该项跳过还是判整 P 失败。delay 可注入以便单测（默认走真实 Task.Delay）
    public static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetry, string item, CancellationToken ct, Func<Exception, bool> shouldRetry, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        Exception? last = null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action( );
            }
            catch (Exception ex) when (shouldRetry(ex))
            {
                last = ex;
                LogError(ex.Message);
                if (attempt >= maxRetry)
                {
                    break;
                }

                var backoff = TimeSpan.FromSeconds(Math.Min(1 << (attempt + 1), MaxBackoff.TotalSeconds));
                LogWarn($"{item} 下载失败，{backoff.TotalSeconds:0} 秒后重试...");
                await delay(backoff, ct);
            }
        }

        throw last!;
    }

    public static async Task RetryAsync(Func<Task> action, int maxRetry, string item, CancellationToken ct, Func<Exception, bool> shouldRetry, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        await RetryAsync(async ( ) => { await action( ); return 0; }, maxRetry, item, ct, shouldRetry, delay);
    }
}
