namespace BBDown.GUI.Tests;

public class TaskParamsMapperTests
{
    [Fact]
    public void ToDownloadRequest_Defaults_MapToWebApiAndDefaultContent( )
    {
        var req = new TaskParams( ).ToDownloadRequest("BV1xx");

        Assert.Equal(ApiType.Web, req.Api);
        Assert.Equal(MuxMode.Mpeg4, req.Mux);
        Assert.Equal("BV1xx", req.Url);
    }

    [Fact]
    public void ToDownloadRequest_BoolOptions_AreMapped( )
    {
        var req = new TaskParams
        {
            InfoOnly = true,
            ShowAll = true,
            UseAria2c = true,
            SingleThread = true,
        }.ToDownloadRequest("BV1xx");

        Assert.True(req.OnlyShowInfo);
        Assert.True(req.ShowAll);
        Assert.True(req.UseAria2c);
        Assert.True(req.SingleThread);
    }

    [Fact]
    public void ToDownloadRequest_InvalidNumbers_FallBackToDefaults( )
    {
        var req = new TaskParams { CommentsCount = "abc", LiveQuality = "xyz" }.ToDownloadRequest("BV1xx");

        Assert.Equal(0, req.CommentCount);
        Assert.Equal(LiveQuality.Original, req.LiveQuality);
    }

    [Fact]
    public void ToDownloadRequest_InvalidEnums_FallBackToDefaults( )
    {
        var req = new TaskParams { Api = "nope", Mux = "nope" }.ToDownloadRequest("BV1xx");

        Assert.Equal(ApiType.Web, req.Api);
        Assert.Equal(MuxMode.Mpeg4, req.Mux);
    }

    [Fact]
    public void ToDownloadRequest_EmptyPriorities_MapToNull( )
    {
        var req = new TaskParams { EncodingPriority = "", DfnPriority = "" }.ToDownloadRequest("BV1xx");

        Assert.Null(req.EncodingPriority);
        Assert.Null(req.DfnPriority);
    }
}
