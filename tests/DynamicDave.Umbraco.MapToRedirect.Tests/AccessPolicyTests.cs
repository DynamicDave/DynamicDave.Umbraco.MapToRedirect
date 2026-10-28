using DynamicDave.Umbraco.MapToRedirect.Security;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Models.Membership.Permissions;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class AccessPolicyTests
{
    private static IUser User(params string[] groupAliases)
    {
        var user = new User(new GlobalSettings());
        var id = 1;
        foreach (var alias in groupAliases)
        {
            user.AddGroup(new ReadOnlyUserGroup(id++, Guid.NewGuid(), alias, null, null, null, alias, [], [], new HashSet<string>(), new HashSet<IGranularPermission>(), true));
        }

        return user;
    }

    private static AccessPolicy Policy(MapToRedirectOptions? options = null) => new(new TestOptionsMonitor<MapToRedirectOptions>(options ?? new MapToRedirectOptions()));

    [Fact]
    public void Admin_has_access_by_default() => Assert.True(Policy().HasAccess(User("editor", "admin")));

    [Fact]
    public void Editor_has_no_access_by_default() => Assert.False(Policy().HasAccess(User("editor", "writer")));

    [Fact]
    public void Configured_group_has_access_case_insensitive()
    {
        var policy = Policy(new MapToRedirectOptions { AllowedUserGroups = ["SEO"] });

        Assert.True(policy.HasAccess(User("seo")));
        Assert.False(policy.HasAccess(User("admin")));
    }

    [Fact]
    public void Null_user_has_no_access() => Assert.False(Policy().HasAccess(null));

    [Fact]
    public void User_without_groups_has_no_access() => Assert.False(Policy().HasAccess(User()));
}
