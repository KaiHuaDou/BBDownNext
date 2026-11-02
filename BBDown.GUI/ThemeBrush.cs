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
    /// 按当前主题变体取画刷。键只登记在 ThemeDictionaries 里，
    /// 必须显式传入 ActualThemeVariant；不带变体的查找按 Default 走，取不到
    /// </summary>
    public static IBrush? Get(string key)
    {
        return Application.Current is { } app
               && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as IBrush
            : null;
    }
}
