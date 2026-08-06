using System;
using System.Collections.Generic;
using System.Linq;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Pipeline;

internal static class PageSelect
{
    /// <summary>
    /// 获取选中的分 P 列表。返回 null 表示不筛选（全量下载）；空列表表示用户显式指定但无任何合法分 P（一个都不下）。
    /// 语法：--pages all｜1｜1,2,5｜3-5（闭区间，含两端）｜16-（开区间，到末集）｜-22（开区间，从首集）｜
    /// 1,2,3-3,4-5,6-10,15-latest（混合）｜latest/new=最后一集｜last/LAST=倒数第二集。
    /// 关键字大小写不敏感；表达式首尾、项内空白与尾逗号均忽略；越界数字夹紧到有效边界并提醒；倒序区间自动交换。
    /// </summary>
    internal static List<string>? Resolve(DownloadRequest myOption, VInfo vInfo, string input)
    {
        if (string.IsNullOrWhiteSpace(myOption.Pages))
        {
            //如果用户没有选择分 P, 根据 epid 或 query param 来确定某一集
            if (!string.IsNullOrEmpty(vInfo.Index))
            {
                Log("程序已自动选择你输入的集数，如果要下载其他集数请自行指定分 P（如可使用 -p ALL 代表全部）。");
                return [vInfo.Index];
            }

            var urlPage = GetQueryString("p", input);
            if (!string.IsNullOrEmpty(urlPage))
            {
                Log("程序已自动选择你输入的集数，如果要下载其他集数请自行指定分 P（如可使用 -p ALL 代表全部）。");
                return [urlPage];
            }

            return null;
        }

        if (myOption.Pages.Trim( ).Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pagesInfo = vInfo.PagesInfo;
        // 空系列/空收藏夹：显式 -p 时无可选项，返回空选中列表而非因 pagesInfo[^1] 越界崩溃（§2.6）
        if (pagesInfo.Count == 0)
        {
            return [];
        }

        var lastIndex = pagesInfo[^1].index;        // 列表末项，即最后一集（兼容非连续 index）
        var firstIndex = pagesInfo[0].index;
        var secondLastIndex = pagesInfo.Count >= 2 ? pagesInfo[^2].index : -1;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anyValid = false;

        foreach (var rawToken in myOption.Pages.Split(','))
        {
            var token = rawToken.Trim( );
            if (token.Length == 0)
            {
                continue;
            }

            if (token.Contains('-'))
            {
                var parts = token.Split('-', 2);
                var startStr = parts[0].Trim( );
                var endStr = parts.Length > 1 ? parts[1].Trim( ) : "";

                var startValid = startStr.Length == 0;
                var start = startValid ? firstIndex : ResolveIndex(startStr, firstIndex, lastIndex, secondLastIndex, out startValid);
                var endValid = endStr.Length == 0;
                var end = endValid ? lastIndex : ResolveIndex(endStr, firstIndex, lastIndex, secondLastIndex, out endValid);

                if (!startValid || !endValid)
                {
                    continue;
                }

                if (start > end)
                {
                    (start, end) = (end, start);   // 倒序区间归一化
                }

                for (var i = start; i <= end; i++)
                {
                    if (seen.Add(i.ToString( )))
                    {
                        anyValid = true;
                    }
                }
            }
            else
            {
                var value = ResolveIndex(token, firstIndex, lastIndex, secondLastIndex, out var valid);
                if (!valid)
                {
                    continue;
                }

                if (seen.Add(value.ToString( )))
                {
                    anyValid = true;
                }
            }
        }

        return anyValid ? [.. seen.OrderBy(int.Parse)] : [];
    }

    /// <summary>
    /// 交互式逐集选择：对每个分 P 询问是否下载，[y] 要，[n] 不要，[a] 剩余全部要，[q] 剩余全部不要，回车=不要。
    /// 返回空列表表示一集都没选（一个都不下）。
    /// </summary>
    internal static List<string> ResolveInteractive(VInfo vInfo)
    {
        Log("逐集选择要下载的分 P：[y] 要，[n] 不要，[a] 剩余全部要，[q] 剩余全部不要，直接回车表示不要");
        var selected = new List<string>(vInfo.PagesInfo.Count);
        for (var i = 0; i < vInfo.PagesInfo.Count; i++)
        {
            var p = vInfo.PagesInfo[i];
            Log($"[{p.index}] {p.title}（{FormatTime(p.dur)}）是否下载？[y/n/a/q]", false);
            var input = Console.ReadLine( );
            var choice = string.IsNullOrWhiteSpace(input) ? "n" : input.Trim( ).ToLowerInvariant( );
            if (choice is "y" or "yes")
            {
                selected.Add(p.index.ToString( ));
            }
            else if (choice is "a" or "all")
            {
                selected.AddRange(vInfo.PagesInfo[i..].Select(rest => rest.index.ToString( )));
                break;
            }
            else if (choice is "q" or "quit")
            {
                break;
            }
            // n / no / 其他 / 回车：跳过本集
        }

        return selected.Count == 0 ? [] : [.. selected.OrderBy(int.Parse)];
    }

    // 解析单个分 P 片段：latest/new → 最后一集；last/LAST → 倒数第二集；数字越界则夹紧到有效边界并提醒。
    // 无法解析（非数字非关键字）返回 (0, false)。
    private static int ResolveIndex(string part, int firstIndex, int lastIndex, int secondLastIndex, out bool valid)
    {
        valid = true;
        var upper = part.ToUpperInvariant( );
        if (upper is "LATEST" or "NEW")
        {
            return lastIndex;
        }

        if (upper is "LAST")
        {
            if (secondLastIndex < 0)
            {
                LogError($"分 P 选择「{part}」需要至少 2 个分 P，已忽略。");
                valid = false;
                return 0;
            }

            return secondLastIndex;
        }

        if (int.TryParse(part, out var n))
        {
            if (n < firstIndex)
            {
                Log($"分 P 选择「{part}」小于最小分 P {firstIndex}，已夹紧到 {firstIndex}。");
                return firstIndex;
            }

            if (n > lastIndex)
            {
                Log($"分 P 选择「{part}」超出最大分 P {lastIndex}，已夹紧到 {lastIndex}。");
                return lastIndex;
            }

            return n;
        }

        LogError($"分 P 选择「{part}」不是合法的分 P 编号或关键字（可用：latest/new/last），已忽略。");
        valid = false;
        return 0;
    }
}
