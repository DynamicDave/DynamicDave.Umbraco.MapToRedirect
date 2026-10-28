using System.Net;
using System.Net.Sockets;
using DynamicDave.Umbraco.MapToRedirect.Sitemaps;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace DynamicDave.Umbraco.MapToRedirect.Composers;

public class MapToRedirectComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<MapToRedirectOptions>().Configure(options => MapToRedirectOptions.Bind(builder.Config.GetSection(Constants.OptionsSection), options));

        builder.Services.AddScoped<SitemapReader>();
        builder.Services.AddScoped<ISiteCatalog, UmbracoSiteCatalog>();

        // Redirects are followed manually (SafeHttp) so every target can be checked. Private network targets are
        // blocked unless configured, or the site runs in the Development environment. Decompression is automatic for
        // Content-Encoding; compressed .xml.gz files are decompressed by the reader itself.
        builder.Services.AddHttpClient(Constants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var allow = sp.GetRequiredService<IOptions<MapToRedirectOptions>>().Value.AllowPrivateNetworkTargets
                            ?? sp.GetRequiredService<IHostEnvironment>().IsDevelopment();
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectCallback = allow ? null : ConnectToPublicAddressAsync,
                };
            });
    }

    // Resolves the host and connects only to addresses outside the blocked ranges. Checking the resolved address
    // (not the host name) also covers DNS names and DNS rebinding that point at internal addresses.
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(a => !PrivateNetwork.IsBlocked(a)).ToArray();
        if (allowed.Length == 0) throw new PrivateNetworkBlockedException(host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
