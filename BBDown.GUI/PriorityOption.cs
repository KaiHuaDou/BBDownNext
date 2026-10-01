using System.ComponentModel;

namespace BBDown.GUI;

/// <summary>优先级 chip 数据项；Order 为 chips 流中 1 起始的序号，变化时通知绑定刷新。</summary>
public sealed class PriorityOption(string name) : INotifyPropertyChanged
{
    public string Name { get; } = name;

    public int Order
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
