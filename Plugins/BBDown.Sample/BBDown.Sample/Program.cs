using System;
using System.IO;
using System.Text.Json;

using BBDown.Core.Download;

// 外部后处理协议的最小实现（PROTOCOL.md）：读取主程序落盘的请求 JSON 并打印字段。
// 反序列化复用主程序同款源生成器上下文 PostProcessJsonContext，保证字段名与主程序输出严格对齐。
// 本示例不做实际处理：以 0 退出且不写 DestPath，主程序据此判定「轨道无需处理」，原文件照常混流。
if (args.Length != 1)
{
    Console.WriteLine("用法：BBDown.Sample <请求JSON路径>");
    return 2;
}

var request = JsonSerializer.Deserialize(File.ReadAllText(args[0]), PostProcessJsonContext.Default.PostProcessRequest);
if (request is null)
{
    return 2;
}

Console.WriteLine($"收到请求：Aid={request.Aid} Cid={request.Cid} Kind={request.Kind}");
Console.WriteLine($"轨道：{request.TrackPath} -> {request.DestPath}（示例插件不做处理）");
Console.WriteLine("实际插件应把产物写入 DestPath 后以 0 退出，失败时返回非 0 以保留原文件。");
return 0;
