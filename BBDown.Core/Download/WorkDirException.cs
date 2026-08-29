using System;

namespace BBDown.Core.Download;

/// <summary>
/// 工作目录无效或不可写入时抛出，由 Program.RunApp 决定为退出码 1 并打印原始文案（不带「请升级」误导语）。
/// public 是 CA1064 的硬要求（AnalysisLevel=latest-all）。
/// </summary>
public sealed class WorkDirException : Exception
{
    public WorkDirException( ) { }

    public WorkDirException(string message) : base(message) { }

    public WorkDirException(string message, Exception innerException) : base(message, innerException) { }
}
