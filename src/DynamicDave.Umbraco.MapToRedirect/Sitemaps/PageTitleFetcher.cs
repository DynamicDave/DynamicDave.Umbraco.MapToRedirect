using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

/// <summary>Fetches the titles of old pages, server-side, with the same redirect and internal-address rules as the sitemap reader.</summary>
public class PageTitleFetcher
{
    private const int MaxConcurrency = 4;
    private const int MaxRedirects = 5;
    private const int MaxHtmlBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<HttpClient> _client;
    private readonly Func<bool> _allowPrivate;
    private readonly Func<string, IPAddress[]> _resolve;
    private readonly TimeSpan _timeout;

    public PageTitleFetcher(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<MapToRedirectOptions> options,
        IHostEnvironment environment)
    {
        _client = () => httpClientFactory.CreateClient(Constants.HttpClientName);
        _allowPrivate = () => options.CurrentValue.AllowPrivateNetworkTargets ?? environment.IsDevelopment();
        _resolve = Dns.GetHostAddresses;
        _timeout = DefaultTimeout;
    }

    internal PageTitleFetcher(HttpMessageHandler handler, Func<string, IPAddress[]> resolve, bool allowPrivate, TimeSpan? timeout = null)
    {
        var client = new HttpClient(handler, disposeHandler: false);
        _client = () => client;
        _allowPrivate = () => allowPrivate;
        _resolve = resolve;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// Fetches every URL (4 at a time, 15 s each for headers and body together) and returns its title, or null when the page
    /// could not be fetched or has none. <paramref name="onDone"/> is called once per URL, from any thread.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string?>> FetchAsync(IReadOnlyList<string> urls, Action<int> onDone, CancellationToken ct)
    {
        var client = _client();
        var allowPrivate = _allowPrivate();
        var results = new Dictionary<string, string?>(StringComparer.Ordinal);
        var distinct = urls.Distinct(StringComparer.Ordinal).ToList();
        foreach (var url in distinct)
        {
            results[url] = null;
        }

        var done = 0;
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var fetched = await Task.WhenAll(distinct.Select(async url =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return (Url: url, Title: await FetchOneAsync(client, url, allowPrivate, ct));
            }
            finally
            {
                gate.Release();
                onDone(Interlocked.Increment(ref done));
            }
        }));

        foreach (var (url, title) in fetched)
        {
            results[url] = title;
        }

        return results;
    }

    private async Task<string?> FetchOneAsync(HttpClient client, string url, bool allowPrivate, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(_timeout);

            using var response = await SafeHttp.GetAsync(client, uri, allowPrivate, _resolve, MaxRedirects, _timeout, budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(budget.Token);
            var buffer = new byte[MaxHtmlBytes];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), budget.Token)) > 0)
            {
                total += read;
            }

            return TitleText.Extract(Decode(buffer.AsSpan(0, total), response.Content.Headers.ContentType?.CharSet));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Any failure of one page (network, hostile redirect, parsing) only means "no title" for that page.
            return null;
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes, string? headerCharset)
    {
        var encoding = FindEncoding(headerCharset) ?? FindEncoding(SniffMetaCharset(bytes)) ?? Encoding.UTF8;
        return encoding.GetString(bytes);
    }

    private static Encoding? FindEncoding(string? charset)
    {
        var name = charset?.Trim().Trim('"', '\'').Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(name) ?? Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads the charset of a <c>&lt;meta charset&gt;</c> or http-equiv Content-Type tag in the first 4 KB.</summary>
    private static string? SniffMetaCharset(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, 4096)]);
        var index = head.IndexOf("charset", StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var i = index + "charset".Length;
            while (i < head.Length && head[i] == ' ')
            {
                i++;
            }

            if (i < head.Length && head[i] == '=')
            {
                i++;
                while (i < head.Length && head[i] is ' ' or '"' or '\'')
                {
                    i++;
                }

                var start = i;
                while (i < head.Length && (char.IsLetterOrDigit(head[i]) || head[i] is '-' or '_' or ':' or '.'))
                {
                    i++;
                }

                return i > start ? head[start..i] : null;
            }

            index = head.IndexOf("charset", index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }
}
