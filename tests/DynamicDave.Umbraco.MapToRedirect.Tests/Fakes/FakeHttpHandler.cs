using System.Net;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>Returns canned responses by absolute URL (404 when unknown) and records every requested URL.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public FakeHttpHandler Add(string url, Func<HttpResponseMessage> response)
    {
        _routes[url] = response;
        return this;
    }

    public FakeHttpHandler Text(string url, string body, string contentType = "application/xml") =>
        Add(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, contentType) });

    public FakeHttpHandler Bytes(string url, byte[] body, string contentType = "application/gzip") =>
        Add(url, () =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

    public FakeHttpHandler Redirect(string url, string location, HttpStatusCode status = HttpStatusCode.Found) =>
        Add(url, () =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (Requests) Requests.Add(url);
        return Task.FromResult(_routes.TryGetValue(url, out var factory)
            ? factory()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
