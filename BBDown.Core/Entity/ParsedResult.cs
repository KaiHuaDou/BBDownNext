using System.Collections.Generic;


namespace BBDown.Core.Entity;

public class ParsedResult
{
    public string RawResponse { get; set; } = "";
    public List<Video> VideoTracks { get; set; } = [];
    public List<Audio> AudioTracks { get; set; } = [];
    public List<Audio> BackgroundAudioTracks { get; set; } = [];
    public List<AudioMaterialInfo> RoleAudioList { get; set; } = [];
    public List<ViewPoint> ExtraPoints { get; set; } = [];
    /// <summary>playurl 声明的时长（秒），0 表示接口未给出。用于识别充电专属试看片段。</summary>
    public int Duration { get; set; }
    // ⬇⬇⬇⬇⬇ FOR FLV ⬇⬇⬇⬇⬇
    public List<string> Clips { get; set; } = [];
    public List<string> Dfns { get; set; } = [];
}