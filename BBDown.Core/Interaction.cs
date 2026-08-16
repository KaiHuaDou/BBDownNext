using System;

namespace BBDown.Core;

/// <summary>
/// 交互式下载（逐集确认、手动选轨）的提问。直接读控制台；
/// 无控制台进程下 <see cref="Console.ReadLine"/> 返回 null，按「不交互」回落处理。
/// </summary>
public static class Interaction
{
    /// <summary>读输入前的钩子（如暂停进度条渲染），由 CLI 宿主注册；null 时直接读控制台。回调内不得再调用本类方法。</summary>
    public static Action? BeforeRead { get; set; }

    /// <summary>读输入后的钩子（如恢复进度条渲染），由 CLI 宿主注册；null 时直接读控制台。回调内不得再调用本类方法。</summary>
    public static Action? AfterRead { get; set; }

    /// <summary>提问并读取一行输入；返回 null 表示宿主不支持交互。</summary>
    public static string? AskLine(string prompt)
    {
        // 进度条在后台按帧重绘当前行，会覆盖这里的提示与用户输入，先暂停
        BeforeRead?.Invoke( );
        Logger.Log(prompt, false);
        var line = Console.ReadLine( );
        AfterRead?.Invoke( );
        return line;
    }

    /// <summary>读取 [0, count) 的序号，输入非法回落 0（交互选轨不该因手滑输入而抛异常）。</summary>
    public static int AskIndex(string prompt, int count)
    {
        var input = AskLine(prompt);
        return int.TryParse(input, out var index) && index >= 0 && index < count ? index : 0;
    }
}
