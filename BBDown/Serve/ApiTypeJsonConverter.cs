using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using BBDown.Core;

namespace BBDown.Serve;

/// <summary>
/// serve 请求体用字符串表达 API 通道（如 "tv"，忽略大小写），与 CLI 输入一致；
/// 序列化时输出数字，保证 <see cref="DownloadRequest"/> 的枚举字段经 STJ 往返能正常还原。
/// </summary>
internal sealed class ApiTypeJsonConverter : JsonConverter<ApiType>
{
    public override ApiType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ApiTypeUtil.TryParse(reader.TokenType == JsonTokenType.String ? reader.GetString( ) : null) ?? ApiType.Web;
    }

    public override void Write(Utf8JsonWriter writer, ApiType value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}

/// <summary>
/// serve 请求体用规范化字符串表达内容集（如 "avmsCi"，非法字符忽略），与 CLI 输入一致；
/// 序列化时输出数字，保证 <see cref="DownloadRequest"/> 的枚举字段经 STJ 往返能正常还原。
/// </summary>
internal sealed class DownloadContentJsonConverter : JsonConverter<DownloadContent>
{
    public override DownloadContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ContentSelector.FromNormalizedString(reader.TokenType == JsonTokenType.String ? reader.GetString( ) : null);
    }

    public override void Write(Utf8JsonWriter writer, DownloadContent value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
