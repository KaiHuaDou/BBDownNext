# 性能报告

## record 复制

27 个结果 - 18 文件

```
BBDown\Program.cs:
  170:             myOption = myOption with { Url = url };

BBDown\Serve\Tasks\TaskStore.cs:
  305:             return option with { WorkDir = workDir };

BBDown.Core\Auth\CredentialStore.cs:
  80:         return SaveCredential(dir, c => c with { Cookie = cookie, RefreshToken = refreshToken, Ts = issueTs });
  85:         return SaveCredential(dir, c => c with { TvAccessToken = accessToken, TvTs = issueTs });
  90:         return SaveCredential(dir, c => c with { AppAccessToken = accessToken, AppTs = issueTs });

BBDown.Core\Download\DownloadRequest.cs:
  83:         return this with { Cookie = "", AccessToken = "" };

BBDown.Core\Media\DashDownload.cs:
  73:             selection = selection with { Selected = true, VIndex = vIndex, AIndex = aIndex };

BBDown.Core\Media\FlvDownload.cs:
  156:             selection = selection with { Selected = true, VIndex = await TrackSelect.PickDfnAsync(dfns, ct) };
  189:         var clipConfig = downloadConfig with { ParallelCount = DownloaderAdapter.MaxRangeConcurrency / MaxClipParallelism };

BBDown.Core\Media\PageDownload.cs:
  61:                 pageCtx = pageCtx with { IsPreview = true };
  76:         session = session with { Subtitles = subtitleInfo };
  80:             outcome = outcome with { Preview = true };

BBDown.Core\Mux\Muxer.cs:
  65:         req = req with { VideoPath = videoPath, AudioPath = audioPath, Subs = validSubs };

BBDown.Core\Pipeline\OpusDownload.cs:
  151:                 config = config with { Wbi = wbi };

BBDown.Core\Pipeline\ReadListDownload.cs:
  47:             var itemReq = myOption with { Url = $"{BiliApi.ReadPage}/cv{cvId}", WorkDir = itemDir };

BBDown.Core\Pipeline\SpaceAudioDownload.cs:
  55:                 await AudioDownload.RunAsync(item.AuId, myOption with { WorkDir = itemDir }, sink, ct);

BBDown.Core\Pipeline\SpaceDynamicDownload.cs:
  84:                     var videoReq = myOption with { Url = $"{BiliApi.VideoPage}/{item.BvId}", WorkDir = itemDir };
  89:                     var opusReq = myOption with { Url = $"{BiliApi.OpusPage}/{item.OpusId}", WorkDir = itemDir };

BBDown.Core\Pipeline\SpaceDynamicFeed.cs:
  38:         return cfg with { Wbi = wbi };

BBDown.Core\Pipeline\SpaceOpusDownload.cs:
  50:             var itemReq = myOption with { Url = $"{BiliApi.OpusPage}/{item.OpusId}", WorkDir = itemDir };

BBDown.Core\Pipeline\VideoInfo.cs:
  37:                 cfg = cfg with { Cookie = newCookie };
  58:         cfg = cfg with { Wbi = wbi };
  133:             return myOption with { Api = ApiType.Web };
  139:             return myOption with { Api = ApiType.Web };

BBDown.GUI\MainWindow.Download.cs:
  39:                     req = req with { Url = url };

BBDown.Core.Tests\BiliHeadersTests.cs:
  97:         var cfg = AppConfig.Empty with { EpHost = "mirror.example.com" };

BBDown.Core.Tests\DownloadTests.cs:
  240:         var cfg = AppConfig.Empty with { Cookie = "SESSDATA=abc" };
```
