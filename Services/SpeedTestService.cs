using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

namespace TcpHardwareCheck.Services;

// Primary: Cloudflare's speed test endpoints (the same ones behind speed.cloudflare.com and the
// @cloudflare/speedtest npm package) — documented and stable, no scraping a token out of an
// obfuscated JS bundle like fast.com required. Verified live before switching: GET
// .../__down?bytes=N returns exactly N bytes, POST .../__up accepts and discards any body.
//
// Fallback: M-Lab's NDT7 (locate.measurementlab.net + a WebSocket protocol) — confirmed live that
// Cloudflare's public endpoint has no documented rate-limit SLA and does start rejecting requests
// (429) after enough test volume from one IP. Falling back to a second, independently-operated
// provider means a temporary block on one doesn't take down the whole speed test.
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

    private const string CloudflareDownloadUrl = "https://speed.cloudflare.com/__down?bytes=10000000";
    private const string CloudflareUploadUrl = "https://speed.cloudflare.com/__up";

    private const string Ndt7LocateUrl = "https://locate.measurementlab.net/v2/nearest/ndt/ndt7";
    private const string Ndt7SubProtocol = "net.measurementlab.ndt.v7";
    private const int Ndt7MaxMessageBytes = 8_388_608; // 8MB, matches the ndt7-js reference client

    private static readonly HttpClient Http = new HttpClient();
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(5);

    public static async Task<(double DownMbps, double UpMbps)> MeasureAsync()
    {
        try
        {
            var down = await MeasureCloudflareAsync(isUpload: false);
            var up = await MeasureCloudflareAsync(isUpload: true);
            return (down, up);
        }
        catch (Exception)
        {
            // Both directions fall back together: a rate limit or outage on Cloudflare's side
            // almost always affects both, so there's no point retrying the same broken provider
            // for the second leg.
            return await MeasureNdt7Async();
        }
    }

    private static async Task<double> MeasureCloudflareAsync(bool isUpload)
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
        var bytes = await Http.GetByteArrayAsync(CloudflareDownloadUrl);
        return bytes.LongLength;
    }

    private static async Task<long> UploadChunkAsync()
    {
        var payload = new byte[UploadChunkBytes];
        Random.Shared.NextBytes(payload);
        using var content = new ByteArrayContent(payload);
        await Http.PostAsync(CloudflareUploadUrl, content);
        return payload.LongLength;
    }

    private static async Task<(double DownMbps, double UpMbps)> MeasureNdt7Async()
    {
        var (downloadUrl, uploadUrl) = await GetNdt7UrlsAsync();
        var down = await Ndt7DownloadAsync(downloadUrl);
        var up = await Ndt7UploadAsync(uploadUrl);
        return (down, up);
    }

    private static async Task<(string DownloadUrl, string UploadUrl)> GetNdt7UrlsAsync()
    {
        var json = await Http.GetStringAsync(Ndt7LocateUrl);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("M-Lab locate service returned no NDT7 servers");
        }

        var urls = results[0].GetProperty("urls");
        return (
            urls.GetProperty("wss:///ndt/v7/download").GetString()
                ?? throw new InvalidOperationException("NDT7 locate result missing a download URL"),
            urls.GetProperty("wss:///ndt/v7/upload").GetString()
                ?? throw new InvalidOperationException("NDT7 locate result missing an upload URL"));
    }

    // Single connection, unlike the 4-stream Cloudflare approach — ndt7 measures true throughput
    // via the kernel's own BBR bandwidth estimate (read from the server's periodic measurement
    // messages below) rather than needing extra streams to outrun a per-connection cap.
    private static async Task<double> Ndt7DownloadAsync(string url)
    {
        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(Ndt7SubProtocol);
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ws.ConnectAsync(new Uri(url), connectCts.Token);

        using var testCts = new CancellationTokenSource(TestDuration + TimeSpan.FromSeconds(5));
        var buffer = new byte[65536];
        var textBuffer = new List<byte>();
        long totalBytes = 0;
        double lastBbrMbps = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (stopwatch.Elapsed < TestDuration && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, testCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                totalBytes += result.Count;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    textBuffer.AddRange(buffer.Take(result.Count));
                    if (result.EndOfMessage)
                    {
                        lastBbrMbps = TryParseBbrMbps(textBuffer.ToArray()) ?? lastBbrMbps;
                        textBuffer.Clear();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Test-duration/safety timeout — fine, we already have whatever was measured so far.
        }

        await CloseNdt7SocketAsync(ws);

        if (lastBbrMbps > 0)
        {
            return Math.Round(lastBbrMbps, 1);
        }

        return Math.Round(totalBytes * 8.0 / stopwatch.Elapsed.TotalSeconds / 1_000_000, 1);
    }

    private static double? TryParseBbrMbps(byte[] jsonBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            if (doc.RootElement.TryGetProperty("BBRInfo", out var bbrInfo)
                && bbrInfo.TryGetProperty("BW", out var bwEl)
                && bwEl.TryGetInt64(out var bwBytesPerSecond))
            {
                return bwBytesPerSecond * 8.0 / 1_000_000;
            }
        }
        catch (JsonException)
        {
            // A text frame split across multiple ReceiveAsync calls in a way that didn't
            // reassemble into valid JSON — skip it, the next measurement message a few hundred
            // milliseconds later covers for it.
        }

        return null;
    }

    // No concurrency here, unlike the download side: BBR (read above) is measured by whichever
    // side is sending, so for upload the server's own BBRInfo would just reflect its trivial ack
    // traffic, not the client's real speed — client-side byte counting is what actually matters,
    // same as the Cloudflare path above.
    private static async Task<double> Ndt7UploadAsync(string url)
    {
        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(Ndt7SubProtocol);
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ws.ConnectAsync(new Uri(url), connectCts.Token);

        using var drainCts = new CancellationTokenSource();
        var drainTask = DrainNdt7ServerMessagesAsync(ws, drainCts.Token);

        var buffer = new byte[Ndt7MaxMessageBytes];
        Random.Shared.NextBytes(buffer);

        var messageSize = 8192;
        var sentAtCurrentSize = 0;
        long totalBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TestDuration && ws.State == WebSocketState.Open)
        {
            await ws.SendAsync(buffer.AsMemory(0, messageSize), WebSocketMessageType.Binary, true, CancellationToken.None);
            totalBytes += messageSize;
            sentAtCurrentSize++;

            // Mirrors ndt7-js's own upload loop: grow the message size gradually (doubling every
            // 16 sends) instead of jumping straight to a huge size — a fixed huge message would
            // badly undersample a slow connection, a fixed tiny one would never fill a fast one.
            if (messageSize < Ndt7MaxMessageBytes && sentAtCurrentSize >= 16)
            {
                messageSize = Math.Min(messageSize * 2, Ndt7MaxMessageBytes);
                sentAtCurrentSize = 0;
            }
        }

        drainCts.Cancel();
        try
        {
            await drainTask;
        }
        catch (OperationCanceledException)
        {
            // Expected — we just cancelled it above.
        }

        await CloseNdt7SocketAsync(ws);

        return Math.Round(totalBytes * 8.0 / stopwatch.Elapsed.TotalSeconds / 1_000_000, 1);
    }

    // The server sends small ack/measurement messages back during an upload; nothing needs to
    // read them for our purposes, but they still have to be drained or the socket's receive
    // buffer backs up and can stall the send side.
    private static async Task DrainNdt7ServerMessagesAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected once the upload loop finishes and cancels this.
        }
        catch (WebSocketException)
        {
            // Socket torn down from the other side (CloseNdt7SocketAsync below, or the server) —
            // fine, the upload loop above is what actually determines the measured result.
        }
    }

    private static async Task CloseNdt7SocketAsync(ClientWebSocket ws)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, closeCts.Token);
            }
        }
        catch (Exception)
        {
            // Best-effort close — the test's own numbers are already computed by this point.
        }
    }
}
