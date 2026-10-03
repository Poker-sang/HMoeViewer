using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public class Role
{
    [JsonPropertyName("color")]
    public required string Color { get; set; }

    [Key]
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}
