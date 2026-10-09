using System.ComponentModel;

namespace BBDown.GUI;

/// <summary>
/// 内容复选项数据：字符键 + 显示名来自 ContentSelector.Order 单一来源；IsChecked / IsEnabled 为 UI 态，变化时通知绑定
/// 用 class 而非 record：勾选与可用性随交互变，值等值会把它们计入，List.Contains / Distinct 随之失真
/// </summary>
public sealed class ContentOption(char key, string label) : INotifyPropertyChanged
{
    public char Key { get; } = key;

    public string Label { get; } = label;

    public bool IsChecked
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
