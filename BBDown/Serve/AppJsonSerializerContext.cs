using System.Collections.Generic;
using System.Text.Json.Serialization;

using BBDown.Core;
using BBDown.Serve.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BBDown.Serve;

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(DownloadTask))]
[JsonSerializable(typeof(List<DownloadTask>))]
[JsonSerializable(typeof(DownloadTaskSnapshot))]
[JsonSerializable(typeof(ResourceId))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(QrLoginStartRequest))]
[JsonSerializable(typeof(QrLoginStartResponse))]
[JsonSerializable(typeof(QrLoginStatusResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonSerializerContext : JsonSerializerContext;
