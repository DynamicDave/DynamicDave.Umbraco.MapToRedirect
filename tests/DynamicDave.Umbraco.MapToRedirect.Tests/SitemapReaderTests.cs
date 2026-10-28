using System.IO.Compression;
using System.Net;
using System.Text;
using DynamicDave.Umbraco.MapToRedirect.Sitemaps;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class SitemapReaderTests
{
    private static readonly IPAddress[] Public = [IPAddress.Parse("93.184.216.34")];

    private static SitemapReader Reader(FakeHttpHandler handler, bool allowPrivate = false, int maxUrls = 50_000, Func<string, IPAddress[]>? resolve = null) =>
        new(handler, resolve ?? (_ => Public), allowPrivate, maxUrls);

    private static string UrlSet(params string[] locs) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + string.Concat(locs.Select(l => $"<url><loc>{l}</loc></url>")) + "</urlset>";

    private static string Index(params string[] locs) =>
        "<?xml version=\"1.0\"?><sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + string.Concat(locs.Select(l => $"<sitemap><loc>{l}</loc></sitemap>")) + "</sitemapindex>";

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static FakeHttpHandler RedirectChain(int redirects)
    {
        var handler = new FakeHttpHandler();
        for (var i = 0; i < redirects; i++)
        {
            handler.Redirect($"https://old.example/r{i}", $"https://old.example/r{i + 1}");
        }

        return handler.Text($"https://old.example/r{redirects}", UrlSet("https://old.example/a"));
    }

    [Fact]
    public async Task Reads_urlset()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", UrlSet("https://old.example/a", "https://old.example/b"));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Null(result.Error);
        Assert.Equal(["https://old.example/a", "https://old.example/b"], result.Urls);
        Assert.Empty(result.LimitsReached);
    }

    [Fact]
    public async Task Follows_sitemap_index()
    {
        var handler = new FakeHttpHandler()
            .Text("https://old.example/sitemap.xml", Index("https://old.example/s1.xml", "https://old.example/s2.xml"))
            .Text("https://old.example/s1.xml", UrlSet("https://old.example/a"))
            .Text("https://old.example/s2.xml", UrlSet("https://old.example/b"));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(["https://old.example/a", "https://old.example/b"], result.Urls);
    }

    [Fact]
    public async Task Reads_gzip_sitemap()
    {
        var handler = new FakeHttpHandler().Bytes("https://old.example/sitemap.xml.gz", Gzip(UrlSet("https://old.example/a")));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml.gz", null, default);

        Assert.Null(result.Error);
        Assert.Equal(["https://old.example/a"], result.Urls);
    }

    [Fact]
    public async Task Reads_gzip_body_without_gz_extension()
    {
        var handler = new FakeHttpHandler().Bytes("https://old.example/sitemap.xml", Gzip(UrlSet("https://old.example/a")), "application/xml");

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(["https://old.example/a"], result.Urls);
    }

    [Fact]
    public async Task Domain_input_uses_robots_txt_sitemap_lines()
    {
        var handler = new FakeHttpHandler()
            .Text("https://old.example/robots.txt", "User-agent: *\nDisallow: /x\nSitemap: https://old.example/custom.xml\n", "text/plain")
            .Text("https://old.example/custom.xml", UrlSet("https://old.example/a"))
            .Text("https://old.example/sitemap.xml", UrlSet("https://old.example/wrong"));

        var result = await Reader(handler).ReadAsync("old.example", null, default);

        Assert.Equal(["https://old.example/a"], result.Urls);
        Assert.DoesNotContain("https://old.example/sitemap.xml", handler.Requests);
    }

    [Fact]
    public async Task Domain_input_falls_back_to_sitemap_xml()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", UrlSet("https://old.example/a"));

        var result = await Reader(handler).ReadAsync("https://old.example", null, default);

        Assert.Equal(["https://old.example/a"], result.Urls);
        Assert.Contains("https://old.example/robots.txt", handler.Requests);
    }

    [Fact]
    public async Task Duplicate_paths_are_merged()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", UrlSet("https://old.example/a", "https://old.example/A/", "https://old.example/b"));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(["https://old.example/a", "https://old.example/b"], result.Urls);
    }

    [Fact]
    public async Task Stops_at_url_limit()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", UrlSet("https://old.example/a", "https://old.example/b", "https://old.example/c"));

        var result = await Reader(handler, maxUrls: 2).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Null(result.Error);
        Assert.Equal(["https://old.example/a", "https://old.example/b"], result.Urls);
        Assert.Contains(SitemapLimit.Urls, result.LimitsReached);
    }

    [Fact]
    public async Task Stops_at_file_limit()
    {
        var handler = new FakeHttpHandler()
            .Text("https://old.example/sitemap.xml", Index(Enumerable.Range(1, 51).Select(i => $"https://old.example/s{i}.xml").ToArray()));
        foreach (var i in Enumerable.Range(1, 51))
        {
            handler.Text($"https://old.example/s{i}.xml", UrlSet($"https://old.example/p{i}"));
        }

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Contains(SitemapLimit.Files, result.LimitsReached);
        Assert.Equal(49, result.Urls.Count); // 50 files in total: the index itself and 49 children
        Assert.DoesNotContain("https://old.example/s50.xml", handler.Requests);
    }

    [Fact]
    public async Task Stops_at_file_size_limit_counting_decompressed_bytes()
    {
        var big = UrlSet(Enumerable.Range(0, 20).Select(i => $"https://old.example/{i}").ToArray()) + new string(' ', 5000);
        var handler = new FakeHttpHandler().Bytes("https://old.example/sitemap.xml", Gzip(big), "application/xml");
        Assert.True(Gzip(big).Length < 400); // the compressed file alone would fit in the cap

        var result = await new SitemapReader(handler, _ => Public, false, 50_000, maxFileBytes: 400)
            .ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Contains(SitemapLimit.FileSize, result.LimitsReached);
        Assert.True(result.Urls.Count < 20);
    }

    [Fact]
    public async Task Html_page_is_not_a_sitemap()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", "<!DOCTYPE html><html><body>Hi</body></html>", "text/html");

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(SitemapError.NotASitemap, result.Error);
        Assert.Empty(result.Urls);
    }

    [Fact]
    public async Task Doctype_with_external_entity_is_refused_and_not_resolved()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE urlset [<!ENTITY x SYSTEM \"http://evil.example/x\">]><urlset><url><loc>&x;</loc></url></urlset>";
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", xml);

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(SitemapError.NotASitemap, result.Error);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("evil.example"));
    }

    [Fact]
    public async Task Unreachable_host()
    {
        var handler = new FakeHttpHandler().Add("https://old.example/sitemap.xml", () => throw new HttpRequestException("boom"));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(SitemapError.Unreachable, result.Error);
    }

    [Fact]
    public async Task Not_found_is_unreachable()
    {
        var result = await Reader(new FakeHttpHandler()).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(SitemapError.Unreachable, result.Error);
    }

    [Fact]
    public async Task Redirect_to_internal_address_is_refused()
    {
        var handler = new FakeHttpHandler()
            .Redirect("https://old.example/sitemap.xml", "http://169.254.169.254/latest")
            .Text("http://169.254.169.254/latest", UrlSet("https://old.example/a"));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(SitemapError.InternalAddress, result.Error);
        Assert.DoesNotContain("http://169.254.169.254/latest", handler.Requests);
    }

    [Fact]
    public async Task Host_resolving_to_any_internal_address_is_refused()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", UrlSet("https://old.example/a"));

        var result = await Reader(handler, resolve: _ => [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.5")])
            .ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Equal(SitemapError.InternalAddress, result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Internal_address_is_allowed_when_configured()
    {
        var handler = new FakeHttpHandler().Text("http://localhost/sitemap.xml", UrlSet("http://localhost/a"));

        var result = await Reader(handler, allowPrivate: true, resolve: _ => [IPAddress.Loopback])
            .ReadAsync("http://localhost/sitemap.xml", null, default);

        Assert.Equal(["http://localhost/a"], result.Urls);
    }

    [Fact]
    public async Task Follows_at_most_five_redirects()
    {
        var five = await Reader(RedirectChain(5)).ReadAsync("https://old.example/r0", null, default);
        var six = await Reader(RedirectChain(6)).ReadAsync("https://old.example/r0", null, default);

        Assert.Null(five.Error);
        Assert.Equal(["https://old.example/a"], five.Urls);
        Assert.Equal(SitemapError.Unreachable, six.Error);
    }

    [Fact]
    public async Task Reports_url_count()
    {
        var handler = new FakeHttpHandler().Text("https://old.example/sitemap.xml", UrlSet("https://old.example/a", "https://old.example/b"));
        var counts = new List<int>();

        await Reader(handler).ReadAsync("https://old.example/sitemap.xml", counts.Add, default);

        Assert.Equal(2, counts[^1]);
    }

    [Fact]
    public async Task Stalled_body_times_out()
    {
        var handler = new FakeHttpHandler().Add("https://old.example/sitemap.xml", () =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream("<?xml version=\"1.0\"?><urlset>"u8.ToArray())) });
        var reader = new SitemapReader(handler, _ => Public, false, timeout: TimeSpan.FromMilliseconds(200));

        var read = reader.ReadAsync("https://old.example/sitemap.xml", null, default);
        var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(read, finished);
        Assert.Equal(SitemapError.Unreachable, (await read).Error);
    }

    [Fact]
    public async Task Index_children_beyond_the_file_limit_are_never_queued_or_requested()
    {
        var handler = new FakeHttpHandler()
            .Text("https://old.example/sitemap.xml", Index(Enumerable.Range(1, 200).Select(i => $"https://old.example/s{i}.xml").ToArray()));

        var result = await Reader(handler).ReadAsync("https://old.example/sitemap.xml", null, default);

        Assert.Contains(SitemapLimit.Files, result.LimitsReached);
        Assert.Equal(50, handler.Requests.Count); // the index itself and 49 children
    }

    /// <summary>Delivers the given bytes, then never completes (honours cancellation like a real socket stream).</summary>
    private sealed class StallingStream(byte[] head) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < head.Length)
            {
                var n = Math.Min(buffer.Length, head.Length - _position);
                head.AsSpan(_position, n).CopyTo(buffer.Span);
                _position += n;
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
