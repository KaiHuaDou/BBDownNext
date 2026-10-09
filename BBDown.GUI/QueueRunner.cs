#pragma warning disable CA1001 // shutdown 只读 Token 作关停信号，不取 WaitHandle 故无内核句柄，生命周期随窗口

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Live;

namespace BBDown.GUI;

public enum TaskStatus
{
    Waiting,
    Running,
    Success,
    Failed,
    Cancelled,
}

/// <summary>任务执行链路：直播录制单独成类（录制会话以任务序号注册，停止按钮按序号停录），其余统一走 Core 分发。</summary>
public enum TaskKind
{
    Video,
    Live,
}

/// <summary>队列任务单元：参数快照 + 目标 + 状态 + 日志序号。</summary>
public sealed class TaskState : INotifyPropertyChanged
{
    /// <summary>速度 / 剩余时间采样的基准时刻，仅 UI 线程由采样回调读写。</summary>
    internal DateTime etaStart;

    /// <summary>上一次采样进度（0..1），用于检测分 P 切换导致的进度回退。</summary>
    internal double lastRatio;

    /// <summary>执行器返回码，后台线程在 UI 回投前写入；-1 表示未收尾，关窗写入时据此排除已完成的任务。</summary>
    internal volatile int exitCode = -1;

    public required TaskParams Params { get; init; }
    public required string Url { get; init; }

    /// <summary>直播 / 视频形式；b23 短链展开后才暴露直播形式时由执行器补记。空串通知用于重估按整项绑定的转换器。</summary>
    public required TaskKind Kind
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public required int Index { get; init; }

    /// <summary>
    /// 总线作用域与日志前缀用的序号串。固定用不变文化：随系统文化走会写出非 ASCII 数字，跨线程按串匹配随即失配
    /// </summary>
    public string Scope => Index.ToString(CultureInfo.InvariantCulture);

    /// <summary>直播录制会话号（LiveTarget.SessionId）；非直播任务为 null，停止录制按钮据此定位录制会话。</summary>
    public string? LiveSessionId { get; set; }

    public TaskStatus Status
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            // 进度条可见性 / 直播不确定态等转换器按整项绑定，需整体通知触发重估
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public CancellationTokenSource? TokenSource { get; set; }

    /// <summary>解析出的视频标题（Meta 回吐后填充）；空则列表回退显示 Url。</summary>
    public string? Title
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    /// <summary>任务列表展示文本：有标题显标题，否则显 Url。</summary>
    public string Display => Title ?? Url;

    /// <summary>运行中的速度 / 剩余时间文本（如「12.3 MB/s · 剩余 1m23s」），空则隐藏。</summary>
    public string? Detail
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        }
    }

    /// <summary>当前分片下载进度（0..1）；仅在 UI 线程变更。</summary>
    public double Progress
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText => Status switch
    {
        TaskStatus.Waiting => "等待中",
        TaskStatus.Running => "运行中",
        TaskStatus.Success => "成功",
        TaskStatus.Failed => "失败",
        TaskStatus.Cancelled => "已取消",
        _ => "未知",
    };

    public override string ToString( )
    {
        return $"{StatusText} | {Url}";
    }
}

/// <summary>任务队列与并发调度；集合与状态只在 UI 线程变更（经 dispatch 回投），后台仅执行子进程。</summary>
public sealed partial class QueueRunner(Action<Action> dispatch)
{
    private readonly Action<Action> dispatch = dispatch;
    private readonly List<TaskState> waiting = [];
    private readonly List<TaskState> running = [];
    private readonly List<TaskState> finished = [];
    private readonly CancellationTokenSource shutdown = new( );
    // 槽位可用信号：SignalSlotFree 换新 TCS 并完成旧的，故任意时刻至多一个待消费信号。
    // 计数值信号量做不到这点——并发上限上调多次会累积许可，等待者醒来后 CAS 连续失败形成忙等
    private TaskCompletionSource slotFree = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int activeCount;
    private volatile bool scheduling;
    private int nextIndex = 1;
    private volatile int concurrency = 3;

