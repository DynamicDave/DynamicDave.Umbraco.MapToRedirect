using System.Net;
using DynamicDave.Umbraco.MapToRedirect.Sitemaps;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class PageTitleFetcherTests
{
    private static readonly IPAddress[] Public = [IPAddress.Parse("93.184.216.34")];

    private sealed class HangingHandler(FakeHttpHandler inner, string hangUrl) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == hangUrl)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CountingHandler(Action enter, Action leave) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            enter();
            await Task.Delay(50, cancellationToken);
            leave();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<title>t</title>") };
        }
    }

    [Fact]
    public async Task Fetches_titles_and_nulls_failures()
    {
        var inner = new FakeHttpHandler()
            .Text("https://old.example/a", "<title>Pagina A | Klant</title>", "text/html")
            .Text("https://old.example/b", "<h1>Pagina B</h1>", "text/html")
            .Text("https://old.example/c", "<p>no title</p>", "text/html");
        var fetcher = new PageTitleFetcher(new HangingHandler(inner, "https://old.example/slow"), _ => Public, false, TimeSpan.FromMilliseconds(300));
        var done = 0;
        string[] urls = ["https://old.example/a", "https://old.example/b", "https://old.example/c", "https://old.example/slow"];

        var result = await fetcher.FetchAsync(urls, _ => Interlocked.Increment(ref done), default);

        Assert.Equal(4, done);
        Assert.Equal("Pagina A", result["https://old.example/a"]);
        Assert.Equal("Pagina B", result["https://old.example/b"]);
        Assert.Null(result["https://old.example/c"]);
        Assert.Null(result["https://old.example/slow"]);
    }

    [Fact]
    public async Task Failing_status_and_internal_address_give_null()
    {
        var inner = new FakeHttpHandler();
        var fetcher = new PageTitleFetcher(inner, host => host == "internal.example" ? [IPAddress.Loopback] : Public, false, TimeSpan.FromSeconds(5));

        var result = await fetcher.FetchAsync(["https://old.example/missing", "https://internal.example/x"], _ => { }, default);

        Assert.Null(result["https://old.example/missing"]);
        Assert.Null(result["https://internal.example/x"]);
        Assert.DoesNotContain("https://internal.example/x", inner.Requests);
    }

    [Fact]
    public async Task Runs_at_most_four_at_a_time()
    {
        var current = 0;
        var max = 0;
        var handler = new CountingHandler(
            () =>
            {
                var now = Interlocked.Increment(ref current);
                int seen;
                while (now > (seen = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, now, seen) != seen)
                {
                }
            },
            () => Interlocked.Decrement(ref current));
        var fetcher = new PageTitleFetcher(handler, _ => Public, false, TimeSpan.FromSeconds(5));

        await fetcher.FetchAsync(Enumerable.Range(0, 12).Select(i => $"https://old.example/{i}").ToList(), _ => { }, default);

        Assert.Equal(4, max);
    }
}
