#pragma warning disable CA1032 // code 是必填参数：无 code 的构造无从构造本类型

using System;

namespace BBDown.Core.Util;

/// <summary>
/// B 站接口的业务错误：<c>code != 0</c>，或 <c>code == 0</c> 但 <c>data</c> 缺失 / 为 null
/// 派生自 <see cref="InvalidOperationException"/>，上层的宽泛捕获仍照旧生效
/// <see cref="Code"/> 为接口外层 code；<c>0</c> 表示服务端报成功却没给 data
/// </summary>
public sealed class ApiException : InvalidOperationException
{
    public ApiException(int code, string message) : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}