    /// <summary>
    /// 同时运行的任务数上限。调大时唤醒一个排队中的任务立即扩容，调小不打断在途任务
    /// （超出新上限的任务跑完当前项后不再调度下一项）
    /// </summary>
    public int Concurrency
    {
        get => concurrency;
        set
        {
            if (value > concurrency)
            {
                concurrency = value;
                // 主动唤醒一个等待者使其立刻重试获取槽位，否则它要等到有任务完成才醒
                SignalSlotFree( );
                return;
            }

            concurrency = value;
        }
    }

    /// <summary>任务执行器，返回子进程退出码；未设置时任务直接标记失败。</summary>
    public Func<TaskState, CancellationToken, Task<int>>? Executor { get; set; }

    /// <summary>执行异常日志回调（如启动失败原因）。</summary>
    public Action<TaskState, string>? Logger { get; set; }

    /// <summary>队列或任务状态变化时触发（保证在 UI 线程）。</summary>
    public event EventHandler? Changed;

    public IEnumerable<TaskState> All => waiting.Concat(running).Concat(finished);

    /// <summary>是否存在等待调度的任务。</summary>
    public bool HasWaiting => waiting.Count > 0;

    /// <summary>立即执行：入队尾并启动调度；并发已满时返回 true（任务排队等待）。</summary>
    public bool RunNow(TaskParams options, string url)
    {
        waiting.Add(CreateState(options, url));
        var queued = running.Count >= concurrency;
        Changed?.Invoke(this, EventArgs.Empty);
        StartSchedule( );
        return queued;
    }

    /// <summary>加入任务队列尾部，不启动调度。</summary>
    public void Enqueue(TaskParams options, string url)
    {
        waiting.Add(CreateState(options, url));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>启动队列调度；已调度中时返回 false，调用方据此区分提示文案。</summary>
    public bool StartSchedule( )
    {
        if (scheduling)
        {
            return false;
        }

        scheduling = true;
        _ = Task.Run(RunScheduleAsync);
        return true;
    }

    /// <summary>移除指定任务：等待中或已完成直接移除；运行中不处理（用取消）。</summary>
    public bool Remove(TaskState state)
    {
        var removed = waiting.Remove(state) || finished.Remove(state);
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>
    /// 把失败/已取消的任务重新入队尾并启动调度；不在已完成列表时返回 false
    /// 速度 / 剩余时间基准一并清零：留着上一轮的 Detail、lastRatio 与 etaStart 时，
    /// 重跑后首个样本到达前会显示上一轮的速度与剩余时间
    /// </summary>
    public bool Retry(TaskState state)
    {
        if (!finished.Remove(state))
        {
            return false;
        }

        state.Status = TaskStatus.Waiting;
        state.Progress = 0;
        state.Detail = null;
        state.lastRatio = 0;
        state.etaStart = DateTime.UtcNow;
        state.TokenSource = null;
        state.exitCode = -1;
        waiting.Add(state);
        Changed?.Invoke(this, EventArgs.Empty);
        StartSchedule( );
        return true;
    }

    public void ClearFinished( )
    {
        if (finished.Count == 0)
        {
            return;
        }

        finished.Clear( );
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 关窗收尾：取消全部运行中任务并终止调度循环。
    /// 两件事必须同处一次调用——调度循环阻塞在槽位信号上，只取消任务不会让它退出
    /// </summary>
    public void Shutdown( )
    {
        foreach (var state in running)
        {
            state.TokenSource?.Cancel( );
        }

        shutdown.Cancel( );
    }

    /// <summary>
    /// 取消指定运行中的任务；非本队列的运行态返回 false。
    /// 归属校验不可省：取消源是任务自带的，不校验时任何 TaskState 都能被取消别的任务的执行
    /// </summary>
    public bool CancelTask(TaskState state)
    {
        if (!running.Contains(state))
        {
            return false;
        }

        state.TokenSource?.Cancel( );
        return true;
    }

    private TaskState CreateState(TaskParams options, string url)
    {
        return new TaskState
        {
            Params = options,
            Url = url,
            Kind = DetectKind(url),
            Index = nextIndex++,
        };
    }

    private static TaskKind DetectKind(string url)
    {
        // 直播单独成类：录制会话以直播间注册（LiveSignal），停止按钮按房间精准停录
        // 其余（视频 / 专栏 / 文集 / 空间图文 / 音频 / 动态）在执行期统一经 InputResolver.TryDispatch 分流
        return LiveInputResolver.TryParse(url, out _) ? TaskKind.Live : TaskKind.Video;
    }
}
