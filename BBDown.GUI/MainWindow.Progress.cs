using System;
using System.Collections.Generic;
using System.Threading;

using Avalonia.Threading;

using BBDown.Core.Util;
using BBDown.Core.Workflow;

namespace BBDown.GUI;

/// <summary>进度 / ETA / 标题回投，控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    private readonly Dictionary<int, TaskState> byIndex = new( );
    private readonly Lock indexGate = new( );

    // 进度事件在下载线程回调，按 Scope 解析为任务序号后回投 UI 线程更新，避免每次线性扫描任务列表
    private void OnProgress(WorkflowEvent evt)
    {
        switch (evt)
        {
            case ProgressRangeStartEvent start:
                // 新阶段（分 P 切换 / 重下）：重置 ETA 基准，覆盖首帧样本到达前的旧剩余时间残留
                ResetTaskEta(FindByScope(start.Scope));
                break;
            case ProgressSampleEvent sample:
                if (FindByScope(sample.Scope) is { } state)
                {
                    SetTaskSample(state, sample.Ratio, sample.Speed, sample.Detail);
                }

                break;
            case ProgressRangeEndEvent:
                // 任务进入混流等无进度阶段：进度条停在满条，任务收尾时随 Status 隐藏，无需额外动作
                break;
        }
    }

    private TaskState? FindByScope(string scope)
    {
        if (!int.TryParse(scope, out var idx))
        {
            return null;
        }

        lock (indexGate)
        {
            return byIndex.TryGetValue(idx, out var state) ? state : null;
        }
    }

    // 阶段开始即重置 ETA 基准（lastRatio / etaStart 仅 UI 线程读写，回投 UI 线程执行）
    private void ResetTaskEta(TaskState? state)
    {
        if (closed || state is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed)
            {
                return;
            }

            state.lastRatio = 0;
            state.etaStart = DateTime.UtcNow;
        });
    }

    /// <summary>进度总线采样回投进度与速度 / 剩余时间到 UI 线程；stageDetail 为总线阶段文本（直播时长 / 分段 / 清晰度），优先显示。</summary>
    private void SetTaskSample(TaskState state, double ratio, double speed, string? stageDetail)
    {
        if (closed)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed)
            {
                return;
            }

            state.Progress = Math.Clamp(ratio, 0, 1);
            // 直播等阶段文本（时长 / 分段 / 清晰度）直接展示并附速度；视频无阶段文本，走速度 + ETA 折算
            if (stageDetail is not null)
            {
                state.Detail = speed > 0 ? $"{stageDetail} | {Utils.FormatSpeed((long) speed, 1)}" : stageDetail;
                return;
            }

            var now = DateTime.UtcNow;
            // 进度回退视为分 P 切换，重置 ETA 基准
            if (state.lastRatio == 0 || ratio < state.lastRatio)
            {
                state.etaStart = now;
            }

            state.lastRatio = ratio;

            // speed 为链路折算的每秒速率
            var detail = speed > 0 ? Utils.FormatSpeed((long) speed, 1) : "";
            if (Utils.FormatEta(ratio, now - state.etaStart) is { } eta)
            {
                detail = detail.Length == 0 ? $"剩余 {eta}" : $"{detail} · 剩余 {eta}";
            }

            state.Detail = detail;
        });
    }

    /// <summary>解析出标题后回投到任务列表（替代裸 Url 展示）。</summary>
    private void SetTaskTitle(TaskState state, string title)
    {
        if (closed)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (!closed)
            {
                state.Title = title;
            }
        });
    }
}
