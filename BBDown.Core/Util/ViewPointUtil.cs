using System.Collections.Generic;

using BBDown.Core.Entity;


namespace BBDown.Core.Util;

/// <summary>
/// 分段点（片头/片尾/章节）的合并与时间轴补齐。WEB 与 APP 两条解析路径共用。
/// </summary>
internal static class ViewPointUtil
{
    /// <summary>
    /// 追加分段点后按起点排序并补齐空隙。会替换 <see cref="ParsedResult.ExtraPoints"/> 的列表实例。
    /// </summary>
    public static void Append(ParsedResult parsedResult, IEnumerable<ViewPoint> points)
    {
        parsedResult.ExtraPoints.AddRange(points);
        parsedResult.ExtraPoints.Sort((p1, p2) => p1.Start.CompareTo(p2.Start));
        parsedResult.ExtraPoints = FillGapsWithMainContent(parsedResult.ExtraPoints);
    }

    // 番剧片头片尾转分段信息, 预计效果: 正片? -> 片头 -> 正片 -> 片尾
    public static List<ViewPoint> FillGapsWithMainContent(List<ViewPoint> points)
    {
        List<ViewPoint> result = [];
        var lastEnd = 0;
        foreach (var point in points)
        {
            if (lastEnd < point.Start)
            {
                result.Add(new ViewPoint( ) { Title = "正片", Start = lastEnd, End = point.Start });
            }

            result.Add(point);
            lastEnd = point.End;
        }

        return result;
    }
}
