using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Workflow;

namespace BBDown.Cli;

/// <summary>
/// 控制台交互消费端：订阅 AskBus，把选项请求渲染为提示并读取一行输入，输入经规范化映射后应答
/// 读输入前 / 后调用钩子（进度条暂停 / 恢复渲染），由 ProgressBar 注册
/// </summary>
public sealed class CliInteraction : IDisposable
{
    /// <summary>读输入前的钩子（如暂停进度条渲染），由 CLI 宿主注册；null 时直接读控制台。</summary>
    public static Action? BeforeRead { get; set; }

    /// <summary>读输入后的钩子（如恢复进度条渲染），由 CLI 宿主注册；null 时直接读控制台。</summary>
    public static Action? AfterRead { get; set; }

    public CliInteraction( )
    {
        AskBus.Subscribe(OnAsk);
    }

    public void Dispose( )
    {
        AskBus.Unsubscribe(OnAsk);
    }

    private static void OnAsk(OptionRequestEvent evt)
    {
        BeforeRead?.Invoke( );
        Logger.Log(evt.Prompt, false);
        string? input;
        try
        {
            input = ReadLineOrCancel( );
        }
        finally
        {
            AfterRead?.Invoke( );
        }

        var optionId = Normalize(input, evt.Options) ?? evt.DefaultOptionId ?? evt.Options[0].Id;
        AskBus.Answer(evt.RequestId, new AskAnswer(optionId, input));
    }

    // 同步 Console.ReadLine 不响应取消令牌：Ctrl+C 后进程会一直挂起在等待输入上
    // 把读入放到线程池与取消句柄竞速，取消时按取消处理上抛（AskBus.Ask 的同步调用链原样传播）
    // 被放弃的读入线程阻塞在控制台上，取消即退出进程，由进程回收，无需专门终止
    private static string? ReadLineOrCancel( )
    {
        var read = Task.Run(( ) => Console.ReadLine( ));
        var which = WaitHandle.WaitAny([AppEnv.CancellationToken.WaitHandle, ((IAsyncResult) read).AsyncWaitHandle]);
        if (which == 0)
        {
            throw new OperationCanceledException(AppEnv.CancellationToken);
        }

        return read.Result;
    }

    // 输入规范化：忽略大小写匹配选项 Id，再尝试常见全拼缩写（CLI 交互便利，与短形式选项 Id 对应）
    private static string? Normalize(string? input, IReadOnlyList<AskOption> options)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim( );
        foreach (var option in options)
        {
            if (option.Id.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return option.Id;
            }
        }

        var alias = text.ToUpperInvariant( ) switch
        {
            "YES" => "y",
            "ALL" => "a",
            "QUIT" => "q",
            "NO" => "n",
            _ => null,
        };
        return alias is not null && options.Any(o => o.Id.Equals(alias, StringComparison.OrdinalIgnoreCase)) ? alias : null;
    }
}
