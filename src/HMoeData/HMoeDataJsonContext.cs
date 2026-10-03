using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using HMoeData.Models;

namespace HMoeData;

[JsonSerializable(typeof(SearchData))]
[JsonSerializable(typeof(ApiResponse))]
[JsonSerializable(typeof(DateInfo))]
[JsonSerializable(typeof(Post))]
[JsonSerializable(typeof(HashSet<Post>))]
[JsonSerializable(typeof(IReadOnlyList<Post>))]
[JsonSerializable(typeof(List<Post>))]
[JsonSerializable(typeof(ImageDataResult))]
[JsonSerializable(typeof(PostsSearchResult))]
public partial class HMoeDataJsonContext : JsonSerializerContext
{
    public static HMoeDataJsonContext DefaultOverride => field ??= new(new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}