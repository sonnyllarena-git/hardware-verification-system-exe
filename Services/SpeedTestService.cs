using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TcpHardwareCheck.Services;

// Uses fast.com's undocumented API (the same mechanism community tools like `fast-cli` rely on):
// scrape a token out of fast.com's JS bundle, request CDN target URLs, then GET (download) /
// POST (upload) against them. There is no official SLA for this — Netflix can change or remove
// it without notice, which would silently break this class (empty/failed requests, not a crash).
public static class SpeedTestService
{
    private static readonly HttpClient Http = new HttpClient();
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(5);

    public static async Task<(double DownMbps, double UpMbps)> MeasureAsync()
    {
        var urls = await GetTargetUrlsAsync();
        var down = await MeasureAsync(urls, isUpload: false);
        var up = await MeasureAsync(urls, isUpload: true);
        return (down, up);
    }

    private static async Task<List<string>> GetTargetUrlsAsync()
    {
        var html = await Http.GetStringAsync("https://fast.com/");
        var scriptPath = Regex.Match(html, "/app-[^\"]+\\.js").Value;
        var script = await Http.GetStringAsync($"https://fast.com{scriptPath}");
        var token = Regex.Match(script, "token:\"(?<token>[^\"]+)\"").Groups["token"].Value;

        var json = await Http.GetStringAsync(
            $"https://api.fast.com/netflix/speedtest/v2?https=true&token={token}&urlCount=3");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("targets")
            .EnumerateArray()
            .Select(target => target.GetProperty("url").GetString()
                ?? throw new InvalidOperationException("fast.com target missing a url"))
            .ToList();
    }

    private static async Task<double> MeasureAsync(List<string> urls, bool isUpload)
    {
        long totalBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        var tasks = urls.Select(async url =>
        {
            while (stopwatch.Elapsed < TestDuration)
            {
                var sent = isUpload ? await UploadChunkAsync(url) : await DownloadChunkAsync(url);
                Interlocked.Add(ref totalBytes, sent);
            }
        });
        await Task.WhenAll(tasks);

        return Math.Round(totalBytes * 8.0 / stopwatch.Elapsed.TotalSeconds / 1_000_000, 1);
    }

    private static async Task<long> DownloadChunkAsync(string url)
    {
        var bytes = await Http.GetByteArrayAsync(url);
        return bytes.LongLength;
    }

    private static async Task<long> UploadChunkAsync(string url)
    {
        var payload = new byte[1_000_000];
        Random.Shared.NextBytes(payload);
        using var content = new ByteArrayContent(payload);
        await Http.PostAsync(url, content);
        return payload.LongLength;
    }
}
