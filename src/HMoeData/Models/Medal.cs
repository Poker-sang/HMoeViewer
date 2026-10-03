using System;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public class Medal
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("imgUrl")]
    public required Uri ImgUrl { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("des")]
    public required string Des { get; set; }

    [JsonPropertyName("attrTitle")]
    public required string AttrTitle { get; set; }
}
