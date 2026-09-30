using System;

// net10.0 的 BCL 尚未内置 closed 类型标记，编译器按特性绑定查找该 attribute；
// 升级到内置此类型的 .NET 版本后可删除
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class IsClosedTypeAttribute : Attribute
{
    public IsClosedTypeAttribute( )
    {
    }
}
