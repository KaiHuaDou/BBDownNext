#pragma warning disable CA1308, CS8600, CS8602 // CA1308：格式名取枚举名小写，与 Core 解析器共用同一来源；CS8600/CS8602：Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using BBDown.Core.Download;

namespace BBDown.GUI;

/// <summary>内容复选项数据：字符键 + 显示名来自 ContentSelector.Order 单一来源；IsChecked 为 UI 勾选态，变化时通知绑定，使「重置选项」与载入配置能刷新复选框。</summary>
public sealed record ContentOption(char Key, string Label) : INotifyPropertyChanged
{
    private bool isChecked;

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
            {
                return;
            }

            isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>面板控件与 TaskParams 之间的映射，按 §3 控件组拆分为 partial，控制 MainWindow.xaml.cs 行数。</summary>
public partial class MainWindow
{
    private static readonly (string Value, string Label)[] MuxChoices =
    [
        ("mpeg4", "FFmpeg 混流为 MPEG4"),
        ("mp4box", "MP4Box 混流"),
        ("mkv", "FFmpeg 混流为 Matroska"),
        ("none", "不混流（保留裸轨）"),
    ];

    // 弹幕/评论格式名取枚举名小写，与 Core 解析器共用同一来源
    private static string FormatName<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        return value.ToString( ).ToLowerInvariant( );
    }

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
            UserAgent = UserAgentBox.Text.Trim( ),
            WorkDir = WorkDirBox.Text.Trim( ),
            FFmpegPath = FFmpegPathBox.Text.Trim( ),
            Mp4boxPath = Mp4boxPathBox.Text.Trim( ),
            Aria2cPath = Aria2cPathBox.Text.Trim( ),
            PostProcessPath = PostProcessPathBox.Text.Trim( ),
            Aria2cArgs = Aria2cArgsBox.Text.Trim( ),
            DelayPerPage = DelayPerPageBox.Text.Trim( ),
            LiveQuality = ReadLiveQuality( ),
            Api = ApiBox.SelectedItem as string ?? "web",
            FilePattern = FilePatternBox.Text.Trim( ),
            MultiFilePattern = MultiFilePatternBox.Text.Trim( ),
            Host = HostBox.Text.Trim( ),
            EpHost = EpHostBox.Text.Trim( ),
            TvHost = TvHostBox.Text.Trim( ),
            Area = AreaBox.Text.Trim( ),
            UposHost = UposHostBox.Text.Trim( ),
        };
    }

    private string ReadDanmakuFormats( )
    {
        var builder = new StringBuilder( );
        if (DanmakuXmlCheckBox.IsChecked == true)
        {
            builder.Append(FormatName(DanmakuFormat.Xml));
        }

        if (DanmakuAssCheckBox.IsChecked == true)
        {
            builder.Append(builder.Length > 0 ? "," : "");
            builder.Append(FormatName(DanmakuFormat.Ass));
        }

        return builder.ToString( );
    }

    private string ReadCommentsFormats( )
    {
        var builder = new StringBuilder( );
        if (CommentJsonCheckBox.IsChecked == true)
        {
            builder.Append(FormatName(CommentFormat.Json));
        }

        if (CommentTxtCheckBox.IsChecked == true)
        {
            builder.Append(builder.Length > 0 ? "," : "");
            builder.Append(FormatName(CommentFormat.Txt));
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
        var builder = new StringBuilder( );
        foreach (ContentOption item in ContentItems.Items)
        {
            if (item.IsChecked)
            {
                builder.Append(item.Key);
            }
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
        UserAgentBox.Text = options.UserAgent;
        WorkDirBox.Text = options.WorkDir;
        FFmpegPathBox.Text = options.FFmpegPath;
        Mp4boxPathBox.Text = options.Mp4boxPath;
        Aria2cPathBox.Text = options.Aria2cPath;
        PostProcessPathBox.Text = options.PostProcessPath;
        Aria2cArgsBox.Text = options.Aria2cArgs;
        DelayPerPageBox.Text = options.DelayPerPage;
        ApplyLiveQuality(options.LiveQuality);
        ApiBox.SelectedItem = options.Api;
        FilePatternBox.Text = options.FilePattern;
        MultiFilePatternBox.Text = options.MultiFilePattern;
        HostBox.Text = options.Host;
        EpHostBox.Text = options.EpHost;
        TvHostBox.Text = options.TvHost;
        AreaBox.Text = options.Area;
        UposHostBox.Text = options.UposHost;
    }

    private void ApplyDanmakuFormats(string formats)
    {
        DanmakuXmlCheckBox.IsChecked = formats.Contains(FormatName(DanmakuFormat.Xml), StringComparison.Ordinal);
        DanmakuAssCheckBox.IsChecked = formats.Contains(FormatName(DanmakuFormat.Ass), StringComparison.Ordinal);
    }

    private void ApplyCommentsFormats(string formats)
    {
        CommentJsonCheckBox.IsChecked = formats.Contains(FormatName(CommentFormat.Json), StringComparison.Ordinal);
        CommentTxtCheckBox.IsChecked = formats.Contains(FormatName(CommentFormat.Txt), StringComparison.Ordinal);
    }

    private void ApplyCommentsSort(string sort)
    {
        SortHotRadioButton.IsChecked = sort == "hot";
        SortTimeRadioButton.IsChecked = sort == "time";
    }

    private void ApplyMux(string mux)
    {
        foreach (var item in MuxBox.Items.Cast<ComboBoxItem?>( ))
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
        foreach (var item in LiveQualityBox.Items.Cast<ComboBoxItem?>( ))
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
        foreach (ContentOption item in ContentItems.Items)
        {
            item.IsChecked = content.Contains(item.Key, StringComparison.Ordinal);
        }
    }

    /// <summary>仅解析不下载时禁用下载内容相关复选框（含弹幕/评论格式），避免无效选项误导。</summary>
    private void InfoOnlyCheckBoxChanged(object? o, RoutedEventArgs e)
    {
        var enabled = InfoOnlyCheckBox.IsChecked != true;
        ContentGrid.IsEnabled = enabled;
        DanmakuFormatPanel.IsEnabled = enabled;
        CommentFormatPanel.IsEnabled = enabled;
    }

    private void DebugCheckBoxChecked(object? o, RoutedEventArgs e)
    {
        LogExpander.IsExpanded = true;
    }

    private async void BrowseDirButtonClicked(object? o, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择工作目录",
            SuggestedStartLocation = await TrySuggestedLocation(topLevel, WorkDirBox.Text.Trim( )),
        });
        if (folders.Count > 0)
        {
            WorkDirBox.Text = folders[0].Path.LocalPath;
        }
    }

    /// <summary>ffmpeg / mp4box / aria2c 路径选择，按按钮 Tag 区分目标框。</summary>
    private async void BrowseFileButtonClicked(object? o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: string target })
        {
            return;
        }

        var box = target switch
        {
            "ffmpeg" => FFmpegPathBox,
            "mp4box" => Mp4boxPathBox,
            "aria2c" => Aria2cPathBox,
            "postprocess" => PostProcessPathBox,
            _ => null,
        };
        if (box is null || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择可执行文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("可执行文件") { Patterns = ["*.exe"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] },
            ],
        });
        if (files.Count > 0)
        {
            box.Text = files[0].Path.LocalPath;
        }
    }

    private static async Task<IStorageFolder?> TrySuggestedLocation(TopLevel topLevel, string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        return await topLevel.StorageProvider.TryGetFolderFromPathAsync(path);
    }
}
