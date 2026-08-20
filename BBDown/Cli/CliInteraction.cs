using System;
using System.Collections.Generic;
using System.Linq;

using BBDown.Core;
using BBDown.Core.Workflow;

namespace BBDown.Cli;

/// <summary>
/// 控制台交互消费端：订阅 AskBus，把选项请求渲染为提示并读取一行输入，输入经规范化映射后应答。
/// 读输入前 / 后调用钩子（进度条暂停 / 恢复渲染），由 ProgressBar 注册。
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
        var input = Console.ReadLine( );
        AfterRead?.Invoke( );
        var optionId = Normalize(input, evt.Options) ?? evt.DefaultOptionId ?? evt.Options[0].Id;
        AskBus.Answer(evt.RequestId, new AskAnswer(optionId, input));
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
