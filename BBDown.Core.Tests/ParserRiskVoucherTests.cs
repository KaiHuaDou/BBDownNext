using System;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

// 走 HttpStub 驱动 playurl 真实链路：只测纯函数无法证明 Parser 的调用点在位，
// 摘掉 PlayUrlResponse.ThrowIfRiskControlled 调用后本文件用例会红
[Collection<HttpStubCollectionDefinition>]
public class ParserRiskVoucherTests
{
    // B 站风控窗口内的真实响应形状：HTTP 200、code=0、合法 JSON，但只有 v_voucher 凭据，
    // 既无 dash 也无 durl。此前三处漏判（code 检查只拦非 0、解析出零轨道）会让用户
    // 只看到「未解析到任何音视频轨道」，且不参与 PageDownload 的解析重试
    private const string VoucherJson = """
    {
      "code": 0,
      "message": "OK",
      "ttl": 1,
      "data": {
        "v_voucher": "voucher_6ffa4b62-5720-4de1-9bdf-20031d94bda5"
      }
    }
    """;

    private const string DashJson = """
    {
      "code": 0,
      "data": {
        "timelength": 125000,
        "dash": {
          "duration": 125,
          "video": [
            { "id": 80, "codecid": 7, "bandwidth": 1000000, "width": 1920, "height": 1080, "frame_rate": 30, "base_url": "https://cdn/v.m4s", "backup_url": ["https://cdn2/v.m4s"] }
          ],
          "audio": [
            { "id": 30280, "codecs": "mp4a.40.2", "bandwidth": 192000, "base_url": "https://cdn/a.m4s" }
          ]
        }
      }
    }
    """;

    [Fact]
    public async Task ExtractTracksAsync_VoucherResponse_ThrowsReadableRiskError( )
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) => HttpStub.WithJsonResponse(
            VoucherJson, ( ) => ExtractAsync(ApiType.Web)));

        Assert.Contains("v_voucher", ex.Message);
        Assert.Contains("人机验证", ex.Message);
    }

    // 番剧 / 课程类端点把数据根放在 result 下，风控凭据同样可能挂在那里
    [Fact]
    public async Task ExtractTracksAsync_VoucherUnderResult_ThrowsReadableRiskError( )
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) => HttpStub.WithJsonResponse(
            """{"code":0,"result":{"v_voucher":"voucher_x"}}""",
            ( ) => ExtractAsync(ApiType.Web, isEpisode: true)));

        Assert.Contains("v_voucher", ex.Message);
    }

    // 正常播放响应不得被误伤
    [Fact]
    public async Task ExtractTracksAsync_NormalDashResponse_ParsesTracks( )
    {
        var result = await HttpStub.WithJsonResponse(DashJson, ( ) => ExtractAsync(ApiType.Web));

        Assert.Single(result.VideoTracks);
        Assert.Single(result.AudioTracks);
    }

    // code != 0 的业务错误优先于风控凭据判定：两者同时出现时报业务错误，code 里已含原因
    [Fact]
    public async Task ExtractTracksAsync_BizErrorWithVoucher_ReportsBizError( )
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) => HttpStub.WithJsonResponse(
            """{"code":-404,"message":"啥都木有","data":{"v_voucher":"voucher_x"}}""",
            ( ) => ExtractAsync(ApiType.Web)));

        Assert.Contains("-404", ex.Message);
    }

    private static Task<ParsedResult> ExtractAsync(ApiType api, bool isEpisode = false)
    {
        // IsEpisode 只认 Ep / Season 类型，传 Av 会悄悄落到 UGC 分支
        ResourceId id = isEpisode ? new ResourceId.Ep(123) : new ResourceId.Av(170001);
        return Parser.ExtractTracksAsync(id, "170001", "170001", isEpisode ? "123" : "", api, "", AppConfig.Empty);
    }
}
