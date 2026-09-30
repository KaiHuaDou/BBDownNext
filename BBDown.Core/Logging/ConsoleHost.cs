using System;
using System.Threading;

namespace BBDown.Core.Logging;

/// <summary>
/// 控制台展示基础设施：渲染器在写日志前调用 <see cref="BeforeWrite"/>（擦除活动状态行），
/// 绘制者（进度条 / 直播状态行）设置它。仅 CLI 宿主消费，GUI / serve 无控制台渲染时不消费。
/// </summary>
public static class ConsoleHost
{
    public static Action? BeforeWrite { get; set; }

    // 全部控制台写入（日志正文 / 进度条帧 / 状态行帧）统一拿这把锁互斥：
    // 擦行（BeforeWrite）与写正文分属两个线程时，无共同锁会让进度条帧插进日志正文中间造成错位。
    // 锁序约束：各方先拿自己的 gate 再拿本锁（System.Threading.Lock 可重入，擦行回调链上重复进入无害）
    public static readonly Lock WriteGate = new( );
}
