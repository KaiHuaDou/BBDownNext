using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Download;

// 外部后处理进程的文件交换协议：请求 JSON 落盘 → 调起进程单次执行 → 产物文件即响应。
// 未配置 --post-process 时整个路径不启用：默认静默，原文件照常混流输出。
// 请求只携带轨道定位与本地路径，不携带任何加密特征与凭据——处理方自行获取所需信息。
// 处理程序路径随 DownloadRequest.PostProcessPath 按任务透传（取代进程级全局，
// 避免 GUI 并发任务互相覆盖配置）。
public static class PostProcessClient
{
    // 插件需完成 playurl 重抓（可能含 drm_tech_type=2 重试）、license 取钥与 ffmpeg 解密，20 秒在慢网络下不够
    private const int ProcessTimeoutMs = 60000;

    /// <summary>
    /// 通过请求文件调起外部进程处理已下载的轨道。任何失败（未配置 / 进程异常 / 超时 /
    /// 无产物）都返回 false，调用方据此静默保留原文件；仅当进程退出码为 0 且产物存在才视为成功。
    /// <paramref name="postProcessExe"/> 为空表示未配置后处理。
    /// </summary>
    public static async Task<bool> TryProcessAsync(string postProcessExe, string aid, string cid, string kind, string trackPath, string destPath, string ffmpeg, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(postProcessExe))
        {
            return false;
        }

        var requestPath = Path.Combine(Path.GetDirectoryName(trackPath) ?? ".", $"postprocess_{Guid.NewGuid( ):N}.json");
        try
        {
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new PostProcessRequest(aid, cid, kind, trackPath, destPath, ffmpeg), PostProcessJsonContext.Default.PostProcessRequest), ct);
            using var process = Process.Start(new ProcessStartInfo(postProcessExe, requestPath) { UseShellExecute = false })!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProcessTimeoutMs);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                process.Kill( );
                throw;
            }
            catch (OperationCanceledException)
            {
                LogWarn($"外部后处理超时，跳过：{trackPath}");
                process.Kill( );
                return false;
            }

            if (process.ExitCode != 0)
            {
                LogWarn($"外部后处理退出码 {process.ExitCode}，跳过：{trackPath}");
                return false;
            }

            return File.Exists(destPath) && new FileInfo(destPath).Length > 0;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogWarn($"外部后处理超时，跳过：{trackPath}");
            return false;
        }
        catch (Exception ex)
        {
            LogWarn($"外部后处理不可用，跳过：{ex.Message}");
            return false;
        }
        finally
        {
            File.Delete(requestPath);
        }
    }
}

/// <summary>后处理请求：轨道定位与本地路径，不含任何加密特征与凭据。</summary>
public sealed record PostProcessRequest(string Aid, string Cid, string Kind, string TrackPath, string DestPath, string Ffmpeg);

[JsonSerializable(typeof(PostProcessRequest))]
public partial class PostProcessJsonContext : JsonSerializerContext;
