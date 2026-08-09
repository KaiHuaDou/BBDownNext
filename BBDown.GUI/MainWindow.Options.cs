using System;
using System.Text;
using System.Windows.Controls;

namespace BBDown.GUI;

/// <summary>面板控件与 TaskParams 之间的映射，按 §3 控件组拆分为 partial，控制 MainWindow.xaml.cs 行数。</summary>
public partial class MainWindow
{
    private static readonly (string Value, string Label)[] MuxChoices =
    [
        ("mpeg4", "FFmpeg 混流为 MP4（默认）"),
        ("mp4box", "MP4Box 混流"),
        ("mkv", "FFmpeg 混流为 Matroska"),
        ("none", "不混流（保留裸轨）"),
    ];

    private static readonly (string Value, string Label)[] LiveQualityChoices =
    [
        ("10000", "10000 原画"),
        ("400", "400 蓝光"),
        ("250", "250 超清"),
        ("150", "150 高清"),
        ("80", "80 流畅"),
    ];

    private TaskParams ReadOptions( )
    {
        return new TaskParams
        {
            Content = ReadContent( ),
            UseAria2c = UseAria2cCheckBox.IsChecked == true,
            SingleThread = SingleThreadCheckBox.IsChecked == true,
            InfoOnly = InfoOnlyCheckBox.IsChecked == true,
            ShowAll = ShowAllCheckBox.IsChecked == true,
            AllowPreview = AllowPreviewCheckBox.IsChecked == true,
            SaveRecords = SaveRecordsCheckBox.IsChecked == true,
            StopOnError = StopOnErrorCheckBox.IsChecked == true,
            Debug = DebugCheckBox.IsChecked == true,
            VideoAscending = VideoAscendingCheckBox.IsChecked == true,
            AudioAscending = AudioAscendingCheckBox.IsChecked == true,
            AllowPcdn = AllowPcdnCheckBox.IsChecked == true,
            NoForceHost = NoForceHostCheckBox.IsChecked == true,
            NoForceHttp = NoForceHttpCheckBox.IsChecked == true,
            Mux = ReadMux( ),
            EncodingPriority = EncodingPriorityBox.Text.Trim( ),
            DfnPriority = DfnPriorityBox.Text.Trim( ),
            Pages = PagesBox.Text.Trim( ),
            DanmakuFormats = ReadDanmakuFormats( ),
            CommentsCount = CommentsCountBox.Text.Trim( ),
            CommentsSort = ReadCommentsSort( ),
            CommentsFormats = ReadCommentsFormats( ),
            Lang = LangBox.Text.Trim( ),
            Cookie = CookieBox.Text.Trim( ),
            AccessToken = AccessTokenBox.Text.Trim( ),
            UserAgent = UserAgentBox.Text.Trim( ),
            WorkDir = WorkDirBox.Text.Trim( ),
            FFmpegPath = FFmpegPathBox.Text.Trim( ),
            Mp4boxPath = Mp4boxPathBox.Text.Trim( ),
            Aria2cPath = Aria2cPathBox.Text.Trim( ),
            Aria2cArgs = Aria2cArgsBox.Text.Trim( ),
            DelayPerPage = DelayPerPageBox.Text.Trim( ),
            LiveQuality = ReadLiveQuality( ),
            Api = ApiBox.SelectedItem as string ?? "web",
            FilePattern = FilePatternBox.Text.Trim( ),
            MultiFilePattern = MultiFilePatternBox.Text.Trim( ),
            DrmKey = DrmKeyBox.Text.Trim( ),
            Host = HostBox.Text.Trim( ),
            EpHost = EpHostBox.Text.Trim( ),
            TvHost = TvHostBox.Text.Trim( ),
            Area = AreaBox.Text.Trim( ),
            UposHost = UposHostBox.Text.Trim( ),
        };
    }

    private string ReadDanmakuFormats( )
    {
        StringBuilder builder = new( );
        if (DanmakuXmlCheckBox.IsChecked == true)
        {
            builder.Append("xml");
        }

        if (DanmakuAssCheckBox.IsChecked == true)
        {
            builder.Append(builder.Length > 0 ? ",ass" : "ass");
        }

        return builder.ToString( );
    }

    private string ReadCommentsFormats( )
    {
        StringBuilder builder = new( );
        if (CommentJsonCheckBox.IsChecked == true)
        {
            builder.Append("json");
        }

        if (CommentTxtCheckBox.IsChecked == true)
        {
            builder.Append(builder.Length > 0 ? ",txt" : "txt");
        }

        return builder.ToString( );
    }

    private string ReadCommentsSort( )
    {
        return SortTimeRadioButton.IsChecked == true ? "time" : "hot";
    }

