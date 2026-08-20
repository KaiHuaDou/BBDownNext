#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using Avalonia.Threading;

using BBDown.Core.Workflow;

namespace BBDown.GUI;

/// <summary>AskBus 弹窗交互消费端：选项请求回投 UI 线程弹窗，选择后应答；控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    // OnAsk 在下载线程（调度循环）同步回调，弹窗必须回投 UI 线程；并发弹窗叠加（Avalonia 多模态窗口）
    private void OnAsk(OptionRequestEvent request)
    {
        Dispatcher.UIThread.Post(async ( ) =>
        {
            if (closed)
            {
                AskBus.Answer(request.RequestId, new AskAnswer(request.DefaultOptionId ?? request.Options[0].Id));
                return;
            }

            var dialog = new AskDialog(request);
            await dialog.ShowDialog(this);
            // 窗口被关闭（未选）→ 回落默认选项，与 CLI 回车回落语义一致
            AskBus.Answer(request.RequestId, new AskAnswer(dialog.Result ?? request.DefaultOptionId ?? request.Options[0].Id));
        });
    }
}
