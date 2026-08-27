# 性能报告

## record 复制

15 个结果 - 8 文件

BBDown\Serve\Tasks\TaskStore.cs:
  224:            return option with { WorkDir = workDir };

BBDown.Core\Auth\CredentialStore.cs:
  76:         var c = LoadCredential(dir) with { Cookie = cookie, RefreshToken = refreshToken, Ts = issueTs };
  82:         var c = LoadCredential(dir) with { TvAccessToken = accessToken, TvTs = issueTs };
  88:         var c = LoadCredential(dir) with { AppAccessToken = accessToken, AppTs = issueTs };

BBDown.Core\Download\DownloadRequest.cs:
  82:         return this with { Cookie = "", AccessToken = "" };

BBDown.Core\Media\DashDownload.cs:
  72:             selection = selection with { Selected = true, VIndex = vIndex, AIndex = aIndex };

BBDown.Core\Media\FlvDownload.cs:
  48:                         selection = selection with { Selected = true, VIndex = TrackSelect.PickDfn(dfns) };

BBDown.Core\Media\PageDownload.cs:
  62:                         pageCtx = pageCtx with { IsPreview = true };
  77:                 session = session with { Subtitles = subtitleInfo };
  81:                     outcome = outcome with { Preview = true };

BBDown.Core\Mux\Muxer.cs:
  265:         req = req with { VideoPath = videoPath, AudioPath = audioPath, Subs = validSubs };

BBDown.Core\Pipeline\VideoInfo.cs:
   36:             cfg = cfg with { Cookie = await Login.TryRefreshWebCookieIfStaleAsync(token: ct) };
   47:         cfg = cfg with { Wbi = wbi };
  121:             return myOption with { Api = ApiType.Web };
  127:             return myOption with { Api = ApiType.Web };
