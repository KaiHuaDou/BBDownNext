using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BBDown.GUI;

/// <summary>任务状态 → 状态文字颜色；取主题变体登记的笔刷，未登记时为 null，由控件沿用主题前景色。</summary>
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

    /// <summary>取 App.axaml ThemeDictionaries 登记的画刷，已是单例引用，不再逐次新建</summary>
    public static IBrush? StatusColor(TaskStatus status)
    {
        return status switch
        {
            TaskStatus.Waiting => ThemeBrush.Get(ThemeBrush.Waiting),
            TaskStatus.Running => ThemeBrush.Get(ThemeBrush.Running),
            TaskStatus.Success => ThemeBrush.Get(ThemeBrush.Ok),
            TaskStatus.Failed => ThemeBrush.Get(ThemeBrush.Failed),
            TaskStatus.Cancelled => ThemeBrush.Get(ThemeBrush.Hint),
            _ => null,
        };
    }
}

/// <summary>任务状态 → 按钮可见性：默认仅运行中；"invert" 非运行中（移除按钮）；"retry" 仅失败/已取消（继续按钮）。</summary>
public sealed class StatusToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (parameter as string) switch
        {
            "invert" => value is not TaskStatus.Running,
            "retry" => value is TaskStatus.Failed or TaskStatus.Cancelled,
            _ => value is TaskStatus.Running,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException( );
    }
}

/// <summary>直播任务「停止录制」按钮可见性：仅运行中的直播任务可见（区分于普通任务的取消）。</summary>
public sealed class LiveStopVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TaskState { Kind: TaskKind.Live, Status: TaskStatus.Running };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException( );
    }
}

/// <summary>直播任务运行中 → 进度条不确定（无总量，滚动动画）；其余确定进度。</summary>
public sealed class LiveIndeterminateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TaskState { Kind: TaskKind.Live, Status: TaskStatus.Running };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException( );
    }
}
