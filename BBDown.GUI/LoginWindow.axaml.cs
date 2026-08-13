#pragma warning disable CS8602, CA1001 // CS8602：Avalonia 源生成的 x:Name 控件字段可空；CA1001：tokenSource 生命周期随窗口，在 Closed 中释放

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using BBDown.Core.Auth;

namespace BBDown.GUI;

public partial class LoginWindow : Window
{
    private CancellationTokenSource? tokenSource;
    private volatile bool closed;
    private bool ready;
    private LoginChannel channel;

    public LoginResult? Result { get; private set; }

    public LoginWindow( )
        : this(LoginChannel.Web)
    {
    }

    public LoginWindow(LoginChannel initial)
    {
        InitializeComponent( );
        ApplyChannel(initial);
        Opened += LoginWindowOpened;
        Closed += LoginWindowClosed;
    }

    private void LoginWindowOpened(object? o, EventArgs e)
    {
        ready = true;
        StartLogin(channel);
    }

    private void LoginWindowClosed(object? o, EventArgs e)
    {
        closed = true;
        tokenSource?.Cancel( );
        tokenSource?.Dispose( );
        tokenSource = null;
    }

    private void ChannelRadioButtonChecked(object? o, RoutedEventArgs e)
    {
        if (!ready || o is not RadioButton { IsChecked: true, Tag: string tag })
        {
            return;
        }

        var selected = tag switch
        {
            "tv" => LoginChannel.Tv,
            "app" => LoginChannel.App,
            _ => LoginChannel.Web,
        };
        if (selected == channel)
        {
            return;
        }

        StartLogin(selected);
    }

    private void CancelButtonClicked(object? o, RoutedEventArgs e)
    {
        tokenSource?.Cancel( );
        Close( );
    }

    private void ApplyChannel(LoginChannel value)
    {
        channel = value;
        WebRadioButton.IsChecked = value == LoginChannel.Web;
        TvRadioButton.IsChecked = value == LoginChannel.Tv;
        AppRadioButton.IsChecked = value == LoginChannel.App;
    }

    private void StartLogin(LoginChannel value)
    {
        channel = value;
        tokenSource?.Cancel( );
        tokenSource?.Dispose( );
        tokenSource = new CancellationTokenSource( );
        UpdateStateText("正在生成二维码...");
        var token = tokenSource.Token;
        _ = Task.Run(( ) => RunLoginAsync(value, token));
    }

    private async Task RunLoginAsync(LoginChannel value, CancellationToken token)
    {
        try
        {
            var result = await LoginAsync(value, token);
            if (result is null)
            {
                UpdateStateText("二维码已过期，请重新登录");
                return;
            }

            CompleteLogin(result);
        }
        catch (OperationCanceledException)
        {
            // 用户取消或窗口关闭，静默结束
        }
        catch (Exception e)
        {
            UpdateStateText($"登录失败：{e.Message}");
        }
    }

    private async Task<LoginResult?> LoginAsync(LoginChannel value, CancellationToken token)
    {
        switch (value)
        {
            case LoginChannel.Web:
            {
                var (cookie, refreshToken) = await Login.WebCredentialAsync(ShowQrAsync, UpdateStateText, token);
                return cookie is null ? null : new LoginResult(value, cookie, refreshToken);
            }
            case LoginChannel.Tv:
            {
                var accessToken = await Login.TvCredentialAsync(ShowQrAsync, UpdateStateText, token);
                return accessToken is null ? null : new LoginResult(value, accessToken, null);
            }
            default:
            {
                var accessToken = await Login.AppCredentialAsync(ShowQrAsync, UpdateStateText, token);
                return accessToken is null ? null : new LoginResult(value, accessToken, null);
            }
        }
    }

    // showQr 在后台线程调用：QRCoder 在后台生成，Bitmap 构造回投 UI 线程
    private Task ShowQrAsync(string url)
    {
        if (closed)
        {
            return Task.CompletedTask;
        }

        var bytes = Login.GenerateQrPng(url);
        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed)
            {
                return;
            }

            QrImage.Source = MakeBitmap(bytes);
            StatusText.Text = "等待扫码";
        });
        return Task.CompletedTask;
    }

    private void UpdateStateText(Login.QrState state)
    {
        UpdateStateText(state switch
        {
            Login.QrState.WaitingScan => "等待扫码",
            Login.QrState.WaitingConfirm => "已扫码，请在手机上确认",
            Login.QrState.Expired => "二维码已过期",
            Login.QrState.Success => "登录成功",
            _ => "",
        });
    }

    private void UpdateStateText(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess( ))
        {
            if (!closed)
            {
                Dispatcher.UIThread.Post(( ) => UpdateStateText(text));
            }

            return;
        }

        StatusText.Text = text;
    }

    private void CompleteLogin(LoginResult result)
    {
        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed)
            {
                return;
            }

            Result = result;
            Close( );
        });
    }

    private static Bitmap MakeBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }
}
