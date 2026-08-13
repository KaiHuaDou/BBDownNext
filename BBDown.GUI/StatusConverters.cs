using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BBDown.GUI;

/// <summary>任务状态 → 状态文字颜色；所有状态均返回显式非空笔刷，避免 null 在深色背景下渲染成黑色不可见。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TaskStatus status ? StatusColor(status) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException( );
    }

    public static IBrush? StatusColor(TaskStatus status)
    {
        return status switch
        {
            TaskStatus.Waiting => new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27)),
            TaskStatus.Running => new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xEB)),
            TaskStatus.Success => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            TaskStatus.Failed => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            TaskStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            _ => null,
        };
    }
}

/// <summary>任务状态 → 取消按钮可见性（仅运行中可见；parameter 传 "invert" 取反，用于移除按钮）。</summary>
public sealed class StatusToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var running = value is TaskStatus.Running;
        if (parameter is string s && s == "invert")
        {
            return !running;
        }

        return running;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException( );
    }
}
