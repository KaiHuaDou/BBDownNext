#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BBDown.Core.Auth;

namespace BBDown.GUI;

/// <summary>三通道登录入口与登录态展示，控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    private async void LoginButtonClicked(object? o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: string tag })
        {
            return;
        }

        var channel = tag switch
        {
            "tv" => LoginChannel.Tv,
            "app" => LoginChannel.App,
            _ => LoginChannel.Web,
        };
        var dialog = new LoginWindow(channel);
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

    private async Task ApplyLoginResultAsync(LoginResult result)
    {
        var issueTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds( );
        switch (result.Channel)
        {
            case LoginChannel.Web:
                await CredentialStore.SaveWebCookie(result.Credential, refreshToken: result.RefreshToken, issueTs: issueTs);
                ApplyApi("web");
                AppendLog("WEB 登录成功，Cookie 已写入 BBDown.data");
                break;
            case LoginChannel.Tv:
                await CredentialStore.SaveTvToken(result.Credential, issueTs: issueTs);
                ApplyApi("tv");
                AppendLog("TV 登录成功，access_token 已写入 BBDown.data");
                break;
            case LoginChannel.App:
                await CredentialStore.SaveAppToken(result.Credential, issueTs: issueTs);
                ApplyApi("app");
                AppendLog("APP 登录成功，access_token 已写入 BBDown.data");
                break;
        }

        await RefreshLoginStatusAsync( );
    }

    /// <summary>启动即续期 WEB Cookie（best-effort），凭据写入变化才提示；完成后探测登录态，展示续期后的结果。</summary>
    private async Task RenewWebCookieOnStartupAsync( )
    {
        try
        {
            var before = CredentialStore.LoadWebCookie( );
            await Login.TryRefreshWebCookieIfStaleAsync( );
            if (CredentialStore.LoadWebCookie( ) != before)
            {
                AppendLog("WEB Cookie 已自动续期");
            }
        }
        catch (Exception e)
        {
            AppendLog($"WEB Cookie 续期检查失败：{e.Message}");
        }

        await RefreshLoginStatusAsync( );
    }

    /// <summary>
    /// 探测三通道登录态并分别展示。三通道各自独立呈现，未登录的通道各自独立显示
    /// 已保存但服务端未认可（失效）与探测失败分列两态
    /// </summary>
    private async Task RefreshLoginStatusAsync( )
    {
        try
        {
            foreach (var status in await Login.QueryStatusAsync( ))
            {
                SetLoginStatus(ChannelOf(status.Channel), Describe(status));
            }
        }
        catch (Exception e)
        {
            AppendLog($"登录态探测失败：{e.Message}");
        }
    }

    private static LoginChannel ChannelOf(string channel)
    {
        return channel switch
        {
            Login.TvChannel => LoginChannel.Tv,
            Login.AppChannel => LoginChannel.App,
            _ => LoginChannel.Web,
        };
    }

    private static string Describe(LoginStatus status)
    {
        var (state, detail) = Login.Describe(status);
        return detail.Length > 0 ? $"{state}：{detail}" : state;
    }

    private void SetLoginStatus(LoginChannel channel, string text)
    {
        if (!Dispatcher.UIThread.CheckAccess( ))
        {
            if (!closed)
            {
                Dispatcher.UIThread.Post(( ) => SetLoginStatus(channel, text));
            }

            return;
        }

        var block = channel switch
        {
            LoginChannel.Web => WebLoginStatusText,
            LoginChannel.Tv => TvLoginStatusText,
            _ => AppLoginStatusText,
        };
        block.Text = text;
    }
}
