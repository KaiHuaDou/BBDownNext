using System;
using System.Text;
using System.Threading;

using BBDown.Cli;
using BBDown.Core.Logging;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

namespace BBDown;

// 控制台进度条渲染器：订阅 ProgressBus 的进度事件（阶段开始/样本/结束），按 1/8 秒刷新一帧
public sealed class ProgressBar : IDisposable
{
    private const int BarWidth = 40;
    private const string SpinnerFrames = @"|/-\";
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(1.0 / 8);
    // 采样间隔 125ms（与渲染帧率一致）；超过 1 秒无新采样视为下载已结束（进入混流等阶段），清行停止渲染
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

    private readonly Lock gate = new( );
    private readonly Timer? renderTimer;
    private readonly CancellationToken cancelToken;
    private readonly bool drawToConsole = !Console.IsOutputRedirected;
    // 比较置空需要委托实例一致：方法组每次转换都会生成新委托，注册时缓存一份
    private readonly Action? suspendHook;
    private readonly Action? resumeHook;

    // 以下状态字段只在持有 gate 时访问（disposed 例外：Blit 在 WriteGate 内终检，volatile 保证可见性）
    private double ratio;
    private string speedText = string.Empty;
    private string etaText = string.Empty;
    private int spinnerIndex;
    private DateTime etaStart;
    private double lastRatio;
    private long lastSampleTick;
    private bool downloading;
    private bool rendering;
    private bool suspended;
    private volatile bool disposed;

    // 差异重绘基准，与全部控制台写入同锁：只在 WriteGate 内访问
    private string renderedText = string.Empty;

