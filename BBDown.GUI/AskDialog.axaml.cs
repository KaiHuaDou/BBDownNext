#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

using BBDown.Core.Workflow;

namespace BBDown.GUI;

/// <summary>
/// 选项请求弹窗：显示 Prompt 与选项列表，点击选项即关闭并携带所选 Id；
/// 窗口被关闭（取消 / Esc）时 Result 为 null，由调用方回落默认选项。
/// </summary>
public partial class AskDialog : Window
{
    /// <summary>用户选择的选项 Id；窗口被关闭（未选）为 null。</summary>
    public string? Result { get; private set; }

    // 无参构造仅用于 Avalonia XamlLoader 的运行时可达性检查，实际使用走带参构造
    public AskDialog( )
    {
        InitializeComponent( );
    }

    public AskDialog(OptionRequestEvent request)
        : this( )
    {
        PromptText.Text = request.Prompt;
        OptionList.ItemsSource = request.Options;
    }

    private void OptionClicked(object? o, RoutedEventArgs e)
    {
        if (o is Button { Tag: AskOption option })
        {
            Result = option.Id;
            Close( );
        }
    }

    private void DismissButtonClicked(object? o, RoutedEventArgs e)
    {
        Close( );
    }
}
