#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Threading.Tasks;

using Avalonia.Threading;

using BBDown.Core.Workflow;

namespace BBDown.GUI;

/// <summary>AskBus 弹窗交互消费端：选项请求回投 UI 线程弹窗，选择后应答；控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    // OnAsk 在下载线程（调度循环）同步回调，弹窗必须回投 UI 线程；并发弹窗叠加（Avalonia 多模态窗口）
    private void OnAsk(OptionRequestEvent request)
    {
        Dispatcher.UIThread.Post(( ) => _ = HandleAskAsync(request));
    }

    private async Task HandleAskAsync(OptionRequestEvent request)
    {
        // 取默认项排在 try 之外：DefaultOptionId 为 null 且 Options 为空时抛 IndexOutOfRangeException，
        // 调用点是无人观察的任务，异常会让 AskBus 永远收不到应答，下载线程挂满超时
        var options = request.Options;
        var defaultId = request.DefaultOptionId ?? (options.Count > 0 ? options[0].Id : null);
        if (defaultId is null)
        {
            AskBus.Answer(request.RequestId, new AskAnswer(""));
            return;
        }

        var fallback = new AskAnswer(defaultId);
        // 已关窗或已过 Deadline：AskBus 侧不接受应答，直接回落默认选项（与 CLI 回车回落规则一致）
        if (closed || request.Deadline <= DateTimeOffset.Now)
        {
            AskBus.Answer(request.RequestId, fallback);
            return;
        }

        try
        {
            var dialog = new AskDialog(request);
            await dialog.ShowDialog(this);
            // 窗口被关闭（未选）→ 回落默认选项
            AskBus.Answer(request.RequestId, new AskAnswer(dialog.Result ?? fallback.OptionId));
        }
        catch (Exception)
        {
            // 弹窗过程异常（如关窗竞态）：吞掉并回落默认，避免 async 回调异常触发全局错误对话框关停应用
            AskBus.Answer(request.RequestId, fallback);
        }
    }
}
