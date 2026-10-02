using System;
using System.Threading;

using BBDown.Core.Logging;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

using static BBDown.Core.Logger;

namespace BBDown.Cli;

/// <summary>
/// 直播录制状态行：订阅 ProgressBus 的阶段事件渲染单行状态（CLI 专属）。
/// 样本 Detail 承载「时长 / 分段 / 清晰度」，体积与速度取自样本字段；\r 原地刷新单行。
/// </summary>
public sealed class LiveProgress : IDisposable
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(0.5);
    // 输出重定向时状态行以定期日志输出，否则日志文件里只会剩最后一行
    private static readonly TimeSpan RedirectedLogInterval = TimeSpan.FromSeconds(60);

    private readonly bool drawToConsole = !Console.IsOutputRedirected;
    private readonly Lock gate = new( );
    private readonly Timer? renderTimer;
    // 比较置空需要委托实例一致：方法组每次转换都会生成新委托，注册时缓存一份
    private readonly Action? clearLineHook;

    // 以下字段只在持有 gate 时访问（disposed 例外：Blit 在 WriteGate 内终检，volatile 保证可见性）
    private ProgressSampleEvent? sample;
    private long lastRedirectedLogTick;
    private bool rendering;
    private volatile bool disposed;

    // 已落屏帧的显示 cell 数，补齐与擦行以此为基准；与全部控制台写入同锁：只在 WriteGate 内访问
    private int renderedCells;

    public LiveProgress( )
    {
        ProgressBus.Subscribe(OnProgress);
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            renderTimer.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            clearLineHook = ClearLine;
            ConsoleHost.BeforeWrite = clearLineHook;
        }
    }

    // 阶段边界驱动显隐，样本驱动更新；重定向时定期落一行日志
    private void OnProgress(WorkflowEvent evt)
    {
        var erase = false;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            switch (evt)
            {
                case ProgressRangeStartEvent:
                    sample = null;
                    rendering = true;
                    renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
                    break;
                case ProgressSampleEvent value:
                    sample = value;
                    rendering = true;
                    renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
                    // 单调时钟计间隔：墙钟回拨会让 60 秒日志漏打或狂打
                    if (!drawToConsole && Environment.TickCount64 - lastRedirectedLogTick >= RedirectedLogInterval.TotalMilliseconds)
                    {
                        lastRedirectedLogTick = Environment.TickCount64;
                        Log(Compose(value));
                    }

                    break;
                case ProgressRangeEndEvent:
                    renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    rendering = false;
                    erase = true;
                    break;
            }
        }

        if (erase)
        {
            Blit(string.Empty);
        }
    }

    private void Render( )
    {
        string text;
        lock (gate)
        {
            if (disposed || !rendering || sample is not { } current)
            {
                return;
            }

            text = Compose(current);
            renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
        }

        Blit(text);
    }

    // 行内容：Detail（时长 / 分段 / 清晰度）+ 体积 + 速度
    private static string Compose(ProgressSampleEvent value)
    {
        return $"{value.Detail} | {Utils.FormatFileSize(value.TotalBytes)} | {Utils.FormatSpeed((long) value.Speed, 1)}";
    }

    // 控制台按 cell 渲染，CJK 等 East Asian Wide 字符占 2 cell；擦行与补齐须按 cell 数计数才擦得干净
    internal static int CellWidth(string text)
    {
        var cells = 0;
        foreach (var ch in text)
        {
            cells += IsWide(ch) ? 2 : 1;
        }

        return cells;
    }

    // 常用 East Asian Wide 字符块的近似覆盖，未含罕用块与 emoji 序列
    private static bool IsWide(char ch)
    {
        return ch is (>= '\u1100' and <= '\u115F')
            or (>= '\u2E80' and <= '\u303E')
            or (>= '\u3041' and <= '\u33FF')
            or (>= '\u3400' and <= '\u4DBF')
            or (>= '\u4E00' and <= '\u9FFF')
            or (>= '\uA000' and <= '\uA4CF')
            or (>= '\uAC00' and <= '\uD7A3')
            or (>= '\uF900' and <= '\uFAFF')
            or (>= '\uFE30' and <= '\uFE4F')
            or (>= '\uFF00' and <= '\uFF60')
            or (>= '\uFFE0' and <= '\uFFE6');
    }

    /// <summary>
    /// 擦掉状态行，让紧随其后的日志从行首开始。日志打完由下一帧自动重画。
    /// 作为 ConsoleHost.BeforeWrite 在 WriteGate 内被调用：单向锁序禁止在此取 gate。
    /// </summary>
    public void ClearLine( )
    {
        // 重定向时无状态行可擦（采样直接落日志），跳过避免无谓写控制台
        if (!drawToConsole)
        {
            return;
        }

        Blit(string.Empty);
    }

    // \r 回到行首整行重写；新帧比旧帧窄时按 cell 数补空格，避免上一帧的残余留在屏幕上。
    // 落写收口：全部控制台写入只在 WriteGate 内进行，且不得持有 gate 进入本方法——
    // 擦行回调同样在 WriteGate 内执行，双向取锁即 AB-BA 死锁（见 ConsoleHost 锁序说明）
    private void Blit(string text)
    {
        if (!drawToConsole)
        {
            return;
        }

        lock (ConsoleHost.WriteGate)
        {
            // 终检 disposed：本帧与 Dispose 的终态擦行经 WriteGate 串行，后到者胜
            if (disposed)
            {
                return;
            }

            if (text.Length == 0)
            {
                Console.Write("\r" + new string(' ', renderedCells) + "\r");
                renderedCells = 0;
                return;
            }

            var cells = CellWidth(text);
            Console.Write("\r" + text);
            if (cells < renderedCells)
            {
                Console.Write(new string(' ', renderedCells - cells));
            }

            renderedCells = cells;
        }
    }

    public void Dispose( )
    {
        // 摘钩在前：Dispose 之后不再有日志触发本实例的擦行回调；比较置空不误删后注册者的钩子
        ProgressBus.Unsubscribe(OnProgress);
        if (ReferenceEquals(ConsoleHost.BeforeWrite, clearLineHook))
        {
            ConsoleHost.BeforeWrite = null;
        }

        lock (gate)
        {
            disposed = true;
            renderTimer?.Dispose( );
        }

        EraseFinal( );
    }

    // 终态擦行：disposed 已置位故不走 Blit；与在途 Render 的 Blit 经 WriteGate 串行，
    // 在途帧要么先行（被本次擦掉）要么因 Blit 的 disposed 终检跳过
    private void EraseFinal( )
    {
        if (!drawToConsole)
        {
            return;
        }

        lock (ConsoleHost.WriteGate)
        {
            Console.Write("\r" + new string(' ', renderedCells) + "\r");
            renderedCells = 0;
        }
    }
}
