using System;

namespace BBDown.Core;

/// <summary>
/// 交互式下载（逐集确认、手动选轨）的提问回调。CLI 默认读控制台；GUI 等无控制台宿主可整体替换为
/// UI 弹窗，或保持默认（无控制台进程下 <see cref="Console.ReadLine"/> 返回 null，按「不交互」回落处理）。
/// </summary>
public static class Interaction
{
    /// <summary>提问并读取一行输入；返回 null 表示宿主不支持交互。默认实现打印提示后读控制台。</summary>
    public static Func<string, string?>? AskLine { get; set; } = prompt =>
    {
        Logger.Log(prompt, false);
        return Console.ReadLine( );
    };

    /// <summary>读取 [0, count) 的序号，输入非法回落 0（交互选轨不该因手滑输入而抛异常）。</summary>
    public static int AskIndex(string prompt, int count)
    {
        var input = AskLine?.Invoke(prompt);
        return int.TryParse(input, out var index) && index >= 0 && index < count ? index : 0;
    }
}
