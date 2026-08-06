using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using static BBDown.Core.Logger;

namespace BBDown.Core;

public static class DanmakuUtil
{
    private const int MONITOR_WIDTH = 1920;         //渲染字幕时的渲染范围的高度
    private const int MONITOR_HEIGHT = 1080;        //渲染字幕时的渲染范围的高度
    private const int FONT_SIZE = 40;               //字体大小
    private const double MOVE_SPEND_TIME = 8.00;    //单条条滚动弹幕存在时间（控制速度）
    private const double TOP_SPEND_TIME = 4.00;     //单条顶部或底部弹幕存在时间
    private const int PROTECT_LENGTH = 50;          //滚动弹幕屏占百分比
    public static readonly DanmakuComparer comparer = new( );

    public static DanmakuItem[]? ParseXml(string xmlPath)
    {
        // 解析xml文件
        XmlDocument xmlFile = new( );
        XmlReaderSettings settings = new( )
        {
            IgnoreComments = true//忽略文档里面的注释
        };
        List<DanmakuItem> danmakus = [];
        using (var reader = XmlReader.Create(xmlPath, settings))
        {
            try
            {
                xmlFile.Load(reader);
            }
            catch (Exception ex)
            {
                LogDebug("解析字幕xml时出现异常: {0}", ex.ToString( ));
                return null;
            }
        }

        var rootNode = xmlFile.SelectSingleNode("i");
        if (rootNode != null)
        {
            var rootElement = (XmlElement) rootNode;
            var dNodeList = rootElement.SelectNodes("d");
            if (dNodeList != null)
            {
                foreach (XmlNode node in dNodeList)
                {
                    var dElement = (XmlElement) node;
                    var attr = dElement.GetAttribute("p");
                    if (attr != null)
                    {
                        var vs = attr.Split(',');
                        if (vs.Length >= 8)
                        {
                            DanmakuItem danmaku = new(vs, dElement.InnerText);
                            danmakus.Add(danmaku);
                        }
                    }
                }
            }
        }

        return [.. danmakus];
    }

    /// <summary>
    /// 保存为ASS字幕文件
    /// </summary>
    /// <param name="danmakus">弹幕</param>
    /// <param name="outputPath">保存路径</param>
    /// <returns></returns>
    public static async Task SaveAsAssAsync(DanmakuItem[] danmakus, string outputPath, CancellationToken ct = default)
    {
        var sb = new StringBuilder( );
        // ASS字幕文件头
        sb.AppendLine("[Script Info]");
        sb.AppendLine("Script Updated By: BBDown(https://github.com/nilaoda/BBDown)");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {MONITOR_WIDTH}");
        sb.AppendLine($"PlayResY: {MONITOR_HEIGHT}");
        sb.AppendLine($"Aspect Ratio: {MONITOR_WIDTH}:{MONITOR_HEIGHT}");
        sb.AppendLine("Collisions: Normal");
        sb.AppendLine("WrapStyle: 2");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine("YCbCr Matrix: TV.601");
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: BBDOWN_Style, 黑体, {FONT_SIZE}, &H00FFFFFF, &H00FFFFFF, &H00000000, &H00000000, 0, 0, 0, 0, 100, 100, 0.00, 0.00, 1, 2, 0, 7, 0, 0, 0, 0");
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        PositionController controller = new( );   // 弹幕位置控制器
        // 排序应在副本上进行，避免就地改写调用方传入的数组（P1-26）
        var sorted = (DanmakuItem[]) danmakus.Clone( );
        Array.Sort(sorted, comparer);
        foreach (var danmaku in sorted)
        {
            var height = controller.UpdatePosition(danmaku.DanmakuMode, danmaku.Second, danmaku.Content.Length);
            if (height == -1)
            {
                continue;
            }

            var effect = "";
            effect += danmaku.DanmakuMode switch
            {
                3 => $"\\an8\\pos({MONITOR_WIDTH / 2}, {MONITOR_HEIGHT - FONT_SIZE - height})",
                2 => $"\\an8\\pos({MONITOR_WIDTH / 2}, {height})",
                _ => $"\\move({MONITOR_WIDTH}, {height}, {-danmaku.Content.Length * FONT_SIZE}, {height})",
            };
            if (danmaku.Color != "FFFFFF")
            {
                effect += $"\\c&H{ToAssColor(danmaku.Color)}&";
            }

            sb.AppendLine($"Dialogue: 2,{danmaku.StartTime},{danmaku.EndTime},BBDOWN_Style,,0000,0000,0000,,{{{effect}}}{danmaku.Content}");
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString( ), Encoding.UTF8, ct);
    }

