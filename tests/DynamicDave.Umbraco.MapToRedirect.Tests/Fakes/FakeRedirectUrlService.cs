using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// The lookup semantics observed on Umbraco 17.7.1 (Task 8 TestSite experiment): Register stores the culture as given
/// (null, "" and "nl-NL" unchanged) and refreshes the date of an identical url/content/culture row;
/// GetMostRecentRedirectUrl(url, culture) returns the newest row whose culture equals the culture (case-insensitive),
/// otherwise the newest culture-less (null or "") row. The url is compared case-insensitively, as on SQL Server, and a
/// blank culture counts as culture-less.
/// </summary>
internal sealed class FakeRedirectUrlService : IRedirectUrlService
{
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public List<RedirectUrl> Rows { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public void Register(string url, Guid contentKey, string? culture = null)
    {
        _clock = _clock.AddMinutes(1);
        var existing = Rows.FirstOrDefault(r => r.Url == url && r.ContentKey == contentKey && r.Culture == culture);
        if (existing is not null)
        {
            existing.CreateDateUtc = _clock;
            return;
        }

        Rows.Add(new RedirectUrl { Key = Guid.NewGuid(), Url = url, ContentKey = contentKey, Culture = culture, CreateDateUtc = _clock });
    }

    public IRedirectUrl? GetMostRecentRedirectUrl(string url, string? culture)
    {
        var candidates = Rows.Where(r => string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.CreateDateUtc).ToList();
        return candidates.FirstOrDefault(r => !string.IsNullOrEmpty(culture) && string.Equals(r.Culture, culture, StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Culture));
    }

    public void Delete(Guid id)
    {
        Deleted.Add(id);
        Rows.RemoveAll(r => r.Key == id);
    }

    public void Delete(IRedirectUrl redirectUrl) => Delete(redirectUrl.Key);

    public void DeleteContentRedirectUrls(Guid contentKey) => throw new NotSupportedException();

    public void DeleteAll() => throw new NotSupportedException();

    public IRedirectUrl? GetMostRecentRedirectUrl(string url) => throw new NotSupportedException();

    public Task<IRedirectUrl?> GetMostRecentRedirectUrlAsync(string url, string? culture) => throw new NotSupportedException();

    public IEnumerable<IRedirectUrl> GetContentRedirectUrls(Guid contentKey) => throw new NotSupportedException();

    public List<(long PageIndex, int PageSize)> PageRequests { get; } = [];

    public IEnumerable<IRedirectUrl> GetAllRedirectUrls(long pageIndex, int pageSize, out long total)
    {
        PageRequests.Add((pageIndex, pageSize));
        total = Rows.Count;
        return Rows.OrderByDescending(r => r.CreateDateUtc).Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList();
    }

    public IEnumerable<IRedirectUrl> GetAllRedirectUrls(int rootContentId, int count, out long total) => throw new NotSupportedException();

    public IEnumerable<IRedirectUrl> GetAllRedirectUrls(int rootContentId, long pageIndex, int pageSize, out long total) => throw new NotSupportedException();

    public IEnumerable<IRedirectUrl> SearchRedirectUrls(string searchTerm, long pageIndex, int pageSize, out long total) => throw new NotSupportedException();

    public IEnumerable<IRedirectUrl> SearchRedirectUrls(string searchTerm, int count, out long total) => throw new NotSupportedException();
}
