using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using BBDown.DRM;

// 用法：BBDown.DRM <请求JSON路径>
// 与主程序的文件交换协议：退出码 0 且 destPath 存在 = 成功；其余情况主程序静默保留原文件。
// 请求 JSON 由主程序以 PascalCase 序列化，反序列化沿用 DrmJsonContext 的大小写不敏感设置。
if (args.Length != 1)
{
    Console.WriteLine("用法：BBDown.DRM <请求JSON路径>");
    return 2;
}

PostProcessRequest? request;
try
{
    request = JsonSerializer.Deserialize(File.ReadAllText(args[0]), DrmJsonContext.Default.PostProcessRequest);
}
catch (Exception ex)
{
    Console.WriteLine($"请求 JSON 无效：{ex.Message}");
    return 2;
}

if (request is null)
{
    Console.WriteLine("请求 JSON 无效");
    return 2;
}

var keys = KeyConfig.Load( );
var wvdPath = KeyConfig.FindWvdPath( );
if (!keys.HasKeys && wvdPath is null)
{
    // bili_drm（clearkey）通道零配置自动取钥，无需密钥表或 wvd；仅当两者皆缺时提示
    Console.WriteLine("未配置密钥：bili_drm 通道可零配置自动取钥；widevine 需 BBDOWN_WVD_PATH 或同目录 device.wvd，密钥表可选 BBDOWN_DRM_KEYS 或 BBDown.DRM.json");
}

try
{
    // 协议以「退出码 0 且 destPath 存在」判定成功，先清理历史残留，避免旧产物被误判为本次解密成功
    File.Delete(request.DestPath);

    var (drmType, biliDrmUri, psshBase64) = await PlayUrlFetcher.FetchAsync(request.Aid, request.Cid, request.Kind);
    if (drmType is null)
    {
        Console.WriteLine("该轨道无加密信息，无需处理");
        return 0;
    }

    var result = await DrmDecryptor.DecryptAsync(drmType, biliDrmUri, psshBase64, request.TrackPath, request.DestPath, keys, request.Ffmpeg, wvdPath);
    Console.WriteLine($"解密结果：{result}");
    return result == DrmResult.Decrypted ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine($"处理失败：{ex.Message}");
    return 1;
}
