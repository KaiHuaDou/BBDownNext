#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;

using BBDown.Core.Download;

namespace BBDown.GUI;

/// <summary>
/// 控件可用性联动：界面「可用」的选项与「实际生效」的选项保持同步
/// 单点刷新，任何相关事件只调 <see cref="RefreshAvailability"/>；禁用不清空值，ReadOptions 照读、Core 自然失效
/// </summary>
public partial class MainWindow
{
    private void RefreshAvailability( )
    {
        var info = UrlDetector.Describe(TargetBox.Text);
        var mode = info is null ? null : ModeOf(info.Kind);
        var content = ContentSelector.FromNormalizedString(ReadContent( ));
        var infoOnly = InfoOnlyCheckBox.IsChecked == true;
        var comments = (int)(CommentsCountBox.Value ?? 0);

        ApplyContentArea(mode, infoOnly, comments);
        ApplyDownloadArea(mode, infoOnly, ReadMux( ));
        ApplyEnvironmentArea(mode, infoOnly, UseAria2cCheckBox.IsChecked == true, ReadMux( ));
        ApplyParserArea(mode);
        ApplyContentWarn(info is not null, mode, content, comments, infoOnly);
    }

    private static ContentMode? ModeOf(TargetKind kind)
    {
        return kind switch
        {
            TargetKind.Video or TargetKind.Pgc => ContentMode.Video,
            TargetKind.Opus => ContentMode.Opus,
            TargetKind.Audio => ContentMode.Audio,
            TargetKind.Live => ContentMode.Live,
            TargetKind.Mixed => ContentMode.Mixed,
            _ => null,
        };
    }

    // Video 域含未识别目标（不联动）与空间动态（视频部分跟随内容勾选）
    private static bool VideoLike(ContentMode? mode)
    {
        return mode is null or ContentMode.Video or ContentMode.Mixed;
    }

    // 评论选项对视频域与专栏 / 图文域都生效；其余选项维持各自域
    private static bool Commentable(ContentMode? mode)
    {
        return VideoLike(mode) || mode is ContentMode.Opus;
    }

    private static bool HasBothAv(DownloadContent content)
    {
        return (content & (DownloadContent.Audio | DownloadContent.Video)) == (DownloadContent.Audio | DownloadContent.Video);
    }

    // 弹幕 / 评论的格式与条数是输出选项，不因内容项未勾选而禁用（缺失组合由警告行提示，与 Core 的警告规则一致）
    // 评论条数为 0 时不下载评论，排序与格式随之失效
    private void ApplyContentArea(ContentMode? mode, bool infoOnly, int comments)
    {
        var videoLike = VideoLike(mode);
        var commentRow = Commentable(mode) && comments > 0;

        ContentGrid.IsEnabled = !infoOnly && mode is not ContentMode.Live;
        foreach (var item in ContentItems.Items.Cast<ContentOption>( ))
        {
            item.IsEnabled = mode switch
            {
                null => true,
                ContentMode.Video => item.Key is not ('i' or 'A' or 'M'),
                ContentMode.Mixed => true,
                ContentMode.Opus => item.Key is 'i' or 'A' or 'M' or 'o' or 'O',
                ContentMode.Audio => item.Key is 'a',
                _ => false,
            };
        }

        PagesBox.IsEnabled = videoLike;
        InteractivePagesCheckBox.IsEnabled = videoLike;
        ShowAllCheckBox.IsEnabled = videoLike;
        AllowPreviewCheckBox.IsEnabled = videoLike;
        CommentsCountBox.IsEnabled = Commentable(mode);
        SortHotRadioButton.IsEnabled = commentRow;
        SortTimeRadioButton.IsEnabled = commentRow;
        CommentFormatPanel.IsEnabled = !infoOnly;
        CommentJsonCheckBox.IsEnabled = commentRow;
        CommentTxtCheckBox.IsEnabled = commentRow;
        DanmakuFormatPanel.IsEnabled = !infoOnly;
        DanmakuXmlCheckBox.IsEnabled = videoLike;
        DanmakuAssCheckBox.IsEnabled = videoLike;
    }

