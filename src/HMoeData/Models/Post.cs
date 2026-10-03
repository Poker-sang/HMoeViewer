using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace HMoeData.Models;

[Index(nameof(DateUnixTimeSeconds))]
[Index(nameof(WriteTimeUnixTimeSeconds))]
public class Post : IEquatable<Post>
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("url")]
    public required Uri Url { get; set; }

    [JsonPropertyName("slug")]
    public required string Slug { get; set; }

    [JsonPropertyName("commentsCount")]
    public required int CommentsCount { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [NotMapped]
    [JsonPropertyName("thumbnail")]
    public required Thumbnail Thumbnail
    {
        get => DbThumbnail ?? throw new InvalidOperationException($"Post {Id} is missing thumbnail.");
        set => DbThumbnail = value;
    }

    [NotMapped]
    [JsonPropertyName("author")]
    public required Author Author
    {
        get => DbAuthor ?? throw new InvalidOperationException($"Post {Id} is missing author.");
        set
        {
            DbAuthor = value;
            DbAuthorId = value.Id;
        }
    }

    [NotMapped]
    [JsonPropertyName("tags")]
    public required IReadOnlyList<Tag> Tags
    {
        get => DbTags;
        set => DbTags = [.. value];
    }

    [NotMapped]
    [JsonPropertyName("cats")]
    public required IReadOnlyList<Category> Cats
    {
        get => DbCats;
        set => DbCats = [.. value];
    }

    [JsonPropertyName("excerpt")]
    public required string Excerpt { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("date")]
    [JsonConverter(typeof(DateInfoToDateTimeOffsetConverter))]
    public required DateTimeOffset Date { get; set; }

    [JsonPropertyName("modifiedDate")]
    [JsonConverter(typeof(DateInfoToDateTimeOffsetConverter))]
    public required DateTimeOffset ModifiedDate { get; set; }

    [JsonPropertyName("views")]
    public required int Views { get; set; }

    [JsonPropertyName("WriteTime")]
    [JsonIgnore]
    public DateTimeOffset WriteTime { get; set; }

    [JsonPropertyName("IsSelected")]
    [JsonIgnore]
    public PostSelectionState IsSelected { get; set; } = PostSelectionState.Unselected;

    [JsonIgnore]
    public int DbAuthorId { get; set; }

    [JsonIgnore]
    public Author? DbAuthor { get; set; }

    [JsonIgnore]
    public Thumbnail? DbThumbnail { get; set; }

    [JsonIgnore]
    public List<Tag> DbTags { get; set; } = [];

    [JsonIgnore]
    public List<Category> DbCats { get; set; } = [];

    [JsonIgnore]
    public long DateUnixTimeSeconds { get; set; }

    [JsonIgnore]
    public long ModifiedDateUnixTimeSeconds { get; set; }

    [JsonIgnore]
    public long WriteTimeUnixTimeSeconds { get; set; }

    [JsonIgnore]
    [NotMapped]
    public string LocalThumbnailPath { get; set; } = string.Empty;

    [JsonIgnore]
    [NotMapped]
    public string ThumbnailFileName =>
        Id +
        Path.GetExtension(Thumbnail.Url.IsAbsoluteUri
            ? Thumbnail.Url.Segments[^1]
            : Thumbnail.Url.OriginalString);

    public bool Equals(Post? other) => other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    public override bool Equals(object? obj) => Equals(obj as Post);

    public override int GetHashCode() => Id;
}
