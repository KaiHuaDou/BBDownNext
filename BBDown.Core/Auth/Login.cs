using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using QRCoder;

using static BBDown.Core.Logger;

namespace BBDown.Core.Auth;

public static partial class Login
{
    public enum QrState { Expired, WaitingScan, WaitingConfirm, Success }

    // code 字段在 Web 与 TV 两套接口间类型不一致（整数/字符串），ToString 对两种 ValueKind 都能取到原文
    private static int ReadCode(JsonElement element)
    {
        return int.Parse(element.ToString( ));
    }

    private static string ReadMessage(JsonElement element)
    {
        return element.TryGetProperty("message", out var m) ? (m.GetString( ) ?? "") : "";
    }

    // 日志中只展示凭据首尾，避免明文泄露（P0-3）
    private static string MaskSecret(string? s)
    {
        return string.IsNullOrEmpty(s) || s.Length <= 8 ? "***" : $"{s[..4]}****{s[^4..]}";
    }

    /// <summary>
    /// 接口失败时响应里没有 data，直接 GetProperty 只会抛出无消息的 KeyNotFoundException，
    /// AOT 下 UseSystemResourceKeys 会把它显示成 Arg_KeyNotFound，掩盖真正的错误码。
    /// </summary>
    private static JsonElement ReadData(JsonElement root, string what)
    {
        var code = ReadCode(root.GetProperty("code"));
        return code == 0 && root.TryGetProperty("data", out var data)
            ? data
            : throw new InvalidOperationException($"{what}：{code} {ReadMessage(root)}");
    }

    /// <summary>
    /// 扫码登录的通用编排参数：生成二维码、轮询状态、解释状态、落盘凭据。
    /// Web 与 TV 仅这些环节不同，轮询循环本身完全一致。
    /// </summary>
    private record QrLoginPlan(
        Func<CancellationToken, Task<(string Url, string Key)>> Generate,
        Func<string, CancellationToken, Task<JsonElement>> Poll,
        Func<JsonElement, (QrState State, string? Data)> Interpret,
        Func<string, CancellationToken, Task> Persist,
        string ExpiredText);

    // 状态轮询：1 秒间隔；单次失败（网络抖动）重试至多 3 次自愈，超限才抛；成功返回 true，过期返回 false
    private static async Task<bool> RunQrLoginAsync(QrLoginPlan plan, string qrPath, CancellationToken token)
    {
        var (url, key) = await plan.Generate(token);
        await ShowQrCodeAsync(url, qrPath);
        var confirmed = false;
        var failures = 0;
        while (true)
        {
            await Task.Delay(1000, token);
            JsonElement root;
            try
            {
                root = await plan.Poll(key, token);
                failures = 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (++failures <= 3)
            {
                LogWarn($"状态轮询失败，{failures} 次重试中：{e.Message}");
                continue;
            }

            var (state, data) = plan.Interpret(root);
            switch (state)
            {
                case QrState.Expired:
                    LogColor(plan.ExpiredText);
                    return false;
                case QrState.WaitingScan:
                    break;
                case QrState.WaitingConfirm:
                    if (!confirmed)
                    {
                        Log("扫码成功，请确认...");
                        confirmed = true;
                    }

                    break;
                case QrState.Success:
                    if (data is not null)
                    {
                        await plan.Persist(data, token);
                    }

                    return true;
            }
        }
    }

    private static async Task ShowQrCodeAsync(string url, string qrPath)
    {
        Log("生成二维码...");
        using QRCodeGenerator qrGenerator = new( );
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using PngByteQRCode pngByteCode = new(qrCodeData);
        await File.WriteAllBytesAsync(qrPath, pngByteCode.GetGraphic(7));
        Log("生成二维码成功，请打开并扫描，或扫描打印的二维码。");
        using var ascii = new AsciiQRCode(qrCodeData);
        Console.WriteLine(ascii.GetGraphic(1, "█", " ", false));
    }

    // 清理二维码文件；删除失败（文件被占用等）不应掩盖主流程异常
    private static void DeleteQrCode(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            LogDebug("清理二维码失败（可忽略）：{0}", e.Message);
        }
    }
}
