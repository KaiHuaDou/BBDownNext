using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBDown.Core;

/// <summary>
/// <see cref="ResourceId"/> ↔ 规范字符串的 JSON 转换：serve API 中任务 id 以字符串形态出现
/// （如 "season2539"），与 <see cref="ResourceId.TryParse"/> 的路径参数编码严格对称，
/// 客户端拿到即可直接回显到 /get-tasks/{id} 等路径。
/// </summary>
public sealed class ResourceIdJsonConverter : JsonConverter<ResourceId>
{
    public override ResourceId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString( );
        if (text is not null && ResourceId.TryParse(text, out var id))
        {
            return id;
        }

        throw new JsonException("ResourceId 应为规范字符串（如 \"season2539\"）");
    }

    public override void Write(Utf8JsonWriter writer, ResourceId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(Format(value));
    }

    private static string Format(ResourceId id)
    {
        return id switch
        {
            ResourceId.Av a => $"av{a.Aid}",
            ResourceId.Ep e => $"ep{e.EpId}",
            ResourceId.Season s => $"season{s.SeasonId}",
            ResourceId.CheeseEp e => $"cheeseEp{e.EpId}",
            ResourceId.CheeseSeason s => $"cheeseSeason{s.SeasonId}",
            ResourceId.Fav f => $"fav{f.Fid}_{f.Mid}",
            ResourceId.MediaList m => $"mediaList{m.BizId}",
            ResourceId.Series s => $"series{s.BizId}",
            ResourceId.Space s => $"space{s.Mid}",
            ResourceId.WatchLater => "watchLater",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }
}
