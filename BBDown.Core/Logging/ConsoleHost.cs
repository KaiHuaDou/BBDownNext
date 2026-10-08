using System;
using System.Threading;

namespace BBDown.Core.Logging;

/// <summary>
/// 控制台展示基础设施：渲染器在写日志前调用 <see cref="BeforeWrite"/>（擦除活动状态行）
/// 绘制者（进度条 / 直播状态行）设置它。仅 CLI 宿主消费，GUI / serve 无控制台渲染时不消费
/// </summary>
public static class ConsoleHost
{
    public static Action? BeforeWrite { get; set; }

    // 全部控制台写入（日志正文 / 进度条帧 / 状态行帧）统一拿这把锁互斥
    // 单向锁序：在本锁内执行的擦行回调（BeforeWrite）不得等待任何其它锁，回调实现只访问本锁保护的状态
    // 进度条 / 状态行的帧文本先在各自 gate 内算好，释放后再进本锁落写。双向取锁即 AB-BA 死锁
    public static readonly Lock WriteGate = new( );
}
