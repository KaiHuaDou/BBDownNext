using System;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Util;

namespace BBDown.GUI;

/// <summary>「留空即有自动值」的文本框以 PlaceholderText 预展示自动值：探测 / 推导类动态生成，默认类为静态文案。</summary>
public partial class MainWindow
{
    /// <summary>PlaceholderText 与 Text 相互独立，只反映当前进程的自动值，配置往返与重置选项无需重跑。</summary>
    private void ApplyPlaceholderTexts( )
    {
        var ffmpeg = Utils.FindExecutable("ffmpeg");
        var mp4box = Utils.FindExecutable("mp4box", "MP4Box", "MP4box");
        var aria2c = Utils.FindExecutable("aria2c");
        FFmpegPathBox.PlaceholderText = ffmpeg ?? "未找到，混流时在 PATH 中查找";
        Mp4boxPathBox.PlaceholderText = mp4box ?? "未找到，混流时在 PATH 中查找";
        Aria2cPathBox.PlaceholderText = aria2c ?? "未找到，需手动指定";
        WorkDirBox.PlaceholderText = $"{Environment.CurrentDirectory}";
        UserAgentBox.PlaceholderText = $"{BiliHeaders.UserAgent}";
        FilePatternBox.PlaceholderText = $"{SavePath.SinglePageDefaultSavePath}";
        MultiFilePatternBox.PlaceholderText = $"{SavePath.MultiPageDefaultSavePath}";
        HostBox.PlaceholderText = BiliApi.MainHost;
        EpHostBox.PlaceholderText = BiliApi.MainHost;
        TvHostBox.PlaceholderText = BiliApi.TvHost;
        LangBox.PlaceholderText = "不写入语言标记";
        CookieBox.PlaceholderText = "使用 BBDown.data 中的 WEB 登录态";
        AccessTokenBox.PlaceholderText = "使用已保存的 access_token";
        Aria2cArgsBox.PlaceholderText = "-x16 -s16 -j16 -k5M";
        PagesBox.PlaceholderText = "下载全部（链接含集数时自动选中）";
        AreaBox.PlaceholderText = "不启用";
        UposHostBox.PlaceholderText = "不替换";
        PostProcessPathBox.PlaceholderText = "不启用";
    }
}
