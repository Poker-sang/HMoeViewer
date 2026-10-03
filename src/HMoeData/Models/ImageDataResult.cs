using System.Text.Json.Serialization;

namespace HMoeData.Models;

public record ImageDataResult
{
    [JsonPropertyName("imgData")]
    public required string ImgData { get; init; }
}
