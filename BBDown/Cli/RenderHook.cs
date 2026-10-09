using System;
using System.Collections.Generic;

using BBDown.Core.Logging;

namespace BBDown.Cli;

/// <summary>
/// 控制台状态行渲染器挂到 <see cref="ConsoleHost.BeforeWrite"/> 的登记与摘除
/// 方法组每次转换都生成新的委托实例，渲染器若要按引用摘除就得在注册时缓存一份。
/// 该记账由本类持有，渲染器只需给出自身与回调
/// </summary>
internal static class RenderHook
{
    // 宿主 → 登记时的委托实例；后者是摘除时唯一的比对依据
    private static readonly List<KeyValuePair<object, Action>> installed = [];

    /// <summary>登记擦行钩子。同一宿主重复登记时先摘掉上一份</summary>
    internal static void Install(object host, Action clearLine)
    {
        Uninstall(host);
        ConsoleHost.BeforeWrite = clearLine;
        installed.Add(new(host, clearLine));
    }

    /// <summary>摘除该宿主登记的钩子；槽位已被后注册者接管时不动</summary>
    internal static void Uninstall(object host)
    {
        for (var i = installed.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(installed[i].Key, host))
            {
                continue;
            }

            var owns = ReferenceEquals(ConsoleHost.BeforeWrite, installed[i].Value);
            installed.RemoveAt(i);
            if (owns)
            {
                ConsoleHost.BeforeWrite = null;
            }
        }
    }
}
