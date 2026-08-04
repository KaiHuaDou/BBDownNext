namespace BBDown;

// Aborted 为 true 表示该分 P 应立即结束（不再登记 SavePath）；Selected 需回传以跨重试保留用户已手动选轨的状态
// Preview 为 true 表示产出的是充电试看片段，不应写入归档记录
internal readonly record struct PageOutcome(bool Aborted, string SavePath, bool Selected, bool Preview = false)
{
    public static PageOutcome Abort(bool selected)
    {
        return new(true, "", selected);
    }

    public static PageOutcome Done(string savePath, bool selected)
    {
        return new(false, savePath, selected);
    }
}
