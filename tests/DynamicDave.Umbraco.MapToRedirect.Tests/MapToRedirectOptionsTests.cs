using Microsoft.Extensions.Configuration;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class MapToRedirectOptionsTests
{
    private static MapToRedirectOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new MapToRedirectOptions();
        MapToRedirectOptions.Bind(configuration.GetSection(Constants.OptionsSection), options);
        return options;
    }

    [Fact]
    public void Options_bind_with_defaults()
    {
        var options = Bind([]);

        Assert.Equal(["admin"], options.AllowedUserGroups);
        Assert.Null(options.AllowPrivateNetworkTargets);
        Assert.True(options.RedirectFileExtensionUrls);
    }

    [Fact]
    public void File_extension_redirects_can_be_switched_off()
    {
        var options = Bind(new() { ["DynamicDave:MapToRedirect:RedirectFileExtensionUrls"] = "false" });

        Assert.False(options.RedirectFileExtensionUrls);
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var options = Bind(new()
        {
            ["DynamicDave:MapToRedirect:AllowedUserGroups:0"] = "admin",
            ["DynamicDave:MapToRedirect:AllowedUserGroups:1"] = "editor",
        });

        Assert.Equal(["admin", "editor"], options.AllowedUserGroups);
    }

    [Fact]
    public void Configured_groups_replace_the_default()
    {
        var options = Bind(new() { ["DynamicDave:MapToRedirect:AllowedUserGroups:0"] = "editor" });

        Assert.Equal(["editor"], options.AllowedUserGroups);
    }
}
