using Microsoft.Extensions.Configuration;

namespace DynamicDave.Umbraco.MapToRedirect;

/// <summary>Settings from the <c>DynamicDave:MapToRedirect</c> configuration section.</summary>
public class MapToRedirectOptions
{
    /// <summary>Aliases of the user groups that may use the dashboard (case-insensitive).</summary>
    public string[] AllowedUserGroups { get; set; } = ["admin"];

    /// <summary>
    /// True: sitemap and page requests may go to private network addresses. Null (default): allowed only in the
    /// Development environment.
    /// </summary>
    public bool? AllowPrivateNetworkTargets { get; set; }

    /// <summary>
    /// True (default): redirects for old URLs with a file extension ("/about/team.html"), which Umbraco does not route
    /// itself, are served by the package's middleware.
    /// </summary>
    public bool RedirectFileExtensionUrls { get; set; } = true;

    /// <summary>
    /// Binds the section onto <paramref name="options"/>. The configuration binder appends configured array items to
    /// the default array, which would make "admin" impossible to remove, so a configured list replaces the default.
    /// </summary>
    public static void Bind(IConfiguration section, MapToRedirectOptions options)
    {
        section.Bind(options);

        var groups = section.GetSection(nameof(AllowedUserGroups)).GetChildren().Select(c => c.Value).OfType<string>().ToArray();
        if (groups.Length > 0)
        {
            options.AllowedUserGroups = groups;
        }
    }
}
