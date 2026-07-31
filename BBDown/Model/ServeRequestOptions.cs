using BBDown;
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


internal class ServeRequestOptions : MyOption
{

    /// <summary>
    /// 任务完成回调Http请求地址
    /// </summary>
    public string? CallBackWebHook { get; set; }
}