using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace HMoeViewer.Extraction;

public sealed class ResultStore
{
    private static readonly OutputJsonContext _Json = new(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private readonly string _path;
    private readonly Dictionary<int, PostResult> _latest = [];

    public ResultStore(string directory)
    {
        _ = Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "posts.jsonl");
        if (!File.Exists(_path))
            return;
        var outdated = false;
        foreach (var line in File.ReadLines(_path))
            if (!string.IsNullOrWhiteSpace(line))
            {
                try
                {
                    var result = JsonSerializer.Deserialize(line, _Json.PostResult);
                    if (result is not null && result.FormatVersion != LinkCollector.FormatVersion)
                        outdated = true;
                    else if (result is not null)
                    {
                        LinkCollector.Consolidate(result);
                        _latest[result.Id] = result;
                    }
                }
                catch (JsonException)
                {
                    Console.Error.WriteLine("忽略一条不完整的历史记录；对应 Post 将重试。");
                }
            }
        if (outdated)
        {
            var backup = _path + ".previous";
            if (!File.Exists(backup))
                File.Copy(_path, backup);
            Console.WriteLine($"旧格式结果已保留在 {backup}，旧条目将重新采集。");
        }
    }

    public bool IsComplete(int id) => _latest.TryGetValue(id, out var result)
        && result.Status is "ok" && result.Links.Count > 0 && result.FormatVersion == LinkCollector.FormatVersion;

    public async Task SaveAsync(PostResult result)
    {
        _latest[result.Id] = result;
        // Replace the compact latest-state file atomically so interrupted writes remain resumable.
        var temporary = _path + ".tmp";
        await using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            foreach (var latest in _latest.Values.OrderByDescending(item => item.Id))
                await writer.WriteLineAsync(JsonSerializer.Serialize(latest, _Json.PostResult)).ConfigureAwait(false);
        File.Move(temporary, _path, true);
        await ExportCsvAsync().ConfigureAwait(false);
    }

    private async Task ExportCsvAsync()
    {
        var path = Path.Combine(Path.GetDirectoryName(_path)!, "links.csv");
        await using var writer = new StreamWriter(path + ".tmp", false, new UTF8Encoding(true));
        await writer.WriteLineAsync("PostId,Title,Status,DownloadItem,Label,Url,ExtractionCode,ArchivePassword,Kind,Source").ConfigureAwait(false);
        foreach (var result in _latest.Values.OrderByDescending(item => item.Id))
            foreach (var link in result.Links)
            {
                if (result.FormatVersion != LinkCollector.FormatVersion)
                    continue;
                var indices = link.Origins.Select(origin => origin.DownloadItemIndex).OfType<int>().Distinct().ToArray();
                var labels = result.Downloads.Where(item => indices.Contains(item.Index)).Select(item => item.Label).Distinct();
                var columns = new[] { result.Id.ToString(), result.Title, result.Status, string.Join('|', indices),
                    string.Join('|', labels), link.Url, link.ExtractionCode, link.ArchivePassword,
                    string.Join('|', link.Origins.Select(origin => origin.Kind).Distinct()),
                    string.Join('|', link.Origins.Select(origin => origin.Source).Distinct()) };
                await writer.WriteLineAsync(string.Join(',', columns.Select(Csv))).ConfigureAwait(false);
            }
        await writer.FlushAsync().ConfigureAwait(false);
        writer.Close();
        File.Move(path + ".tmp", path, true);
    }

    private static string Csv(string? value) => '"' + (value ?? "").Replace("\"", "\"\"") + '"';
}
