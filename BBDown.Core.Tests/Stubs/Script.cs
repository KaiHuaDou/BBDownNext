using System;
using System.Threading;

namespace BBDown.Core.Tests;

/// <summary>把「第 N 次调用返回什么」写成脚本，越界后重复最后一项。</summary>
internal sealed class Script<T>(params T[] items)
{
    private int calls;

    public T Next( )
    {
        var i = Interlocked.Increment(ref calls) - 1;
        return items[Math.Min(i, items.Length - 1)];
    }
}
