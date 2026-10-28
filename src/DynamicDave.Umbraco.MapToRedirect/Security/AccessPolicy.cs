using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models.Membership;

namespace DynamicDave.Umbraco.MapToRedirect.Security;

/// <summary>Who may use the package: members of a user group in <see cref="MapToRedirectOptions.AllowedUserGroups"/>.</summary>
public sealed class AccessPolicy(IOptionsMonitor<MapToRedirectOptions> options)
{
    /// <summary>True when one of the user's group aliases is configured (case-insensitive); false without a user.</summary>
    public bool HasAccess(IUser? user)
    {
        if (user is null) return false;

        var allowed = options.CurrentValue.AllowedUserGroups;
        return user.Groups.Any(group => allowed.Contains(group.Alias, StringComparer.OrdinalIgnoreCase));
    }
}
