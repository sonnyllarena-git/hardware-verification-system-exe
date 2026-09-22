using System.Diagnostics;

namespace TcpHardwareCheck.Services;

// Uses Cloudflare's speed test endpoints (the same ones behind speed.cloudflare.com and the
// @cloudflare/speedtest npm package) instead of fast.com — documented and stable, no scraping a
// token out of an obfuscated JS bundle. Verified live before switching: GET .../__down?bytes=N
// returns exactly N bytes, POST .../__up accepts and discards any body.
public static class SpeedTestService
{
    // One connection per test badly underestimates fast connections (same lesson learned from
    // fast.com's single-stream-per-target design) — open several concurrent streams to approach
    // the real link capacity instead of whatever one connection happens to sustain.
    private const int Streams = 4;

    // Upload chunk size is deliberately much smaller than the download chunk (10MB below): a
    // large POST body can be handed to the OS's socket send buffer almost instantly regardless
    // of the real uplink speed, so timing one big request start-to-response can badly overstate
    // upload throughput — confirmed live: a naive single/few-chunk browser test read ~93 Mbps
    // upload on a connection where a reference client tracking real bytes-in-flight (M-Lab's
    // ndt7, which watches WebSocket bufferedAmount rather than trusting request/response timing)
    // read ~24 Mbps on the same line. Many small round trips are far less likely to be entirely
    // absorbed by buffering, since each one has to actually complete for the loop to continue —
    // the same reasoning ndt7's own upload algorithm uses to grow message size gradually rather
    // than send one large blob.
    private const int UploadChunkBytes = 262_144;

    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=10000000";
    private const string UploadUrl = "https://speed.cloudflare.com/__up";

    private static readonly HttpClient Http = new HttpClient();
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(5);

    public static async Task<(double DownMbps, double UpMbps)> MeasureAsync()
    {
        var down = await MeasureAsync(isUpload: false);
        var up = await MeasureAsync(isUpload: true);
        return (down, up);
    }

    private static async Task<double> MeasureAsync(bool isUpload)
    {
        long totalBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, Streams).Select(async _ =>
        {
            while (stopwatch.Elapsed < TestDuration)
            {
                var sent = isUpload ? await UploadChunkAsync() : await DownloadChunkAsync();
                Interlocked.Add(ref totalBytes, sent);
            }
        });
        await Task.WhenAll(tasks);

        return Math.Round(totalBytes * 8.0 / stopwatch.Elapsed.TotalSeconds / 1_000_000, 1);
    }

    private static async Task<long> DownloadChunkAsync()
    {
        var bytes = await Http.GetByteArrayAsync(DownloadUrl);
        return bytes.LongLength;
    }

    private static async Task<long> UploadChunkAsync()
    {
        var payload = new byte[UploadChunkBytes];
        Random.Shared.NextBytes(payload);
        using var content = new ByteArrayContent(payload);
        await Http.PostAsync(UploadUrl, content);
        return payload.LongLength;
    }
}
