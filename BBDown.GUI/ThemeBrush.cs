using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BBDown.GUI;

/// <summary>
/// 取 App.axaml 的 ThemeDictionaries 里登记的画刷，键名与其中的 x:Key 一致
/// </summary>
internal static class ThemeBrush
{
    public const string Hint = "HintBrush";
    public const string Waiting = "WaitingBrush";
    public const string Running = "RunningBrush";
    public const string Ok = "OkBrush";
    public const string Failed = "FailedBrush";

    /// <summary>
    /// 按当前主题变体取画刷。取不到时返回 null 而非回退色：硬编码回退色在另一个主题下必然对比度不足，
    /// 返回 null 时控件沿用自身前景色，而前景色始终随主题
    /// </summary>
    public static IBrush? Get(string key)
    {
        return Application.Current is { } app && app.TryFindResource(key, out var value)
            ? value as IBrush
            : null;
    }
}