using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using HMoeWebCrawler.LocalModels;

namespace HMoeWebCrawler;

[JsonSerializable(typeof(Settings))]
public partial class SerializerContext : JsonSerializerContext
{
    public static SerializerContext DefaultOverride => field ??= new(new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}
