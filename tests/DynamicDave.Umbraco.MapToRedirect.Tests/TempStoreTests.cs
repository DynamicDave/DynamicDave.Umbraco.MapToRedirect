using DynamicDave.Umbraco.MapToRedirect.Persistence;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class TempStoreTests
{
    private static readonly DateTime Created = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(60)]
    public void Not_expired_within_an_hour(int minutes) =>
        Assert.False(TempStore.IsExpired(Created, Created.AddMinutes(minutes)));

    [Theory]
    [InlineData(61)]
    [InlineData(60 * 24)]
    public void Expired_after_an_hour(int minutes) =>
        Assert.True(TempStore.IsExpired(Created, Created.AddMinutes(minutes)));
}
