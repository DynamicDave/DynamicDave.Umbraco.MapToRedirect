using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using DynamicDave.Umbraco.MapToRedirect.Urls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

public enum SitemapError
{
    Unreachable,
    NotASitemap,
    InternalAddress,
}

public enum SitemapLimit
{
    Files,
    Urls,
    FileSize,
}

/// <param name="Urls">Absolute URLs, distinct by normalized path, in document order.</param>
/// <param name="LimitsReached">Limits that cut the read short.</param>
/// <param name="Error">Set only when nothing could be read.</param>
public record SitemapReadResult(IReadOnlyList<string> Urls, IReadOnlyList<SitemapLimit> LimitsReached, SitemapError? Error);

/// <summary>Reads the URLs of an old website's sitemap(s), following sitemap indexes, server-side and with SSRF protection.</summary>
public class SitemapReader
{
    private const int MaxFiles = 50;
    private const int MaxUrls = 50_000;
    private const int MaxFileBytes = 50 * 1024 * 1024;
    private const int MaxRedirects = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<HttpClient> _client;
    private readonly Func<bool> _allowPrivate;
    private readonly Func<string, IPAddress[]> _resolve;
    private readonly int _maxUrls;
    private readonly int _maxFileBytes;
    private readonly ILogger? _logger;

    public SitemapReader(
        IHttpClientFactory httpClientFactory,
        ILogger<SitemapReader> logger,
        IOptionsMonitor<MapToRedirectOptions> options,
        IHostEnvironment environment)
    {
        _client = () => httpClientFactory.CreateClient(Constants.HttpClientName);
        _allowPrivate = () => options.CurrentValue.AllowPrivateNetworkTargets ?? environment.IsDevelopment();
        _resolve = Dns.GetHostAddresses;
        _maxUrls = MaxUrls;
        _maxFileBytes = MaxFileBytes;
        _logger = logger;
    }

