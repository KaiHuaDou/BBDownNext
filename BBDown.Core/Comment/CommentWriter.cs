#pragma warning disable CA1308 // 产物文件名使用小写扩展名

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;

using static BBDown.Core.Logger;

namespace BBDown.Core.Comment;

/// <summary>
/// 把抓取好的评论区按格式写盘：{basePath} 经 ChangeExtension 换成 .comments.json / .comments.txt
/// 单个格式写盘失败（如追加后缀后路径越限）只跳过该格式，不阻断其余格式
/// 不做存在性判断：是否因文件已存在而跳过由调用方在抓取前决定，写盘一律覆盖
/// </summary>
public static class CommentWriter
{
    // JsonSerializerContext 的 Encoder 默认会把中文转成 \u4e2d\u6587，AOT 下必须显式放开转义
    private static readonly CommentJsonContext JsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    public static async Task<List<string>> WriteAsync(string basePath, CommentDocument document, IReadOnlyList<CommentFormat> formats, bool fullReplies, CancellationToken token = default)
    {
        var written = new List<string>( );
        foreach (var format in formats)
        {
            var path = Path.ChangeExtension(basePath, $".comments.{format.ToString( ).ToLowerInvariant( )}");
            try
            {
                var content = format == CommentFormat.Json
                    ? JsonSerializer.Serialize(document, JsonContext.CommentDocument)
                    : CommentRenderer.Render(document, fullReplies);
                await File.WriteAllTextAsync(path, content, token);
            }
            catch (IOException ex) when (ex is PathTooLongException or DirectoryNotFoundException)
            {
                // 标题截断只作用于基底文件名，追加 .comments.* 仍可能越限；不阻断其余格式
                LogWarn($"评论文件因路径过长无法写入（{path}）：{ex.Message}");
                continue;
            }

            written.Add(path);
        }

        return written;
    }
}
