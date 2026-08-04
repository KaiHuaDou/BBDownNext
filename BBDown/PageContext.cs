using static BBDown.Core.Entity.Entity;

namespace BBDown;

internal sealed record PageContext(
    Page Page,
    string Title,
    string Desc,
    string EpisodeTitle,
    // 该分P的临时目录（绝对路径），中间产物统一落在这里。
    string TempDir,
    string VideoPath,
    string AudioPath,
    string CoverPath,
    string CoverUrl,
    long PubTime,
    int PagesCount,
    bool DeleteCoverAfterMux,
    // 该分P下发的是充电专属试看片段（已由 --allow-preview 放行），落盘文件名加 [试看] 前缀以便与完整视频区分。
    bool IsPreview = false);
