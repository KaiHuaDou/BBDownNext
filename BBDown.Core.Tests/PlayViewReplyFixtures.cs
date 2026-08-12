using BBDown.Core.Protobuf;
using VideoInfo = BBDown.Core.Protobuf.VideoInfo;

using Google.Protobuf;

namespace BBDown.Core.Tests;

// 现网 gRPC 响应无法在离线测试中抓取, 改为按 playviewreply.proto 构造等价报文。
// 构造完的对象一律经 PackMessage/ReadMessage 走一遍真实 gzip 封帧再 ParseFrom,
// 这样 proto2 的字段默认值语义(未设置的 optional string 读出 "" 而非 null)与线上一致。
internal static class PlayViewReplyFixtures
{
    private static readonly MessageParser<PlayViewReply> Parser = new(( ) => new PlayViewReply( ));

    /// <summary>
    /// 普通稿件: 两路 dash 视频 + 一路仅分段(无 dash)视频 + 两路伴音 + Hi-Res + 杜比
    /// </summary>
    public static PlayViewReply Ugc( )
    {
        return RoundTrip(new PlayViewReply
        {
            VideoInfo = new VideoInfo
            {
                Quality = 120,
                Timelength = 754000,
                StreamList =
            {
                DashStream(120, "https://upos-sz-mirror08c.bilivideo.com/v120.m4s", 12, 754_000_000,
                    "https://xy1.mcdn.bilivideo.cn:8082/v120.m4s", "https://cn-hbyc-cu-01.bilivideo.com/v120.m4s"),
                DashStream(80, "https://upos-sz-mirror08c.bilivideo.com/v80.m4s", 7, 377_000_000),
                new StreamItem { StreamInfo = new StreamInfo { Quality = 64 }, SegmentVideo = new SegmentVideo( ) }
            },
                DashAudio =
            {
                DashItem(30280, "https://upos-sz-mirror08c.bilivideo.com/a30280.m4s", 320_000, "https://xy2.mcdn.bilivideo.cn:4483/a30280.m4s"),
                DashItem(30216, "https://upos-sz-mirror08c.bilivideo.com/a30216.m4s", 64_000)
            },
                Flac = new DolbyItem { Audio = DashItem(30251, "https://upos-sz-mirror08c.bilivideo.com/a30251.m4s", 1_000_000) },
                Dolby = new DolbyItem { Audio = DashItem(30250, "https://upos-sz-mirror08c.bilivideo.com/a30250.m4s", 448_000) }
            }
        });
    }

    /// <summary>
    /// 番剧: 带片头片尾跳过 + 背景音轨 + 两条配音(第二条只有 edition 没有 title/person_name)
    /// </summary>
    public static PlayViewReply Bangumi( )
    {
        return RoundTrip(new PlayViewReply
        {
            VideoInfo = new VideoInfo
            {
                Quality = 125,
                Timelength = 1420000,
                StreamList = { DashStream(125, "https://upos-sz-mirror08c.bilivideo.com/ep125.m4s", 12, 1_420_000_000) },
                DashAudio = { DashItem(30280, "https://upos-sz-mirror08c.bilivideo.com/ep30280.m4s", 320_000) }
            },
            Business = new BusinessInfo
            {
                ClipInfo =
            {
                new ClipInfo { Start = 0, End = 90, ToastText = "即将跳过片头" },
                new ClipInfo { Start = 1350, End = 1420, ToastText = "即将跳过片尾" }
            }
            },
            PlayExtInfo = new PlayExtInfo
            {
                PlayDubbingInfo = new PlayDubbingInfo
                {
                    BackgroundAudio = new AudioMaterialProto
                    {
                        AudioId = "bg",
                        Audio = { DashItem(30280, "https://upos-sz-mirror08c.bilivideo.com/bg.m4s", 320_000) }
                    },
                    RoleAudioList =
                {
                    new RoleAudioProto
                    {
                        AudioMaterialList =
                        {
                            new AudioMaterialProto
                            {
                                AudioId = "1001",
                                Title = "中文配音",
                                PersonName = "张三",
                                Audio = { DashItem(30280, "https://upos-sz-mirror08c.bilivideo.com/cn.m4s", 320_000) }
                            },
                            new AudioMaterialProto
                            {
                                AudioId = "1002",
                                Edition = "日语原声",
                                Audio = { DashItem(30216, "https://upos-sz-mirror08c.bilivideo.com/jp.m4s", 64_000) }
                            }
                        }
                    }
                }
                }
            }
        });
    }

    /// <summary>
    /// 服务端拒绝播放时返回的空壳响应: videoInfo 整个缺失
    /// </summary>
    public static PlayViewReply Empty( )
    {
        return RoundTrip(new PlayViewReply( ));
    }

    private static StreamItem DashStream(uint quality, string baseUrl, uint codecid, ulong size, params string[] backupUrl)
    {
        var stream = new StreamItem
        {
            StreamInfo = new StreamInfo { Quality = quality },
            DashVideo = new DashVideo { BaseUrl = baseUrl, Codecid = codecid, Size = size }
        };
        stream.DashVideo.BackupUrl.AddRange(backupUrl);
        return stream;
    }

    private static DashItem DashItem(uint id, string baseUrl, uint bandwidth, params string[] backupUrl)
    {
        var item = new DashItem { Id = id, BaseUrl = baseUrl, Bandwidth = bandwidth };
        item.BackupUrl.AddRange(backupUrl);
        return item;
    }

    private static PlayViewReply RoundTrip(PlayViewReply reply)
    {
        return Parser.ParseFrom(AppHelper.ReadMessage(AppHelper.PackMessage(reply.ToByteArray( ))));
    }
}
