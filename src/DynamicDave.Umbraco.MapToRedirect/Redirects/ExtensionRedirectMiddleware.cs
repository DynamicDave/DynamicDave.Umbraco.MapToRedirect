using DynamicDave.Umbraco.MapToRedirect.Sites;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>
/// Serves Umbraco's redirect list for old URLs with a file extension ("/about/team.html"). Umbraco treats those as
/// static file requests and answers 404 without looking up redirects; this middleware runs after the rest of the
/// pipeline and only turns such a 404 (not yet started) into a 301 when Umbraco's redirect list has the route, exactly
/// as Umbraco's ContentFinderByRedirectUrl would for a path without an extension. Decisions are in
/// <see cref="ExtensionRedirectRules"/>.
/// </summary>
public sealed class ExtensionRedirectMiddleware(
    RequestDelegate next,
    IOptions<MapToRedirectOptions> options,
    IOptions<GlobalSettings> globalSettings,
    TimeProvider timeProvider,
    ILogger<ExtensionRedirectMiddleware> logger)
{
    private static readonly TimeSpan SitesCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<string> _reserved =
        ExtensionRedirectRules.ParseReserved(globalSettings.Value.ReservedPaths, globalSettings.Value.ReservedUrls);

    // Replaced as a whole (a reference write), so concurrent requests never see a torn value.
    private volatile CachedSites? _sites;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.ToUriComponent();
        if (!options.Value.RedirectFileExtensionUrls || !ExtensionRedirectRules.IsCandidate(request.Method, path, _reserved))
        {
            await next(context);
            return;
        }

        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status404NotFound || context.Response.HasStarted) return;

        string location;
        try
        {
            var target = FindTarget(context, path);
            if (target is null) return;

            // Built completely before the response changes, so a failure leaves the 404 untouched.
            location = ExtensionRedirectRules.AsciiSafe(ExtensionRedirectRules.WithQuery(target, request.QueryString.Value ?? string.Empty));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Redirect lookup for {Path} stopped: the client aborted the request.", path);
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not look up a redirect for {Path}; the 404 stands.", path);
            return;
        }

        // Only the status, Location and the 404's body headers change: headers set by other middleware (HSTS, CSP,
        // CORS, cookies) stay.
        var response = context.Response;
        response.StatusCode = StatusCodes.Status301MovedPermanently;
        response.Headers.Location = location;
        response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.ContentType);
        response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.ContentLength);
    }

    // The URL of the redirect's target page in the request's culture (relative on the request's domain, as Umbraco's
    // own redirects); null when there is no redirect or the page has no URL (unpublished, or not routable there).
    private string? FindTarget(HttpContext context, string path)
    {
        var services = context.RequestServices;
        var lookup = ExtensionRedirectRules.Lookup(Sites(services), context.Request.Host.Value ?? string.Empty, path);
        if (lookup is null) return null;

        var redirect = services.GetRequiredService<IRedirectStore>().Find(lookup.Route, lookup.Culture).FirstOrDefault();
        if (redirect is null) return null;

        var current = new Uri($"{context.Request.Scheme}://{context.Request.Host.Value}{context.Request.PathBase.ToUriComponent()}{path}");
        return services.GetRequiredService<IRedirectTargetUrls>().UrlOf(redirect.PageKey, lookup.Culture, current);
    }

    // Sites change rarely; a short cache keeps extension 404s (often bots) from reading domains on every request.
    private IReadOnlyList<SiteInfo> Sites(IServiceProvider services)
    {
        var now = timeProvider.GetUtcNow();
        if (_sites is { } cached && now - cached.LoadedAt < SitesCacheDuration) return cached.Sites;

        var sites = services.GetRequiredService<ISiteCatalog>().GetSites();
        _sites = new CachedSites(sites, now);
        return sites;
    }

    private sealed record CachedSites(IReadOnlyList<SiteInfo> Sites, DateTimeOffset LoadedAt);
}
