using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

using BBDown.Serve;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BBDown.Serve.Http;

/// <summary>
/// 内嵌 WebUI 的静态托管：仅在 --webui 启用且已嵌入 dist 资源时注册。
/// 资源名形如 webui.assets/index-xxx.js、webui.index.html，运行时统一以 '/' 规范化后查表，规避跨平台分隔符差异。
/// </summary>
internal static class WebUiEndpoints
{
    // 扩展名 → MIME：纯查表，AOT 安全（无反射）。未知类型回落 application/octet-stream
    private static readonly Dictionary<string, string> ContentTypes = new( )
    {
        [".js"] = "application/javascript",
        [".mjs"] = "application/javascript",
        [".css"] = "text/css",
        [".html"] = "text/html",
        [".json"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".txt"] = "text/plain",
        [".map"] = "application/json",
    };

    internal static string GetContentType(string path)
    {
        return ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
    }

    // 扫描程序集，建立「相对路径（不含 webui. 前缀，'/' 分隔）→ 真实资源名」映射，供端点闭包按需读取
    internal static IReadOnlyDictionary<string, string> BuildResourceMap(Assembly assembly)
    {
        var map = new Dictionary<string, string>( );
        foreach (var name in assembly.GetManifestResourceNames( ))
        {
            if (!name.StartsWith("webui.", StringComparison.Ordinal))
            {
                continue;
            }

            map[name["webui.".Length..].Replace('\\', '/')] = name;
        }

        return map;
    }

    internal static void MapWebUiEndpoints(this WebApplication app, ServeConfig config, IReadOnlyDictionary<string, string> resources)
    {
        if (!config.EnableWebUi || resources.Count == 0)
        {
            return;
        }

        var assembly = typeof(BBDownServer).Assembly;

        // 静态资源：/assets/* → 内嵌资源；缺失直接 404（不回退 index.html，避免吞掉静态 404）。匿名放行，API 端点仍由 --serve-token 网关
        app.MapGet("/assets/{*file}", (string file, HttpContext context) =>
        {
            if (!resources.TryGetValue($"assets/{file}", out var resourceName))
            {
                return Results.NotFound( );
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return Results.NotFound( );
            }

            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(stream, GetContentType(file));
        }).AllowAnonymous( );

        // SPA 回退：仅 GET 且非 /api、/hubs 的路径返回 index.html；其余保留 404 语义（不遮蔽 API）
        app.MapFallback(async (HttpContext context) =>
        {
            if (context.Request.Method != HttpMethods.Get
                || context.Request.Path.StartsWithSegments("/api")
                || context.Request.Path.StartsWithSegments("/hubs"))
            {
                return Results.NotFound( );
            }

            if (!resources.TryGetValue("index.html", out var resourceName))
            {
                return Results.NotFound( );
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return Results.NotFound( );
            }

            // 注入同源标记：前端据此以 location.origin 调用 API，任意 --listen 均生效。
            // 入口文档禁缓存：资源由带哈希的 /assets 引用，index 滞留旧版会指向已 404 的旧资源
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = (await reader.ReadToEndAsync( )).Replace("</head>", "<script>window.__BBDOWN_SERVE_EMBEDDED__=true</script></head>");
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Content(html, "text/html; charset=utf-8");
        }).AllowAnonymous( );
    }
}