    // B 站 XML 的颜色是整数 RGB，ASS 的 \c&H...& 却是 BGR 字节序，直接照搬会让红蓝对调（P0-5）
    internal static string ToAssColor(string rgbHex)
    {
        if (!int.TryParse(rgbHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return rgbHex;
        }

        var bgr = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
        return bgr.ToString("X6", CultureInfo.InvariantCulture);
    }

    internal class PositionController
    {
        private readonly int maxLine = MONITOR_HEIGHT * PROTECT_LENGTH / FONT_SIZE / 100;    //总行数
        // 三个位置的弹幕队列，记录弹幕结束时间

        private readonly List<double> moveQueue = [];
        private readonly List<double> topQueue = [];
        private readonly List<double> bottomQueue = [];

        public PositionController( )
        {
            for (var i = 0; i < maxLine; i++)
            {
                moveQueue.Add(0.00);
                topQueue.Add(0.00);
                bottomQueue.Add(0.00);
            }
        }

        public int UpdatePosition(int type, double time, int length)
        {
            // 获取可用位置
            List<double> vs;
            var displayTime = TOP_SPEND_TIME;
            if (type == POS_BOTTOM)
            {
                vs = bottomQueue;
            }
            else if (type == POS_TOP)
            {
                vs = topQueue;
            }
            else
            {
                vs = moveQueue;
                displayTime = MOVE_SPEND_TIME * (length + 5) * FONT_SIZE / (MONITOR_WIDTH + length * MOVE_SPEND_TIME);
            }

            for (var i = 0; i < maxLine; i++)
            {
                if (time >= vs[i])
                {   // 此条弹幕已结束，更新该位置信息
                    vs[i] = time + displayTime;
                    return i * FONT_SIZE;
                }
            }

            return -1;
        }
    }

    public class DanmakuItem
    {
        public DanmakuItem(string[] attrs, string content)
        {
            DanmakuMode = attrs[1] switch
            {
                "4" => POS_BOTTOM,
                "5" => POS_TOP,
                _ => POS_MOVE,
            };
            try
            {
                var second = double.Parse(attrs[0], CultureInfo.InvariantCulture);
                Second = second;
                StartTime = ComputeTime(second);
                EndTime = ComputeTime(second + (DanmakuMode == 1 ? MOVE_SPEND_TIME : TOP_SPEND_TIME));
            }
            catch (Exception e)
            {
                Log(e.Message);
            }

            FontSize = attrs[2];
            // TryParse 同时兜住 FormatException 与 OverflowException（颜色值可能超出 int 范围）
            if (int.TryParse(attrs[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var colorD))
            {
                Color = (colorD & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
            }
            else
            {
                Log($"无效的弹幕颜色值：{attrs[3]}");
            }

            Timestamp = attrs[4];
            Content = content;
        }
        internal static string ComputeTime(double second)
        {
            // 先量化到厘秒再拆分，否则末尾格式化的进位会溢出成 0:59:60.00 这种非法时间戳
            var total = (long) Math.Round(second * 100, MidpointRounding.AwayFromZero);
            if (total < 0)
            {
                total = 0;
            }

            return $"{total / 360000}:{total % 360000 / 6000:D2}:{total % 6000 / 100.0:00.00}";
        }
        public string Content { get; set; } = "";
        // 弹幕内容
        public string StartTime { get; set; } = "";
        // 出现时间
        public double Second { get; set; }
        // 出现时间（秒为单位）
        public string EndTime { get; set; } = "";
        // 消失时间
        public int DanmakuMode { get; set; } = POS_MOVE;
        // 弹幕类型
        public string FontSize { get; set; } = "";
        // 字号
        public string Color { get; set; } = "FFFFFF";
        // 颜色
        public string Timestamp { get; set; } = "";
        // 时间戳
    }

    public class DanmakuComparer : IComparer<DanmakuItem>
    {
        public int Compare(DanmakuItem? x, DanmakuItem? y)
        {
            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            return x.Second.CompareTo(y.Second);
        }
    }

    private const int POS_MOVE = 1;     //滚动弹幕
    private const int POS_TOP = 2;      //顶部弹幕
    private const int POS_BOTTOM = 3;   //底部弹幕
}
