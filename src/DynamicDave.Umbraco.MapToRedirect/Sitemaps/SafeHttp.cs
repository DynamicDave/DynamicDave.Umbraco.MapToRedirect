using System.Net;

namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

/// <summary>GET requests that follow redirects manually and refuse internal targets at every hop.</summary>
internal static class SafeHttp
{
    /// <summary>
    /// Sends a GET (headers read only) and follows up to <paramref name="maxRedirects"/> redirects. Unless
    /// <paramref name="allowPrivate"/> is set, the initial URL and every redirect target are resolved first and the
    /// request is refused with <see cref="PrivateNetworkBlockedException"/> when ANY resolved address is blocked
    /// (stricter than the connect callback, which only needs one allowed address; it stays as defence against DNS rebinding).
    /// Only http and https are allowed. <paramref name="timeout"/> applies to each request until the headers arrive.
    /// Throws <see cref="HttpRequestException"/> when there are too many redirects or the scheme is not http(s).
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        Uri url,
        bool allowPrivate,
        Func<string, IPAddress[]> resolve,
        int maxRedirects,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var current = url;
        for (var redirects = 0; ; redirects++)
        {
            if (current.Scheme != Uri.UriSchemeHttp && current.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException($"Scheme '{current.Scheme}' is not allowed.");
            }

            if (!allowPrivate)
            {
                await EnsurePublicAsync(current, resolve, ct);
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Request to '{current.Host}' timed out.");
            }

            var location = response.Headers.Location;
            if (!IsRedirect(response.StatusCode) || location is null)
            {
                return response;
            }

            response.Dispose();
            if (redirects >= maxRedirects)
            {
                throw new HttpRequestException($"More than {maxRedirects} redirects.");
            }

            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task EnsurePublicAsync(Uri url, Func<string, IPAddress[]> resolve, CancellationToken ct)
    {
        var host = url.Host;
        var addresses = IPAddress.TryParse(url.HostNameType == UriHostNameType.IPv6 ? host.Trim('[', ']') : host, out var literal)
            ? [literal]
            : await Task.Run(() => resolve(host), ct);

        if (addresses.Length == 0 || addresses.Any(PrivateNetwork.IsBlocked))
        {
            throw new PrivateNetworkBlockedException(host);
        }
    }
}
