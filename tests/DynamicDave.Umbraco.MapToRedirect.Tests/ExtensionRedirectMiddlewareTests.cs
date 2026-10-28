using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class ExtensionRedirectMiddlewareTests
{
    private const string Host = "klant-nl.localhost:44416";
    private const string OldPath = "/about/ons-team.html";
    private static readonly Guid PageKey = Guid.NewGuid();

    private static readonly SiteInfo KlantNl = new(
        Guid.NewGuid(), 1057, "Klant NL", ["klant-nl.localhost"], ["nl-NL"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["nl-NL"] = ["klant-nl.localhost"] });

    private readonly FakeRedirectStore _store = new();
    private readonly FakeTargetUrls _targets = new() { Url = "/over-ons/ons-team/" };
    private readonly ListLogger<ExtensionRedirectMiddleware> _logger = new();
    private int _nextCalls;

    public ExtensionRedirectMiddlewareTests() => _store.Add("1057/about/ons-team.html", PageKey, "nl-NL");

    private ExtensionRedirectMiddleware Middleware(RequestDelegate? next = null, bool enabled = true) => new(
        next ?? NotFound,
        Options.Create(new MapToRedirectOptions { RedirectFileExtensionUrls = enabled }),
        Options.Create(new GlobalSettings()),
        TimeProvider.System,
        _logger);

    private Task NotFound(HttpContext context)
    {
        _nextCalls++;
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength = 0;
        return Task.CompletedTask;
    }

    private DefaultHttpContext Context(string path = OldPath, string method = "GET", string query = "")
    {
        var services = new ServiceCollection()
            .AddSingleton<ISiteCatalog>(new FakeSiteCatalog(KlantNl, []))
            .AddSingleton<IRedirectStore>(_store)
            .AddSingleton<IRedirectTargetUrls>(_targets)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(Host);
        context.Request.Path = new PathString(path);
        context.Request.QueryString = new QueryString(query);
        context.Response.Headers.StrictTransportSecurity = "max-age=31536000";
        context.Response.Headers.SetCookie = "a=b; Secure";
        context.Response.Headers.XFrameOptions = "DENY";
        return context;
    }

    [Fact]
    public async Task Redirect_found_answers_301_and_keeps_other_headers()
    {
        var context = Context(query: "?a=1&b=2");

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal("/over-ons/ons-team/?a=1&b=2", context.Response.Headers.Location.ToString());
        Assert.Equal("max-age=31536000", context.Response.Headers.StrictTransportSecurity.ToString());
        Assert.Equal("a=b; Secure", context.Response.Headers.SetCookie.ToString());
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions.ToString());
        Assert.False(context.Response.Headers.ContainsKey("Content-Type"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Length"));
        Assert.Equal(1, _nextCalls);
    }

    [Fact]
    public async Task Target_url_gets_the_request_url_as_current()
    {
        await Middleware().InvokeAsync(Context());

        Assert.Equal(PageKey, _targets.LastPageKey);
        Assert.Equal("nl-NL", _targets.LastCulture);
        Assert.Equal("https://klant-nl.localhost:44416/about/ons-team.html", _targets.LastCurrent?.AbsoluteUri);
    }

    [Fact]
    public async Task Non_ascii_target_and_query_give_an_ascii_location()
    {
        _targets.Url = "https://café.example/über/";
        var context = Context(query: "?q=ü");

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        var location = context.Response.Headers.Location.ToString();
        Assert.Equal("https://xn--caf-dma.example/%C3%BCber/?q=%C3%BC", location);
        Assert.All(location, c => Assert.InRange(c, '!', '~'));
    }

    [Fact]
    public async Task Started_response_is_left_alone()
    {
        var context = Context();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Option_off_leaves_the_404()
    {
        var context = Context();

        await Middleware(enabled: false).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(0, _store.FindCalls);
        Assert.Equal(1, _nextCalls);
    }

    [Fact]
    public async Task Other_status_is_left_alone()
    {
        var context = Context();

        await Middleware(c => { _nextCalls++; c.Response.StatusCode = StatusCodes.Status200OK; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, _store.FindCalls);
    }

    [Theory]
    [InlineData("/about/ons-team", "GET")]
    [InlineData(OldPath, "POST")]
    [InlineData("/media/a.html", "GET")]
    public async Task Non_candidates_only_run_next(string path, string method)
    {
        var context = Context(path, method);

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(1, _nextCalls);
        Assert.Equal(0, _store.FindCalls);
    }

    [Fact]
    public async Task No_redirect_leaves_the_404()
    {
        var context = Context("/unknown.html");

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("text/plain", context.Response.ContentType);
    }

    [Fact]
    public async Task Page_without_url_leaves_the_404()
    {
        _targets.Url = null;
        var context = Context();

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Failure_is_logged_as_warning_and_the_404_stands()
    {
        _targets.Throw = new InvalidOperationException("boom");
        var context = Context();

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Client_abort_is_not_logged_as_warning()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        _targets.Throw = new OperationCanceledException(aborted.Token);
        var context = Context();
        context.RequestAborted = aborted.Token;

        await Middleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    private sealed class FakeTargetUrls : IRedirectTargetUrls
    {
        public string? Url { get; set; }

        public Exception? Throw { get; set; }

        public Guid? LastPageKey { get; private set; }

        public string? LastCulture { get; private set; }

        public Uri? LastCurrent { get; private set; }

        public string? UrlOf(Guid pageKey, string culture, Uri current)
        {
            if (Throw is not null) throw Throw;
            (LastPageKey, LastCulture, LastCurrent) = (pageKey, culture, current);
            return Url;
        }
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public StartedResponseFeature() => StatusCode = StatusCodes.Status404NotFound;

        public override bool HasStarted => true;
    }
}