    // 混流方式 / 混流音频语言 / 后处理是输出配置，不随内容勾选闪烁（缺 a / v 的组合由警告行提示）
    // 可用性只随三个确定性输入变化：目标域、混流方式、仅解析
    private void ApplyDownloadArea(ContentMode? mode, bool infoOnly, string mux)
    {
        var videoLike = VideoLike(mode);

        SingleThreadCheckBox.IsEnabled = videoLike && !infoOnly;
        SaveRecordsCheckBox.IsEnabled = videoLike && !infoOnly;
        StopOnErrorCheckBox.IsEnabled = videoLike && !infoOnly;
        HideStreamsCheckBox.IsEnabled = videoLike && !infoOnly;
        EncodingFirstCheckBox.IsEnabled = videoLike && !infoOnly;
        AllowPcdnCheckBox.IsEnabled = videoLike && !infoOnly;
        NoForceHostCheckBox.IsEnabled = videoLike && !infoOnly;
        NoForceHttpCheckBox.IsEnabled = videoLike && !infoOnly;
        DebugCheckBox.IsEnabled = !infoOnly;
        DelayPerPageBox.IsEnabled = videoLike && !infoOnly;
        MaxRetryBox.IsEnabled = videoLike && !infoOnly;
        MultiFilePatternBox.IsEnabled = videoLike && !infoOnly;
        MuxBox.IsEnabled = videoLike && !infoOnly;
        LangBox.IsEnabled = videoLike && mux != "none" && !infoOnly;
        PostProcessPathBox.IsEnabled = videoLike && !infoOnly;
        BrowsePostProcessButton.IsEnabled = videoLike && !infoOnly;
        FilePatternBox.IsEnabled = mode is not ContentMode.Live && !infoOnly;
        NamingVariablesExpander.IsEnabled = mode is not ContentMode.Live && !infoOnly;
    }

    private void ApplyEnvironmentArea(ContentMode? mode, bool infoOnly, bool useAria, string mux)
    {
        var aria = mode is not ContentMode.Live && !infoOnly;
        var ffmpeg = mode is not (ContentMode.Opus or ContentMode.Audio);
        var mp4box = VideoLike(mode) && mux == "mp4box";

        UseAria2cCheckBox.IsEnabled = aria;
        Aria2cPathBox.IsEnabled = aria && useAria;
        BrowseAria2cButton.IsEnabled = aria && useAria;
        Aria2cArgsBox.IsEnabled = aria && useAria;
        FFmpegPathBox.IsEnabled = ffmpeg;
        BrowseFfmpegButton.IsEnabled = ffmpeg;
        Mp4boxPathBox.IsEnabled = mp4box;
        BrowseMp4boxButton.IsEnabled = mp4box;
    }

    private void ApplyParserArea(ContentMode? mode)
    {
        var videoLike = VideoLike(mode);

        VideoAscendingCheckBox.IsEnabled = videoLike;
        AudioAscendingCheckBox.IsEnabled = videoLike;
        InteractiveQualityCheckBox.IsEnabled = videoLike;
        ApiWebRadioButton.IsEnabled = videoLike;
        ApiTvRadioButton.IsEnabled = videoLike;
        ApiAppRadioButton.IsEnabled = videoLike;
        ApiIntlRadioButton.IsEnabled = videoLike;
        EncodingPriorityPicker.IsEnabled = videoLike;
        DfnPriorityPicker.IsEnabled = videoLike;
        AudioQualityPicker.IsEnabled = videoLike;
        LiveQualityBox.IsEnabled = mode is null or ContentMode.Live;
    }

    private void ApplyContentWarn(bool hasTarget, ContentMode? mode, DownloadContent content, int comments, bool infoOnly)
    {
        var warnings = new StringBuilder( );
        if (hasTarget && VideoLike(mode) && !infoOnly)
        {
            if ((content & (DownloadContent.MuxCover | DownloadContent.MuxMetadata)) != 0 && !HasBothAv(content))
            {
                AppendWarn(warnings, "封面嵌入（C）与嵌入元数据（m）需要同时勾选音频与视频才会生效");
            }

            if ((content & DownloadContent.Danmaku) == 0
                && (DanmakuXmlCheckBox.IsChecked == true || DanmakuAssCheckBox.IsChecked == true))
            {
                AppendWarn(warnings, "已选择弹幕格式，但内容未勾选弹幕（d），弹幕不会下载");
            }
        }

        if (hasTarget && Commentable(mode) && !infoOnly
            && comments > 0 && (content & (DownloadContent.Comments | DownloadContent.FullComments)) == 0)
        {
            AppendWarn(warnings, "已设置评论条数，但内容未勾选评论（o / O），评论不会下载");
        }

        ContentWarnText.Text = warnings.ToString( );
        ContentWarnText.IsVisible = warnings.Length > 0;
        ContentWarnText.Foreground = hintBrush;
    }

    private static void AppendWarn(StringBuilder builder, string text)
    {
        if (builder.Length > 0)
        {
            builder.Append('；');
        }

        builder.Append(text);
    }

    private void ContentItemCheckedChanged(object? o, RoutedEventArgs e)
    {
        RefreshAvailability( );
    }

    private void CommentsCountBoxValueChanged(object? o, RoutedEventArgs e)
    {
        RefreshAvailability( );
    }

    private void MuxBoxSelectionChanged(object? o, SelectionChangedEventArgs e)
    {
        RefreshAvailability( );
    }

    private void UseAria2cCheckBoxCheckedChanged(object? o, RoutedEventArgs e)
    {
        RefreshAvailability( );
    }
}
