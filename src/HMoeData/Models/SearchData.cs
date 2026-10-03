using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HMoeData.Models;

public record SearchData()
{
    public SearchData(int paged) : this()
    {
        Paged = paged;
    }

    [JsonPropertyName("kw")]
    [JsonPropertyOrder(0)]
    public string KeyWord { get; init; } = "";

    [JsonPropertyName("tags")]
    [JsonPropertyOrder(1)]
    public string[] Tags { get; init; } = [];

    [JsonPropertyName("cat")]
    [JsonPropertyOrder(2)]
    public string[] Cat { get; init; } = [];

    [JsonPropertyName("paged")]
    [JsonPropertyOrder(3)]
    public int Paged
    {
        get;
        set
        {
            if (value < 1)
                throw new InvalidDataException("Paged must be greater than or equal to 1.");
            field = value;
        }
    }

    [JsonPropertyName("cats")]
    [JsonPropertyOrder(4)]
    public string[] Cats { get; init; } = [];

    public string Encode()
    {
        var u8Str = JsonSerializer.SerializeToUtf8Bytes(this, HMoeDataJsonContext.DefaultOverride.SearchData);
        var str = Convert.ToBase64String(u8Str);
        return Uri.EscapeDataString(str);
    }

    public static SearchData? Decode(string data)
    {
        while (data.Contains('%'))
            data = Uri.UnescapeDataString(data);
        var u8Str = Encoding.UTF8.GetString(Convert.FromBase64String(data));
        return JsonSerializer.Deserialize(u8Str, HMoeDataJsonContext.DefaultOverride.SearchData);
    }
}
