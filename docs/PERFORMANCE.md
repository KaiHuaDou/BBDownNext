# 性能报告

## record 复制

25 个结果 - 17 文件

BBDown\Program.cs:
  241:             myOption = myOption with { Url = url };

BBDown\Serve\Tasks\TaskStore.cs:
  305:             return option with { WorkDir = workDir };

BBDown.Core\Auth\CredentialStore.cs:
  74:         var c = LoadCredential(dir) with { Cookie = cookie, RefreshToken = refreshToken, Ts = issueTs };
  80:         var c = LoadCredential(dir) with { TvAccessToken = accessToken, TvTs = issueTs };
  86:         var c = LoadCredential(dir) with { AppAccessToken = accessToken, AppTs = issueTs };

BBDown.Core\Download\DownloadRequest.cs:
  83:         return this with { Cookie = "", AccessToken = "" };

BBDown.Core\Media\DashDownload.cs:
  73:             selection = selection with { Selected = true, VIndex = vIndex, AIndex = aIndex };

BBDown.Core\Media\FlvDownload.cs:
  48:                         selection = selection with { Selected = true, VIndex = await TrackSelect.PickDfnAsync(dfns, ct) };

BBDown.Core\Media\PageDownload.cs:
  61:                 pageCtx = pageCtx with { IsPreview = true };
  76:         session = session with { Subtitles = subtitleInfo };
  80:             outcome = outcome with { Preview = true };

BBDown.Core\Mux\Muxer.cs:
  65:         req = req with { VideoPath = videoPath, AudioPath = audioPath, Subs = validSubs };

BBDown.Core\Pipeline\ReadListDownload.cs:
  47:             var itemReq = myOption with { Url = $"{BiliApi.ReadPage}/cv{cvId}", WorkDir = itemDir };

BBDown.Core\Pipeline\SpaceAudioDownload.cs:
  55:                 await AudioDownload.RunAsync(item.AuId, myOption with { WorkDir = itemDir }, sink, ct);

BBDown.Core\Pipeline\SpaceDynamicDownload.cs:
  84:                     var videoReq = myOption with { Url = $"{BiliApi.VideoPage}/{item.BvId}", WorkDir = itemDir };
  89:                     var opusReq = myOption with { Url = $"{BiliApi.OpusPage}/{item.OpusId}", WorkDir = itemDir };

BBDown.Core\Pipeline\SpaceDynamicFeed.cs:
  31:         return cfg with { Wbi = wbi };

BBDown.Core\Pipeline\SpaceOpusDownload.cs:
  50:             var itemReq = myOption with { Url = $"{BiliApi.OpusPage}/{item.OpusId}", WorkDir = itemDir };

BBDown.Core\Pipeline\VideoInfo.cs:
   34:             cfg = cfg with { Cookie = await Login.TryRefreshWebCookieIfStaleAsync(token: ct) };
   55:         cfg = cfg with { Wbi = wbi };
  130:             return myOption with { Api = ApiType.Web };
  136:             return myOption with { Api = ApiType.Web };

BBDown.Core.Tests\BiliHeadersTests.cs:
  97:         var cfg = AppConfig.Empty with { EpHost = "mirror.example.com" };

BBDown.Core.Tests\DownloadTests.cs:
  242:         var cfg = AppConfig.Empty with { Cookie = "SESSDATA=abc" };

BBDown.GUI\MainWindow.Download.cs:
  50:                         req = req with { Url = url };
