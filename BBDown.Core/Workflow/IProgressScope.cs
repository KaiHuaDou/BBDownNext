using System;

namespace BBDown.Core.Workflow;

/// <summary>
/// 进度阶段句柄：Dispose 即结束阶段（发射结束事件并清空样本）。
/// 由 <see cref="ProgressBus.BeginStage"/> 创建，仅限单任务执行流持有（非线程安全）。
/// </summary>
public interface IProgressScope : IDisposable
{
    /// <summary>
    /// 上报阶段内增量字节。bytesDelta 为本采样周期新增字节，阶段内累计由
    /// <see cref="ProgressBus"/> 按 scope 维护；speed 为折算速率（Byte/s）。
    /// </summary>
    void Report(double ratio, long bytesDelta, double speed, string? detail = null);
}
