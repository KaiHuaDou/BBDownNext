using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Fetcher;

public static class SpaceVideoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg)
    {
        id = id[4..];
        // using the live API can bypass w_rid
        var userInfoApi = $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={id}";
        using var userJson = await GetJsonAsync(userInfoApi, cfg);
        var userName = BBDown.Core.Util.FileNameUtil.GetValidFileName(userJson.RootElement.GetProperty("data").GetProperty("info").GetProperty("uname").ToString( ), ".", true);
        List<string> urls = [];
        var pageSize = 50;
        var pageNumber = 1;
        var api = Parser.WbiSign($"mid={id}&order=pubdate&pn={pageNumber}&ps={pageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds( )}", cfg);
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        var json = await GetWebSourceAsync(api, cfg);
        var infoJson = JsonDocument.Parse(json);
        JsonElement.ArrayEnumerator pages = infoJson.RootElement.GetProperty("data").GetProperty("list").GetProperty("vlist").EnumerateArray( );
        foreach (JsonElement page in pages)
        {
            urls.Add($"https://www.bilibili.com/video/av{page.GetProperty("aid")}");
        }

        var totalCount = infoJson.RootElement.GetProperty("data").GetProperty("page").GetProperty("count").GetInt32( );
        var totalPage = (int) Math.Ceiling((double) totalCount / pageSize);
        while (pageNumber < totalPage)
        {
            pageNumber++;
            urls.AddRange(await GetVideosByPageAsync(pageNumber, pageSize, id, cfg));
        }

        await File.WriteAllTextAsync("urls.txt", string.Join(Environment.NewLine, urls));
        Log("目前下载器不支持下载用户的全部投稿视频，不过程序已经获取到了该用户的全部投稿视频地址，你可以自行使用批处理脚本等手段调用本程序进行批量下载。如在Windows系统你可以使用如下代码：");
        Console.WriteLine( );
        Console.WriteLine(@"@echo Off
For /F %%a in (urls.txt) Do (BBDown.exe ""%%a"")
pause");
        Console.WriteLine( );
        throw new Exception("暂不支持该功能");
    }

    private static async Task<List<string>> GetVideosByPageAsync(int pageNumber, int pageSize, string mid, AppConfig cfg)
    {
        List<string> urls = [];
        var api = Parser.WbiSign($"mid={mid}&order=pubdate&pn={pageNumber}&ps={pageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds( )}", cfg);
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        var json = await GetWebSourceAsync(api, cfg);
        var infoJson = JsonDocument.Parse(json);
        JsonElement.ArrayEnumerator pages = infoJson.RootElement.GetProperty("data").GetProperty("list").GetProperty("vlist").EnumerateArray( );
        foreach (JsonElement page in pages)
        {
            urls.Add($"https://www.bilibili.com/video/av{page.GetProperty("aid")}");
        }

        return urls;
    }
}