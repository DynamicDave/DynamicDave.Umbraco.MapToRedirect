using DynamicDave.Umbraco.MapToRedirect.Redirects;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// In-memory redirect list with the <see cref="IRedirectStore"/> contract: Find returns the redirect in effect (the
/// newest of the culture, else the newest culture-less one), RemoveExact removes only rows of the culture.
/// </summary>
internal sealed class FakeRedirectStore : IRedirectStore
{
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public List<(Guid Key, string Route, Guid PageKey, string Culture, DateTime CreateDateUtc)> Entries { get; } = [];

    public IReadOnlyList<ExistingRedirect> Find(string route, string culture)
    {
        var rows = Entries.Where(e => e.Route == route).OrderByDescending(e => e.CreateDateUtc).ToList();
        var exact = rows.Where(e => string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase)).ToList();
        var effective = exact.Count > 0 ? exact : rows.Where(e => e.Culture.Length == 0).ToList();
        return effective.Take(1).Select(e => new ExistingRedirect(e.Key, e.PageKey, e.CreateDateUtc, e.Culture.Length == 0)).ToList();
    }

    public void Add(string route, Guid pageKey, string culture)
    {
        _clock = _clock.AddMinutes(1);
        Entries.Add((Guid.NewGuid(), route, pageKey, culture, _clock));
    }

    public void RemoveExact(string route, string culture) =>
        Entries.RemoveAll(e => e.Route == route && string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase));
}
