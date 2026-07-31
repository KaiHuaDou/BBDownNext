using static BBDown.Core.Entity.Entity;

namespace BBDown;

internal sealed record PageContext(
    Page Page,
    string Title,
    string Desc,
    string EpisodeTitle,
    /// <summary>该分P的临时目录（绝对路径），中间产物统一落在这里。</summary>
    string TempDir,
    string VideoPath,
    string AudioPath,
    string CoverPath,
    string CoverUrl,
    long PubTime,
    int PagesCount,
    bool DeleteCoverAfterMux);
