using System.Text.Json.Serialization;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

[JsonConverter(typeof(JsonStringEnumConverter<TempKind>))]
public enum TempKind
{
    Plan,
    GeneratedCsv,
    Report,
}

public sealed record TempEntry(string Content, string? FileName);

/// <summary>
/// Test-run plans and job results for one hour, in the database so every server behind a load balancer sees them.
/// Expired entries are deleted on every access.
/// </summary>
public sealed class TempStore(IScopeProvider scopeProvider, TimeProvider timeProvider)
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromHours(1);

    public static bool IsExpired(DateTime createdUtc, DateTime nowUtc) => createdUtc < nowUtc - TimeToLive;

    /// <summary>Stores the content under the key and kind, replacing an earlier entry with the same key and kind.</summary>
    public void Put(Guid key, TempKind kind, string content, string? fileName)
    {
        var now = Now();
        var kindValue = (int)kind;
        using var scope = scopeProvider.CreateScope();
        DeleteExpired(scope, now);
        scope.Database.Execute(scope.SqlContext.Sql().Delete<TempDto>().Where<TempDto>(x => x.Key == key && x.Kind == kindValue));
        scope.Database.Insert(new TempDto
        {
            Key = key,
            Kind = kindValue,
            CreatedUtc = now,
            ExpiresUtc = now + TimeToLive,
            FileName = fileName,
            Content = content,
        });
        scope.Complete();
    }

    /// <summary>The entry, or null when it does not exist or has expired.</summary>
    public TempEntry? Get(Guid key, TempKind kind)
    {
        var now = Now();
        var kindValue = (int)kind;
        using var scope = scopeProvider.CreateScope();
        DeleteExpired(scope, now);
        var dto = scope.Database.FirstOrDefault<TempDto>(scope.SqlContext.Sql()
            .Select<TempDto>().From<TempDto>().Where<TempDto>(x => x.Key == key && x.Kind == kindValue));
        scope.Complete();
        return dto is null || IsExpired(dto.CreatedUtc, now) ? null : new TempEntry(dto.Content, dto.FileName);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    // The same rule as IsExpired: entries created before now - TimeToLive.
    private static void DeleteExpired(IScope scope, DateTime nowUtc)
    {
        var cutoff = nowUtc - TimeToLive;
        scope.Database.Execute(scope.SqlContext.Sql().Delete<TempDto>().Where<TempDto>(x => x.CreatedUtc < cutoff));
    }
}
