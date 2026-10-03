using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public class Thumbnail
{
    [JsonIgnore]
    [Key]
    [ForeignKey(nameof(DbPost))]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int DbPostId { get; set; }

    [JsonIgnore]
    public Post? DbPost { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("width")]
    public required string Width { get; set; }

    [JsonPropertyName("height")]
    public required string Height { get; set; }

    [JsonPropertyName("url")]
    public required Uri Url { get; set; }

    [JsonPropertyName("visible")]
    public required bool Visible { get; set; }
}
