using static BBDown.Core.Entity.Entity;

namespace BBDown;

internal sealed record PageContext(
    Page Page,
    string Title,
    string Desc,
    string EpisodeTitle,
    string VideoPath,
    string AudioPath,
    string CoverPath,
    string CoverUrl,
    long PubTime,
    int PagesCount,
    bool DeleteCoverAfterMux);
