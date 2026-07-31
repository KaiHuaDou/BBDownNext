using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core;

/// <summary>
/// 一次运行的不可变配置快照。
/// 原 <see cref="Config"/> 的 7 个请求作用域字段（Cookie/Token/Host/EpHost/TvHost/Area/Wbi）
/// 收口到此只读值对象，由 GetVideoInfoAsync 在配置最终就绪时构造一次，并显式向下透传，
/// 不再依赖静态可变全局。qualitys / DEBUG_LOG 等进程级只读项仍保留在 Config。
/// </summary>
public readonly record struct AppConfig(
    string Cookie,
    string Token,
    string Host,
    string EpHost,
    string TvHost,
    string Area,
    string Wbi)
{
    /// <summary>
    /// 空配置，用于登录等尚未持有凭据的独立命令（这些接口不需要 cookie）。
    /// </summary>
    public static readonly AppConfig Empty = new("", "", "api.bilibili.com", "api.bilibili.com", "api.snm0516.aisee.tv", "", "");
}
