using System;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public class Category
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }

    [JsonPropertyName("parentId")]
    public required int ParentId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("slug")]
    public required string Slug { get; set; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; set; }

    [JsonPropertyName("url")]
    public required Uri Url { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }
}
