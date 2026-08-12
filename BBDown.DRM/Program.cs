using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using BBDown.DRM;

// 用法：BBDown.DRM <请求JSON路径>
// 与主程序的文件交换协议：退出码 0 且 destPath 存在 = 成功；其余情况主程序静默保留原文件。
// 请求 JSON 由主程序以 PascalCase 序列化，此处按默认大小写敏感反序列化。
if (args.Length != 1)
{
    Console.WriteLine("用法：BBDown.DRM <请求JSON路径>");
    return 2;
}

var request = JsonSerializer.Deserialize(File.ReadAllText(args[0]), DrmJsonContext.Default.PostProcessRequest);
if (request is null)
{
    Console.WriteLine("请求 JSON 无效");
    return 2;
}

var keys = KeyConfig.Load( );
if (!keys.HasKeys && KeyConfig.FindWvdPath( ) is null)
{
    Console.WriteLine("未配置密钥：设置环境变量 BBDOWN_DRM_KEYS 或 exe 同目录 BBDown.DRM.json；widevine 需 BBDOWN_WVD_PATH 或同目录 device.wvd");
    return 2;
}

try
{
    var (drmType, biliDrmUri, psshBase64) = await PlayUrlFetcher.FetchAsync(request.Aid, request.Cid, request.Kind);
    if (drmType is null)
    {
        Console.WriteLine("该轨道无加密信息，无需处理");
        return 0;
    }

    var result = await DrmDecryptor.DecryptAsync(drmType, biliDrmUri, psshBase64, request.TrackPath, request.DestPath, keys, request.Ffmpeg, KeyConfig.FindWvdPath( ));
    Console.WriteLine($"解密结果：{result}");
    return result == DrmResult.Decrypted ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine($"处理失败：{ex.Message}");
    return 1;
}
