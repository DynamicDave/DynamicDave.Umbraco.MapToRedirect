using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>The URL a redirect sends visitors to.</summary>
public interface IRedirectTargetUrls
{
    /// <summary>
    /// The URL of the published page in the culture, as seen from <paramref name="current"/> (the request URL); null
    /// when the page has no URL there (unpublished, or not routable in that culture).
    /// </summary>
    string? UrlOf(Guid pageKey, string culture, Uri current);
}

/// <summary><see cref="IRedirectTargetUrls"/> over Umbraco's <see cref="IPublishedUrlProvider"/>.</summary>
public sealed class UmbracoRedirectTargetUrls(IUmbracoContextFactory umbracoContextFactory, IPublishedUrlProvider urlProvider) : IRedirectTargetUrls
{
    public string? UrlOf(Guid pageKey, string culture, Uri current)
    {
        // Umbraco creates no context for requests with a file extension (it treats them as static files).
        using var reference = umbracoContextFactory.EnsureUmbracoContext();
        // Default mode, as Umbraco's ContentFinderByRedirectUrl: relative when the page is on the request's domain,
        // absolute only for another domain, whose host comes from Umbraco's domain list (never from the Host header).
        var url = urlProvider.GetUrl(pageKey, UrlMode.Default, culture, current);
        return string.IsNullOrEmpty(url) || url == "#" ? null : url;
    }
}
