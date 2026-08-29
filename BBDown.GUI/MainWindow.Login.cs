#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Threading.Tasks;

using Avalonia.Interactivity;
using Avalonia.Threading;

using BBDown.Core;
using BBDown.Core.Auth;

namespace BBDown.GUI;

/// <summary>扫码登录入口与登录态展示，控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    private async void LoginButtonClicked(object? o, RoutedEventArgs e)
    {
        var dialog = new LoginWindow(ReadLoginChannel( ));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } result)
        {
            return;
        }

        try
        {
            await ApplyLoginResultAsync(result);
        }
        catch (Exception ex)
        {
            AppendLog($"保存登录凭据失败：{ex.Message}");
        }
    }

    private LoginChannel ReadLoginChannel( )
    {
        return (ApiBox.SelectedItem as string) switch
        {
            "tv" => LoginChannel.Tv,
            "app" => LoginChannel.App,
            _ => LoginChannel.Web,
        };
    }

    private async Task ApplyLoginResultAsync(LoginResult result)
    {
        var issueTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds( );
        switch (result.Channel)
        {
            case LoginChannel.Web:
                await CredentialStore.SaveWebCookie(result.Credential, refreshToken: result.RefreshToken, issueTs: issueTs);
                ApiBox.SelectedItem = "web";
                AppendLog("WEB 登录成功，Cookie 已写入 BBDown.data");
                break;
            case LoginChannel.Tv:
                await CredentialStore.SaveTvToken(result.Credential, issueTs: issueTs);
                ApiBox.SelectedItem = "tv";
                AppendLog("TV 登录成功，access_token 已写入 BBDown.data");
                break;
            case LoginChannel.App:
                await CredentialStore.SaveAppToken(result.Credential, issueTs: issueTs);
                ApiBox.SelectedItem = "app";
                AppendLog("APP 登录成功，access_token 已写入 BBDown.data");
                break;
        }

        await RefreshLoginStatusAsync( );
    }

    /// <summary>探测当前登录态并刷新状态文字；无凭据时显示「未登录」。WEB 走 nav 探测昵称，TV/APP 仅判断 token 是否已保存。</summary>
    private async Task RefreshLoginStatusAsync( )
    {
        var status = "未登录";
        var cookie = CredentialStore.LoadWebCookie( );
        if (cookie.Length > 0)
        {
            try
            {
                var config = new AppConfig(
                    Cookie: cookie,
                    Token: "",
                    Host: BiliApi.MainHost,
                    EpHost: BiliApi.MainHost,
                    TvHost: BiliApi.TvHost,
                    Area: "",
                    Wbi: "",
                    UserAgent: "");
                var (info, _) = await Account.ProbeAccountAsync(config);
                status = info.IsLogin ? $"WEB 已登录：{info.UserName}" : "未登录";
            }
            catch (Exception e)
            {
                AppendLog($"登录态探测失败（可忽略）：{e.Message}");
            }
        }
        else if (CredentialStore.LoadTvToken( ).Length > 0)
        {
            status = "TV 已登录（access_token 已保存）";
        }
        else if (CredentialStore.LoadAppToken( ).Length > 0)
        {
            status = "APP 已登录（access_token 已保存）";
        }

        SetLoginStatus(status);
    }

    private void SetLoginStatus(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess( ))
        {
            if (!closed)
            {
                Dispatcher.UIThread.Post(( ) => SetLoginStatus(text));
            }

            return;
        }

        LoginStatusText.Text = text;
    }
}
