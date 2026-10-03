using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public class Author
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }

    [JsonPropertyName("uid")]
    public required string Uid { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("url")]
    public required Uri Url { get; set; }

    [JsonPropertyName("avatarUrl")]
    public required Uri AvatarUrl { get; set; }

    [JsonPropertyName("des")]
    public required string Des { get; set; }

    [JsonPropertyName("commentsCount")]
    public required int CommentsCount { get; set; }

    [JsonPropertyName("postsCount")]
    public required int PostsCount { get; set; }

    [JsonPropertyName("point")]
    public required int Point { get; set; }

    [NotMapped]
    [JsonPropertyName("medals")]
    public required IReadOnlyList<Medal> Medals
    {
        get => DbMedals;
        set => DbMedals = [.. value];
    }

    [JsonPropertyName("followersCount")]
    public required int FollowersCount { get; set; }

    [JsonPropertyName("fansCount")]
    public required int FansCount { get; set; }

    [NotMapped]
    [JsonPropertyName("role")]
    public required Role Role
    {
        get => DbRole ?? throw new InvalidOperationException($"Author {Id} is missing role.");
        set
        {
            DbRole = value;
            DbRoleName = value.Name;
        }
    }

    [JsonIgnore]
    public string DbRoleName { get; set; } = string.Empty;

    [JsonIgnore]
    public Role? DbRole { get; set; }

    [JsonIgnore]
    public List<Medal> DbMedals { get; set; } = [];
}
