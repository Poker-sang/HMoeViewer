using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public record PostsSearchResult
{
    [JsonPropertyName("posts")]
    public required Stack<Post> Posts { get; init; }
}
