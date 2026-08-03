using System;

namespace BBDown.Tests;

// 测试用 bilibili 视频候选池。
// 运行测试时随机抽一个，避免长期只打同一个视频（也顺带扩大覆盖）。
public static class TestVideos
{
    // 精选二台短视频
    private static readonly string[] Candidates =
    [
        "https://www.bilibili.com/video/BV133411X769/",
        "https://www.bilibili.com/video/BV13q4y1u7M1/",
        "https://www.bilibili.com/video/BV16U4y1v7hZ/",
        "https://www.bilibili.com/video/BV17X4y157rE/",
        "https://www.bilibili.com/video/BV1dP411H7pS/",
        "https://www.bilibili.com/video/BV1eX4y1w7fB/",
        "https://www.bilibili.com/video/BV1HMoEYzEdi/",
        "https://www.bilibili.com/video/BV1hY411J7cA/",
        "https://www.bilibili.com/video/BV1iQgEzdEpn/",
        "https://www.bilibili.com/video/BV1nN4y1c72F/",
        "https://www.bilibili.com/video/BV1Rs411o7Sm/",
        "https://www.bilibili.com/video/BV1U64y1C7XY/",
        "https://www.bilibili.com/video/BV1WZ4y1n7z2/",
        "https://www.bilibili.com/video/BV1Xt411a7Cs/",
        "https://www.bilibili.com/video/BV1z34y1o79G/",
        "https://www.bilibili.com/video/BV1Zf7fzZE8w/",
    ];

    public static string PickRandom( )
    {
        return Candidates[Random.Shared.Next(Candidates.Length)];
    }
}