    private string ReadMux( )
    {
        return (MuxBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "mpeg4";
    }

    private string ReadLiveQuality( )
    {
        return (LiveQualityBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "10000";
    }

    private string ReadContent( )
    {
        StringBuilder builder = new( );
        if (AudioCheckBox.IsChecked == true)
        {
            builder.Append('a');
        }

        if (VideoCheckBox.IsChecked == true)
        {
            builder.Append('v');
        }

        if (CoverFileCheckBox.IsChecked == true)
        {
            builder.Append('c');
        }

        if (CoverMuxCheckBox.IsChecked == true)
        {
            builder.Append('C');
        }

        if (DanmakuCheckBox.IsChecked == true)
        {
            builder.Append('d');
        }

        if (ArticleImageCheckBox.IsChecked == true)
        {
            builder.Append('i');
        }

        if (MetadataCheckBox.IsChecked == true)
        {
            builder.Append('m');
        }

        if (YamlCheckBox.IsChecked == true)
        {
            builder.Append('M');
        }

        if (CommentCheckBox.IsChecked == true)
        {
            builder.Append('o');
        }

        if (AllCommentCheckBox.IsChecked == true)
        {
            builder.Append('O');
        }

        if (AiSubtitleCheckBox.IsChecked == true)
        {
            builder.Append('S');
        }

        if (SubtitleCheckBox.IsChecked == true)
        {
            builder.Append('s');
        }

        return builder.ToString( );
    }

    private void ApplyOptions(TaskParams options)
    {
        ApplyContent(options.Content);
        UseAria2cCheckBox.IsChecked = options.UseAria2c;
        SingleThreadCheckBox.IsChecked = options.SingleThread;
        InfoOnlyCheckBox.IsChecked = options.InfoOnly;
        ShowAllCheckBox.IsChecked = options.ShowAll;
        AllowPreviewCheckBox.IsChecked = options.AllowPreview;
        SaveRecordsCheckBox.IsChecked = options.SaveRecords;
        StopOnErrorCheckBox.IsChecked = options.StopOnError;
        DebugCheckBox.IsChecked = options.Debug;
        VideoAscendingCheckBox.IsChecked = options.VideoAscending;
        AudioAscendingCheckBox.IsChecked = options.AudioAscending;
        AllowPcdnCheckBox.IsChecked = options.AllowPcdn;
        NoForceHostCheckBox.IsChecked = options.NoForceHost;
        NoForceHttpCheckBox.IsChecked = options.NoForceHttp;
        ApplyMux(options.Mux);
        EncodingPriorityBox.Text = options.EncodingPriority;
        DfnPriorityBox.Text = options.DfnPriority;
        PagesBox.Text = options.Pages;
        ApplyDanmakuFormats(options.DanmakuFormats);
        CommentsCountBox.Text = options.CommentsCount;
        ApplyCommentsSort(options.CommentsSort);
        ApplyCommentsFormats(options.CommentsFormats);
        LangBox.Text = options.Lang;
        CookieBox.Text = options.Cookie;
        AccessTokenBox.Text = options.AccessToken;
        UserAgentBox.Text = options.UserAgent;
        WorkDirBox.Text = options.WorkDir;
        FFmpegPathBox.Text = options.FFmpegPath;
        Mp4boxPathBox.Text = options.Mp4boxPath;
        Aria2cPathBox.Text = options.Aria2cPath;
        Aria2cArgsBox.Text = options.Aria2cArgs;
        DelayPerPageBox.Text = options.DelayPerPage;
        ApplyLiveQuality(options.LiveQuality);
        ApiBox.SelectedItem = options.Api;
        FilePatternBox.Text = options.FilePattern;
        MultiFilePatternBox.Text = options.MultiFilePattern;
        DrmKeyBox.Text = options.DrmKey;
        HostBox.Text = options.Host;
        EpHostBox.Text = options.EpHost;
        TvHostBox.Text = options.TvHost;
        AreaBox.Text = options.Area;
        UposHostBox.Text = options.UposHost;
    }

    private void ApplyDanmakuFormats(string formats)
    {
        DanmakuXmlCheckBox.IsChecked = formats.Contains("xml", StringComparison.Ordinal);
        DanmakuAssCheckBox.IsChecked = formats.Contains("ass", StringComparison.Ordinal);
    }

    private void ApplyCommentsFormats(string formats)
    {
        CommentJsonCheckBox.IsChecked = formats.Contains("json", StringComparison.Ordinal);
        CommentTxtCheckBox.IsChecked = formats.Contains("txt", StringComparison.Ordinal);
    }

    private void ApplyCommentsSort(string sort)
    {
        SortHotRadioButton.IsChecked = sort == "hot";
        SortTimeRadioButton.IsChecked = sort == "time";
    }

    private void ApplyMux(string mux)
    {
        foreach (ComboBoxItem item in MuxBox.Items)
        {
            if ((item.Tag as string) == mux)
            {
                MuxBox.SelectedItem = item;
                return;
            }
        }

        MuxBox.SelectedIndex = 0;
    }

    private void ApplyLiveQuality(string quality)
    {
        foreach (ComboBoxItem item in LiveQualityBox.Items)
        {
            if ((item.Tag as string) == quality)
            {
                LiveQualityBox.SelectedItem = item;
                return;
            }
        }

        LiveQualityBox.SelectedIndex = 0;
    }

    private void ApplyContent(string content)
    {
        AudioCheckBox.IsChecked = content.Contains('a');
        VideoCheckBox.IsChecked = content.Contains('v');
        CoverFileCheckBox.IsChecked = content.Contains('c');
        CoverMuxCheckBox.IsChecked = content.Contains('C');
        DanmakuCheckBox.IsChecked = content.Contains('d');
        ArticleImageCheckBox.IsChecked = content.Contains('i');
        MetadataCheckBox.IsChecked = content.Contains('m');
        YamlCheckBox.IsChecked = content.Contains('M');
        CommentCheckBox.IsChecked = content.Contains('o');
        AllCommentCheckBox.IsChecked = content.Contains('O');
        AiSubtitleCheckBox.IsChecked = content.Contains('S');
        SubtitleCheckBox.IsChecked = content.Contains('s');
    }
}
