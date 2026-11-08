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
    private readonly LoginChannel channel;

    private CancellationTokenSource? tokenSource;
    private volatile bool closed;

    // 会话轮次：每次重试自增；旧会话的迟到回投按轮次整体丢弃，避免状态文本被上一次运行的回调覆盖
    private int session;

    // 窗口的对外表现只有这一个来源：调用方选阶段，状态文本与重试按钮由 SetPrompt 推出
    private enum Prompt { Generating, WaitingScan, WaitingConfirm, Expired, Failed, Done }

    public LoginResult? Result { get; private set; }

    // 无参构造仅用于 Avalonia XamlLoader 的运行时可达性检查，实际使用走带参构造
    public LoginWindow( ) : this(LoginChannel.Web) { }

    public LoginWindow(LoginChannel value)
    {
        InitializeComponent( );
        channel = value;
        Title = $"{ChannelLabel(value)} 扫码登录";
        ChannelText.Text = ChannelLabel(value);
        Opened += LoginWindowOpened;
        Closed += LoginWindowClosed;
    }

    private static string ChannelLabel(LoginChannel value)
    {
        return value switch
        {
            LoginChannel.Tv => "TV",
            LoginChannel.App => "APP",
            _ => "WEB",
        };
    }

    private void LoginWindowOpened(object? o, EventArgs e)
    {
        StartLogin( );
    }

    private void LoginWindowClosed(object? o, EventArgs e)
    {
        closed = true;
        tokenSource?.Cancel( );
        tokenSource?.Dispose( );
        tokenSource = null;
    }

    private void CancelButtonClicked(object? o, RoutedEventArgs e)
    {
        tokenSource?.Cancel( );
        Close( );
    }

    private void RetryButtonClicked(object? o, RoutedEventArgs e)
    {
        StartLogin( );
    }

    private void StartLogin( )
    {
        session++;
        tokenSource?.Cancel( );
        tokenSource?.Dispose( );
        tokenSource = new CancellationTokenSource( );
        var current = session;
        PostState(current, ( ) =>
        {
            QrImage.Source = null;
            SetPrompt(Prompt.Generating);
        });
        var token = tokenSource.Token;
        _ = Task.Run(( ) => RunLoginAsync(current, token));
    }

    private async Task RunLoginAsync(int current, CancellationToken token)
    {
        try
        {
            var result = await LoginAsync(current, token);
            if (result is null)
            {
                PostState(current, ( ) => SetPrompt(Prompt.Expired));
                return;
            }

            PostState(current, ( ) =>
            {
                Result = result;
                Close( );
            });
        }
        catch (OperationCanceledException)
        {
            // 用户取消或窗口关闭，静默结束
        }
        catch (Exception e)
        {
            PostState(current, ( ) => SetPrompt(Prompt.Failed, e.Message));
        }
    }

    private async Task<LoginResult?> LoginAsync(int current, CancellationToken token)
    {
        switch (channel)
        {
            case LoginChannel.Web:
            {
                var (cookie, refreshToken) = await Login.WebCredentialAsync(url => ShowQrAsync(url, current), state => SetQrState(state, current), token);
                return cookie is null ? null : new LoginResult(channel, cookie, refreshToken);
            }
            case LoginChannel.Tv:
            {
                var accessToken = await Login.TvCredentialAsync(url => ShowQrAsync(url, current), state => SetQrState(state, current), token);
                return accessToken is null ? null : new LoginResult(channel, accessToken, null);
            }
            default:
            {
                var accessToken = await Login.AppCredentialAsync(url => ShowQrAsync(url, current), state => SetQrState(state, current), token);
                return accessToken is null ? null : new LoginResult(channel, accessToken, null);
            }
        }
    }

    // showQr 在后台线程调用：QRCoder 在后台生成，Bitmap 构造回投 UI 线程
    private Task ShowQrAsync(string url, int current)
    {
        if (closed)
        {
            return Task.CompletedTask;
        }

        var bytes = Login.GenerateQrPng(url);
        PostState(current, ( ) =>
        {
            QrImage.Source = MakeBitmap(bytes);
            SetPrompt(Prompt.WaitingScan);
        });
        return Task.CompletedTask;
    }

    private void SetQrState(Login.QrState state, int current)
    {
        PostState(current, ( ) => SetPrompt(state switch
        {
            Login.QrState.WaitingScan => Prompt.WaitingScan,
            Login.QrState.WaitingConfirm => Prompt.WaitingConfirm,
            Login.QrState.Expired => Prompt.Expired,
            _ => Prompt.Done,
        }));
    }

    private void SetPrompt(Prompt prompt, string? error = null)
    {
        StatusText.Text = prompt switch
        {
            Prompt.Generating => "正在生成二维码...",
            Prompt.WaitingScan => "等待扫码",
            Prompt.WaitingConfirm => "已扫码，请在手机上确认",
            Prompt.Expired => "二维码已过期，可重新生成",
            Prompt.Failed => $"登录失败：{error}",
            Prompt.Done => "登录成功",
            _ => "",
        };
        RetryButton.IsVisible = prompt is Prompt.Expired or Prompt.Failed;
    }

    // 回投 UI 线程并校验会话轮次：重试后旧会话的更新直接丢弃
    private void PostState(int current, Action action)
    {
        if (closed)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed || session != current)
            {
                return;
            }

            action( );
        });
    }

    private static Bitmap MakeBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }
}
