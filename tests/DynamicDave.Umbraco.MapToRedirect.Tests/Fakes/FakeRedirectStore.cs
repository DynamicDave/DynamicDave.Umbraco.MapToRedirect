using DynamicDave.Umbraco.MapToRedirect.Redirects;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>In-memory redirect list with the <see cref="IRedirectStore"/> contract: exact route and culture, newest first.</summary>
internal sealed class FakeRedirectStore : IRedirectStore
{
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public List<(Guid Key, string Route, Guid PageKey, string Culture, DateTime CreateDateUtc)> Entries { get; } = [];

    public IReadOnlyList<ExistingRedirect> Find(string route, string culture) =>
        Entries.Where(e => e.Route == route && string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreateDateUtc)
            .Select(e => new ExistingRedirect(e.Key, e.PageKey, e.CreateDateUtc))
            .ToList();

    public void Add(string route, Guid pageKey, string culture)
    {
        _clock = _clock.AddMinutes(1);
        Entries.Add((Guid.NewGuid(), route, pageKey, culture, _clock));
    }

    public void RemoveAll(string route, string culture) =>
        Entries.RemoveAll(e => e.Route == route && string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase));
}
