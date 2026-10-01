using System.ComponentModel;

namespace BBDown.GUI;

/// <summary>内容复选项数据：字符键 + 显示名来自 ContentSelector.Order 单一来源；IsChecked / IsEnabled 为 UI 态，变化时通知绑定。</summary>
public sealed record ContentOption(char Key, string Label) : INotifyPropertyChanged
{
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