    public ProgressBar(CancellationToken ct = default)
    {
        cancelToken = ct;
        ProgressBus.Subscribe(OnProgress);
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            renderTimer.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            // 退格重绘假定光标停在本行末尾，日志若直接跟在进度条后面会把光标推走，下一帧就把 spinner 打到日志行首
            // 注册日志前置钩子：写日志前先擦掉进度条行，让日志从行首开始（与 LiveProgress 同一机制）
            RenderHook.Install(this, ClearLine);
            suspendHook = Suspend;
            resumeHook = Resume;
            // 逐集确认 / 选轨等交互读输入前暂停渲染，避免进度条覆盖提示与用户输入
            CliInteraction.BeforeRead = suspendHook;
            CliInteraction.AfterRead = resumeHook;
        }
    }

    // 进度事件分发：阶段开始/结束驱动进度条显隐，样本驱动更新
    private void OnProgress(WorkflowEvent evt)
    {
        switch (evt)
        {
            case ProgressRangeStartEvent:
                SetDownloading(true);
                break;
            case ProgressSampleEvent sample:
                OnSample(sample);
                break;
            case ProgressRangeEndEvent:
                SetDownloading(false);
                break;
        }
    }

    // 交互读输入前暂停：停掉渲染定时器并擦掉当前行，让提示与用户输入独占本行
    public void Suspend( )
    {
        if (!drawToConsole)
        {
            return;
        }

        var erase = false;
        lock (gate)
        {
            if (!disposed)
            {
                suspended = true;
                renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                erase = true;
            }
        }

        if (erase)
        {
            Blit(string.Empty);
        }
    }

    // 交互读输入后恢复渲染
    public void Resume( )
    {
        if (!drawToConsole)
        {
            return;
        }

        lock (gate)
        {
            if (!disposed)
            {
                suspended = false;
                renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    // 擦掉进度条行，让紧随其后的日志从行首开始。日志打完由下一帧自动重画
    // 作为 ConsoleHost.BeforeWrite 在 WriteGate 内被调用：单向锁序禁止在此取 gate
    public void ClearLine( )
    {
        // 重定向时不渲染进度条，无事可擦，直接跳过避免无谓锁竞争
        if (!drawToConsole)
        {
            return;
        }

        Blit(string.Empty);
    }

    // 主媒体下载窗口：true 进入下载（恢复渲染），false 下载结束（清行停止渲染）
    // 解析 / 混流 / 封面弹幕等附属下载都不开窗，进度条只在明确下载音视频文件时出现
    private void SetDownloading(bool value)
    {
        if (!drawToConsole)
        {
            return;
        }

        var erase = false;
        lock (gate)
        {
            if (!disposed)
            {
                downloading = value;
                if (value)
                {
                    // 标记本帧为“刚采样”，避免 Render 在首个真实采样到达前误判空闲而清行
                    lastSampleTick = Environment.TickCount64;
                    rendering = true;
                    renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    rendering = false;
                    erase = true;
                }
            }
        }

        if (erase)
        {
            Blit(string.Empty);
        }
    }

    // 阶段内样本每 200ms 到达一次，更新进度、速度与剩余时间显示（speed 由链路折算好）
    private void OnSample(ProgressSampleEvent sample)
    {
        lock (gate)
        {
            // 窗口外（封面/弹幕等附属下载）不驱动进度条
            if (!downloading)
            {
                return;
            }

            ratio = sample.Ratio;
            lastSampleTick = Environment.TickCount64;
            var now = DateTime.UtcNow;
            // 进度回退视为分 P 切换，重置 ETA 基准
            if (lastRatio == 0 || sample.Ratio < lastRatio)
            {
                etaStart = now;
            }

            lastRatio = sample.Ratio;

            if (sample.Speed > 0)
            {
                speedText = $" - {Utils.FormatSpeed((long) sample.Speed, 1)}";
            }

            etaText = Utils.FormatEta(sample.Ratio, now - etaStart) is { } eta ? $" ETA {eta}" : string.Empty;

            // 有采样即视为下载进行中：若因空闲停过渲染，恢复定时器
            if (!rendering)
            {
                rendering = true;
                renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Render( )
    {
        var text = string.Empty;
        lock (gate)
        {
            if (disposed || cancelToken.IsCancellationRequested || suspended)
            {
                return;
            }

            // 窗口外或下载结束（进入混流等阶段）后采样停止：擦掉残留的进度条并停掉渲染
            // 采样停止的空闲判定只是补充，正常路径由 SetDownloading(false) 即时清行
            if (!downloading || Environment.TickCount64 - lastSampleTick > IdleTimeout.TotalMilliseconds)
            {
                rendering = false;
            }
            else
            {
                var filled = Math.Clamp((int) (ratio * BarWidth), 0, BarWidth);
                spinnerIndex = (spinnerIndex + 1) % SpinnerFrames.Length;
                text = $"             [{new string('#', filled)}{new string('-', BarWidth - filled)}] {ratio * 100,3:0.00}% {SpinnerFrames[spinnerIndex]}{speedText}{etaText}";
                renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            }
        }

        Blit(text);
    }

    // 帧差异计算（纯函数）：只回退并重写与上一帧不同的那段后缀，整行重画会闪
    internal static string BuildDiff(string previous, string text)
    {
        var commonPrefixLength = 0;
        var commonLength = Math.Min(previous.Length, text.Length);
        while (commonPrefixLength < commonLength && text[commonPrefixLength] == previous[commonPrefixLength])
        {
            commonPrefixLength++;
        }

        StringBuilder output = new( );
        output.Append('\b', previous.Length - commonPrefixLength);
        output.Append(text[commonPrefixLength..]);

        // 新内容更短时，多出来的旧字符要用空格抹掉
        var overlapCount = previous.Length - text.Length;
        if (overlapCount > 0)
        {
            output.Append(' ', overlapCount);
            output.Append('\b', overlapCount);
        }

        return output.ToString( );
    }

    // 落写收口：全部控制台写入只在 WriteGate 内进行，且不得持有 gate 进入本方法——
    // 擦行回调同样在 WriteGate 内执行，双向取锁即 AB-BA 死锁（见 ConsoleHost 锁序说明）
    private void Blit(string text)
    {
        if (!drawToConsole || cancelToken.IsCancellationRequested)
        {
            return;
        }

        lock (ConsoleHost.WriteGate)
        {
            // 终检 disposed：本帧与 Dispose 的终态擦行经 WriteGate 串行，后到者胜
            // 已释放实例的帧不允许落在擦行之后
            if (disposed)
            {
                return;
            }

            Console.Write(BuildDiff(renderedText, text));
            renderedText = text;
        }
    }

    public void Dispose( )
    {
        // 摘钩在前：Dispose 之后没有日志触发本实例的擦行回调
        ProgressBus.Unsubscribe(OnProgress);
        // 只清自己登记的钩子：两实例共存时不误删后注册者的钩子
        RenderHook.Uninstall(this);

        if (ReferenceEquals(CliInteraction.BeforeRead, suspendHook))
        {
            CliInteraction.BeforeRead = null;
        }

        if (ReferenceEquals(CliInteraction.AfterRead, resumeHook))
        {
            CliInteraction.AfterRead = null;
        }

        lock (gate)
        {
            disposed = true;
            renderTimer?.Dispose( );
        }

        EraseFinal( );
    }

    // 终态擦行：disposed 已置位故不走 Blit；与在途 Render 的 Blit 经 WriteGate 串行
    // 在途帧要么先行（被该帧擦掉）要么因 Blit 的 disposed 终检跳过
    // 与 Blit 一致地在取消后跳过：Ctrl+C 时取消提示已落在本行，再擦会打到提示行上
    private void EraseFinal( )
    {
        if (!drawToConsole || cancelToken.IsCancellationRequested)
        {
            return;
        }

        lock (ConsoleHost.WriteGate)
        {
            Console.Write(BuildDiff(renderedText, string.Empty));
            renderedText = string.Empty;
        }
    }
}
