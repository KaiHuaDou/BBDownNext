namespace BBDown;

// Aborted 为 true 表示该分 P 应立即结束（不再登记 SavePath）；
// Preview 为 true 表示产出的是充电试看片段，不应写入归档记录
internal readonly record struct PageOutcome(
    bool Aborted,
    string SavePath,
    bool Selected,
    bool Preview = false,
    int VIndex = 0,
    int AIndex = 0)
{
    public static PageOutcome Abort(TrackSelection selection)
    {
        return new(true, "", selection.Selected, VIndex: selection.VIndex, AIndex: selection.AIndex);
    }

    public static PageOutcome Done(string savePath, TrackSelection selection)
    {
        return new(false, savePath, selection.Selected, VIndex: selection.VIndex, AIndex: selection.AIndex);
    }
}

// 交互选轨状态：Selected 表示已手动选过，VIndex/AIndex 为所选序号。
// 下载失败重试时随 PageOutcome 回传恢复，否则重进 RunAsync 会静默落回第 0 条轨道
internal readonly record struct TrackSelection(bool Selected, int VIndex, int AIndex)
{
    public static readonly TrackSelection Default = new(false, 0, 0);
}
