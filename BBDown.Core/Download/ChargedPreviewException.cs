using System;

namespace BBDown.Core.Download;

/// <summary>
/// 稿件为充电专属且 playurl 只下发了试看片段时抛出，由 Program.RunApp 决定为退出码 2。
/// public 是 CA1064 的硬要求（AnalysisLevel=latest-all）。
/// </summary>
public sealed class ChargedPreviewException : Exception
{
    public ChargedPreviewException( ) { }

    public ChargedPreviewException(string message) : base(message) { }

    public ChargedPreviewException(string message, Exception innerException) : base(message, innerException) { }
}
