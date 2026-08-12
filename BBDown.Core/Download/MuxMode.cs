namespace BBDown.Core.Download;

/// <summary>混流方式（--mux / -m）。取代 --skip-mux 与 --mp4box 两个布尔开关。</summary>
public enum MuxMode
{
    /// <summary>不混流，保留已下载的裸轨。</summary>
    None = 0,
    /// <summary>FFmpeg 混流为 MP4（默认）。</summary>
    Mpeg4 = 1,
    /// <summary>MP4Box 混流。</summary>
    Mp4box = 2,
    /// <summary>FFmpeg 混流为 Matroska</summary>
    Mkv = 3,
}

/// <summary>混流方式名解析，CLI 与 serve 共用，忽略大小写。</summary>
public static class MuxModeUtil
{
    /// <summary>解析混流方式名，忽略大小写；未知值返回 null，由调用方决定报错或回落。</summary>
    public static MuxMode? TryParse(string? value)
    {
        return value?.Trim( ).ToLowerInvariant( ) switch
        {
            "none" => MuxMode.None,
            "mpeg4" => MuxMode.Mpeg4,
            "mp4box" => MuxMode.Mp4box,
            "mkv" => MuxMode.Mkv,
            _ => null,
        };
    }
}
