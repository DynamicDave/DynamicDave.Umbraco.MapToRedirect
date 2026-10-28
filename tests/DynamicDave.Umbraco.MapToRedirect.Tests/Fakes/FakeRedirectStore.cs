using DynamicDave.Umbraco.MapToRedirect.Redirects;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// In-memory redirect list with the <see cref="IRedirectStore"/> contract: Find returns the redirect in effect (the
/// newest of the culture, else the newest culture-less one), RemoveExact removes only rows of the culture. Counts
/// lookups so tests can check how often the store is read.
/// </summary>
internal sealed class FakeRedirectStore : IRedirectStore
{
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public List<StoredRedirect> Entries { get; } = [];

    public int FindCalls { get; private set; }

    public int LoadAllCalls { get; private set; }

    public IReadOnlyList<ExistingRedirect> Find(string route, string culture)
    {
        FindCalls++;
        return new EffectiveRedirects(Entries).Find(route, culture) is { } redirect ? [redirect] : [];
    }

    public IReadOnlyList<StoredRedirect> LoadAll()
    {
        LoadAllCalls++;
        return Entries.ToList();
    }

    public void Add(string route, Guid pageKey, string culture)
    {
        _clock = _clock.AddMinutes(1);
        Entries.Add(new StoredRedirect(Guid.NewGuid(), route, culture, pageKey, _clock));
    }

    public void RemoveExact(string route, string culture) =>
        Entries.RemoveAll(e => string.Equals(e.Route, route, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase));
}