    internal SitemapReader(
        HttpMessageHandler handler,
        Func<string, IPAddress[]> resolve,
        bool allowPrivate,
        int maxUrls = MaxUrls,
        int maxFileBytes = MaxFileBytes)
    {
        var client = new HttpClient(handler, disposeHandler: false);
        _client = () => client;
        _allowPrivate = () => allowPrivate;
        _resolve = resolve;
        _maxUrls = maxUrls;
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>
    /// Reads <paramref name="input"/>: a sitemap URL, or a domain / site URL without a path (robots.txt
    /// <c>Sitemap:</c> lines first, then <c>/sitemap.xml</c>). <paramref name="onUrlCount"/> reports the distinct URLs found so far.
    /// </summary>
    public async Task<SitemapReadResult> ReadAsync(string input, Action<int>? onUrlCount, CancellationToken ct)
    {
        if (!TryParseInput(input, out var start))
        {
            return new SitemapReadResult([], [], SitemapError.Unreachable);
        }

        var client = _client();
        var allowPrivate = _allowPrivate();
        var state = new ReadState(onUrlCount);

        var queue = new Queue<Uri>();
        if (start.AbsolutePath is "/" or "")
        {
            foreach (var found in await RobotsSitemapsAsync(client, start, allowPrivate, state, ct))
            {
                queue.Enqueue(found);
            }

            if (queue.Count == 0 && state.Error is null)
            {
                queue.Enqueue(new Uri(start, "/sitemap.xml"));
            }
        }
        else
        {
            queue.Enqueue(start);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesRead = 0;
        while (queue.Count > 0 && !state.UrlLimitHit)
        {
            ct.ThrowIfCancellationRequested();
            var next = queue.Dequeue();
            if (!visited.Add(next.AbsoluteUri))
            {
                continue;
            }

            if (filesRead >= MaxFiles)
            {
                state.Limits.Add(SitemapLimit.Files);
                break;
            }

            filesRead++;
            await ReadFileAsync(client, next, allowPrivate, queue, state, ct);
        }

        state.OnUrlCount?.Invoke(state.Urls.Count);
        var error = state.Urls.Count == 0 ? state.Error ?? SitemapError.NotASitemap : (SitemapError?)null;
        return new SitemapReadResult(state.Urls, state.Limits.OrderBy(l => l).ToList(), error);
    }

    private static bool TryParseInput(string input, out Uri uri)
    {
        var value = input.Trim();
        if (value.Length == 0)
        {
            uri = null!;
            return false;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out uri!)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && uri.Host.Length > 0;
    }

    private async Task<List<Uri>> RobotsSitemapsAsync(HttpClient client, Uri site, bool allowPrivate, ReadState state, CancellationToken ct)
    {
        var found = new List<Uri>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            var robots = new Uri(site, "/robots.txt");
            using var response = await SafeHttp.GetAsync(client, robots, allowPrivate, _resolve, MaxRedirects, RequestTimeout, ct);
            if (!response.IsSuccessStatusCode)
            {
                return found;
            }

            // robots.txt is small; never read more than 1 MB of it.
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[1024 * 1024];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), timeout.Token)) > 0)
            {
                total += read;
            }

            foreach (var line in System.Text.Encoding.UTF8.GetString(buffer, 0, total).Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase)
                    && Uri.TryCreate(trimmed["sitemap:".Length..].Trim(), UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    found.Add(uri);
                }
            }
        }
        catch (PrivateNetworkBlockedException)
        {
            state.Error = SitemapError.InternalAddress;
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            // No usable robots.txt: fall back to /sitemap.xml.
            _logger?.LogDebug(ex, "robots.txt of {Host} could not be read", site.Host);
        }

        return found;
    }

    private async Task ReadFileAsync(HttpClient client, Uri url, bool allowPrivate, Queue<Uri> queue, ReadState state, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);

            using var response = await SafeHttp.GetAsync(client, url, allowPrivate, _resolve, MaxRedirects, RequestTimeout, ct);
            if (!response.IsSuccessStatusCode)
            {
                state.Fail(SitemapError.Unreachable);
                return;
            }

            await using var raw = await response.Content.ReadAsStreamAsync(timeout.Token);
            // Sniff the gzip magic number: covers .gz files and Content-Encoding: gzip bodies the handler left compressed.
            var prefix = new byte[2];
            var prefixLength = await raw.ReadAtLeastAsync(prefix, 2, throwOnEndOfStream: false, timeout.Token);
            await using var buffered = new PrefixedStream(prefix.AsMemory(0, prefixLength).ToArray(), raw);
            var gzip = prefixLength == 2 && prefix[0] == 0x1f && prefix[1] == 0x8b;
            await using Stream plain = gzip ? new GZipStream(buffered, CompressionMode.Decompress) : buffered;
            await using var limited = new LimitedStream(plain, _maxFileBytes);

            await ParseAsync(limited, response.RequestMessage?.RequestUri ?? url, queue, state, timeout.Token);
        }
        catch (PrivateNetworkBlockedException)
        {
            state.Fail(SitemapError.InternalAddress);
        }
        catch (FileSizeLimitException)
        {
            state.Limits.Add(SitemapLimit.FileSize);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            state.Fail(SitemapError.NotASitemap);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            _logger?.LogDebug(ex, "Sitemap {Url} could not be read", url);
            state.Fail(SitemapError.Unreachable);
        }
    }

    private async Task ParseAsync(Stream stream, Uri baseUri, Queue<Uri> queue, ReadState state, CancellationToken ct)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        using var reader = XmlReader.Create(stream, settings);
        if (!await MoveToRootAsync(reader))
        {
            throw new XmlException("No root element.");
        }

        var isIndex = reader.LocalName == "sitemapindex";
        if (!isIndex && reader.LocalName != "urlset")
        {
            throw new XmlException("Not a sitemap.");
        }

        var depth = reader.Depth;
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "loc" || reader.Depth != depth + 2)
            {
                continue;
            }

            var loc = (await reader.ReadElementContentAsStringAsync()).Trim();
            if (!Uri.TryCreate(baseUri, loc, out var target)
                || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            if (isIndex)
            {
                queue.Enqueue(target);
            }
            else if (!state.Add(target.AbsoluteUri, _maxUrls))
            {
                return;
            }
        }
    }

    private static async Task<bool> MoveToRootAsync(XmlReader reader)
    {
        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or SocketException or TimeoutException or IOException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private sealed class ReadState(Action<int>? onUrlCount)
    {
        private readonly HashSet<string> _seen = [];

        public Action<int>? OnUrlCount { get; } = onUrlCount;

        public List<string> Urls { get; } = [];

        public HashSet<SitemapLimit> Limits { get; } = [];

        public SitemapError? Error { get; set; }

        public bool UrlLimitHit { get; private set; }

        /// <summary>Adds a URL unless its normalized path is known. Returns false once the URL limit is hit.</summary>
        public bool Add(string url, int maxUrls)
        {
            var path = UrlNormalizer.Normalize(url);
            if (path is null || !_seen.Add(path))
            {
                return true;
            }

            if (Urls.Count >= maxUrls)
            {
                Limits.Add(SitemapLimit.Urls);
                UrlLimitHit = true;
                return false;
            }

            Urls.Add(url);
            if (Urls.Count % 100 == 0)
            {
                OnUrlCount?.Invoke(Urls.Count);
            }

            return true;
        }

        /// <summary>Keeps the most telling error: an internal-address refusal wins over the others.</summary>
        public void Fail(SitemapError error)
        {
            if (Error is null || error == SitemapError.InternalAddress)
            {
                Error = error;
            }
        }
    }

    private sealed class FileSizeLimitException : IOException;

    /// <summary>Counts the bytes read from the (decompressed) stream and throws when the cap is exceeded.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _total;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _total += read;
            if (_total > limit)
            {
                throw new FileSizeLimitException();
            }

            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Replays already-read bytes, then continues with the inner stream.</summary>
    private sealed class PrefixedStream(byte[] prefix, Stream inner) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsSpan(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            return inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < prefix.Length)
            {
                return ValueTask.FromResult(Read(buffer.Span));
            }

            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
