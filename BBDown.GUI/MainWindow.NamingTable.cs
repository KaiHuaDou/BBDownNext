using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using BBDown.Core.Download;

namespace BBDown.GUI;

/// <summary>命名变量表交互：双击变量行把占位符写入剪贴板，控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    private async void NamingRowDoubleTapped(object? o, RoutedEventArgs e)
    {
        if (o is not Control { DataContext: NamingVariable variable } ||
            GetTopLevel(this) is not { } topLevel ||
            topLevel.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(variable.Token);
        AppendLog($"已复制 {variable.Token}");
    }
}
