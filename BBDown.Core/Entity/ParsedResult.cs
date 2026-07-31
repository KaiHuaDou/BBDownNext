using System.Collections.Generic;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Entity;

public class ParsedResult
{
    public string WebJsonString { get; set; } = "";
    public List<Video> VideoTracks { get; set; } = [];
    public List<Audio> AudioTracks { get; set; } = [];
    public List<Audio> BackgroundAudioTracks { get; set; } = [];
    public List<AudioMaterialInfo> RoleAudioList { get; set; } = [];
    public List<ViewPoint> ExtraPoints { get; set; } = [];
    // ⬇⬇⬇⬇⬇ FOR FLV ⬇⬇⬇⬇⬇
    public List<string> Clips { get; set; } = [];
    public List<string> Dfns { get; set; } = [];
}