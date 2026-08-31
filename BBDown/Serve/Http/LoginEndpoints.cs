using System;
using System.Threading;

using BBDown.Core.Auth;
using BBDown.Serve.Auth;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BBDown.Serve.Http;

/// <summary>
/// 扫码登录端点：POST /api/v1/login/qr 起点（返回二维码 PNG 与轮询键），GET /api/v1/login/qr/{qrcodeKey} 轮询状态。
/// 登录编排在 Core（Login / CredentialStore），本类仅转发会话数据。
/// </summary>
internal static class LoginEndpoints
{
    public static void MapLoginEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/login/qr", async (QrLoginStartRequest request, QrLoginStore store, CancellationToken token) =>
        {
            var outcome = await store.StartAsync((request.Channel ?? "").Trim( ).ToLowerInvariant( ), token);
            if (outcome.Session is not { } session)
            {
                return outcome.InvalidChannel
                    ? Results.BadRequest(outcome.Error)
                    : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }

            var qrPng = Login.GenerateQrPng(session.QrUrl!);
            return Results.Json(
                new QrLoginStartResponse(session.Key, Convert.ToBase64String(qrPng), session.Channel),
                AppJsonSerializerContext.Default.QrLoginStartResponse);
        }).RequireRateLimiting("loginSubmit");

        app.MapGet("/api/v1/login/qr/{qrcodeKey}", (string qrcodeKey, QrLoginStore store) =>
        {
            if (!store.TryGet(qrcodeKey, out var session))
            {
                return Results.NotFound( );
            }

            return Results.Json(
                new QrLoginStatusResponse(
                    session.State,
                    session.AccountName,
                    session.Cookie,
                    session.AccessToken,
                    session.RefreshToken,
                    session.Error),
                AppJsonSerializerContext.Default.QrLoginStatusResponse);
        });
    }
}