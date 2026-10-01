#pragma warning disable CA2000 // DataTransfer 的所有权随 DoDragDropAsync 转移：平台层包装器在拖拽结束后调用 ReleaseDataTransfer 完成释放，此处不得自行 Dispose

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BBDown.GUI;

/// <summary>优先级拖拽选择器：已选 chips 流从左到右即优先序，未选区按全集自然序；无文本框无模式态，chips 即唯一真相。</summary>
public sealed partial class PriorityPicker : UserControl
{
    private static readonly DataFormat<string> ChipFormat = DataFormat.CreateStringApplicationFormat("bbdown-gui-priority-chip");

    // 位移平方阈值：小于该值视为点击而非拖拽，避免 chip 上的按钮点击误触发拖拽
    private const double DragThresholdSquared = 25;

    private readonly List<PriorityOption> all = [];
    private readonly ObservableCollection<PriorityOption> priority = [];
    private readonly ObservableCollection<PriorityOption> available = [];

    // 拖拽状态：按下事件参数（DoDragDropAsync 只接受 PointerPressedEventArgs）与待拖项，Moved 过阈值后发起
    private Point pressPos;
    private PriorityOption? pending;
    private PointerPressedEventArgs? pressed;

    public PriorityPicker( )
    {
        InitializeComponent( );
        PriorityList.ItemsSource = priority;
        AvailableList.ItemsSource = available;
    }

    /// <summary>宿主注入全集（按高 → 低），未选区按此序展示；选中态由 Reset 单独写入。</summary>
    public void Initialize(IReadOnlyList<string> names)
    {
        all.Clear( );
        all.AddRange(names.Select(n => new PriorityOption(n)));
        Reset("");
    }

    /// <summary>按逗号串重建选中态（顺序即优先序）；全集未命中的 token 忽略。</summary>
    public void Reset(string? value)
    {
        var tokens = value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        priority.Clear( );
        foreach (var token in tokens)
        {
            if (all.FirstOrDefault(o => o.Name.Equals(token, StringComparison.OrdinalIgnoreCase)) is { } option)
            {
                priority.Add(option);
            }
        }

        Sync( );
    }

    /// <summary>当前优先序（逗号分隔）；空选返回空串，即 Core 的默认原序语义。</summary>
    public string Priority => string.Join(",", priority.Select(o => o.Name));

    // 统一重算：可用区 = 全集 − 已选（保持全集序），序号 1 起始，空态提示与清空按钮显隐
    private void Sync( )
    {
        var used = new HashSet<PriorityOption>(priority);
        available.Clear( );
        foreach (var option in all)
        {
            if (!used.Contains(option))
            {
                available.Add(option);
            }
        }

        for (var i = 0; i < priority.Count; i++)
        {
            priority[i].Order = i + 1;
        }

        EmptyHint.IsVisible = priority.Count == 0;
        ClearButton.IsVisible = priority.Count > 0;
    }

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PriorityOption option })
        {
            priority.Add(option);
            Sync( );
        }
    }

    private void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PriorityOption option })
        {
            priority.Remove(option);
            Sync( );
        }
    }

    private void ClearClick(object? sender, RoutedEventArgs e)
    {
        priority.Clear( );
        Sync( );
    }

    private void ChipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PriorityOption option })
        {
            pressPos = e.GetPosition(this);
            pressed = e;
            pending = option;
        }
    }

    private async void ChipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (pending is null || pressed is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            pending = null;
            pressed = null;
            return;
        }

        var pos = e.GetPosition(this);
        var dx = pos.X - pressPos.X;
        var dy = pos.Y - pressPos.Y;
        if (dx * dx + dy * dy < DragThresholdSquared)
        {
            return;
        }

        var dragged = pending;
        var trigger = pressed;
        pending = null;
        pressed = null;

        var data = new DataTransfer( );
        data.Add(DataTransferItem.Create(ChipFormat, dragged.Name));
        await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Move);
    }

    private void ChipDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(ChipFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    // 拖到某个 chip 上：插入到它前面；Handled 阻止冒泡到 ListDrop
    private void ChipDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: PriorityOption target } ||
            e.DataTransfer.TryGetValue(ChipFormat) is not { } name)
        {
            return;
        }

        var dragged = priority.FirstOrDefault(o => o.Name == name);
        if (dragged is null || ReferenceEquals(dragged, target))
        {
            return;
        }

        priority.Move(priority.IndexOf(dragged), priority.IndexOf(target));
        Sync( );
        e.Handled = true;
    }

    // 拖到 chips 区空白：移到末尾
    private void ListDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ChipFormat) is not { } name)
        {
            return;
        }

        var dragged = priority.FirstOrDefault(o => o.Name == name);
        if (dragged is null)
        {
            return;
        }

        var from = priority.IndexOf(dragged);
        if (from == priority.Count - 1)
        {
            return;
        }

        priority.Move(from, priority.Count - 1);
        Sync( );
    }
}
