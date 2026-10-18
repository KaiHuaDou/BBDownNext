using System;

namespace BBDown.Core;

/// <summary>
/// playurl 载荷明示 is_preview=1 时抛出，仅含试看片段不可作为正片下载，由重试层按不可重试处理
/// public 是 CA1064 的硬要求（AnalysisLevel=latest-all）
/// </summary>
public sealed class PreviewOnlyException : Exception
{
    public PreviewOnlyException( ) { }

    public PreviewOnlyException(string message) : base(message) { }

    public PreviewOnlyException(string message, Exception innerException) : base(message, innerException) { }
}
