using System.Text.Json;

using BBDown.Core.Download;

namespace BBDown.Sample.Tests;

// 协议契约测试：主程序以 PascalCase 序列化请求 JSON，Sample 复用主程序同款源生成器上下文
// PostProcessJsonContext 反序列化，字段名与值必须严格对齐（模板的核心约定）。
public class ProtocolTests
{
    [Fact]
    public void RequestJson_DeserializesPascalCaseFields( )
    {
        const string json =
            """{"Aid":"170001","Cid":"283298412","Kind":"video","TrackPath":"D:\\Downloads\\video.m4s","DestPath":"D:\\Downloads\\video.m4s.out.mp4","Ffmpeg":"D:\\ffmpeg\\bin\\ffmpeg.exe"}""";

        var request = JsonSerializer.Deserialize(json, PostProcessJsonContext.Default.PostProcessRequest);

        Assert.NotNull(request);
        Assert.Equal("170001", request!.Aid);
        Assert.Equal("283298412", request.Cid);
        Assert.Equal("video", request.Kind);
        Assert.Equal("D:\\Downloads\\video.m4s", request.TrackPath);
        Assert.Equal("D:\\Downloads\\video.m4s.out.mp4", request.DestPath);
        Assert.Equal("D:\\ffmpeg\\bin\\ffmpeg.exe", request.Ffmpeg);
    }

    [Fact]
    public void RequestJson_RoundTripsThroughMainProcessContext( )
    {
        var request = new PostProcessRequest("1", "2", "audio", "a.m4s", "a.out.mp4", "ffmpeg");

        var json = JsonSerializer.Serialize(request, PostProcessJsonContext.Default.PostProcessRequest);
        var back = JsonSerializer.Deserialize(json, PostProcessJsonContext.Default.PostProcessRequest);

        Assert.Equal(request, back);
    }
}
