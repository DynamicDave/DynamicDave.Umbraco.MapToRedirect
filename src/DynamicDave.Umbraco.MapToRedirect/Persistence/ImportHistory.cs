using System.Text.Json.Serialization;
using DynamicDave.Umbraco.MapToRedirect.Applying;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

[JsonConverter(typeof(JsonStringEnumConverter<ImportState>))]
public enum ImportState
{
    Applied,
    Partial,
    Undone,
}

/// <summary>Rows per outcome; <see cref="Changed"/> rows changed since the test run and were not executed.</summary>
public sealed record ImportCounts(int Added, int Replaced, int AlreadyPresent, int Skipped, int Failed, int Changed);

public sealed record ImportRecord(
    Guid Key,
    DateTime CreatedUtc,
    Guid UserKey,
    string UserName,
    string FileName,
    Guid RootKey,
    string Culture,
    bool Overwrite,
    ImportCounts Counts,
    ImportState State,
    DateTime? UndoneUtc,
    string? UndoneBy);

/// <summary>
/// A row of an import. For a Replaced row, <see cref="ReplacedAllCultures"/> is true when the replaced redirect was
/// culture-less (all languages): it was kept, and the added redirect of the culture overrides it. <see cref="Undone"/>
/// is set per row by an undo. Row numbers are unique within an import (one row per CSV row).
/// </summary>
public sealed record ImportRowRecord(
    int RowNumber,
    string OldRoute,
    string Culture,
    ApplyOutcome Outcome,
    Guid? NewPageKey,
    Guid? ReplacedPageKey,
    DateTime? ReplacedCreateDateUtc,
    string? OldUrl,
    string? NewUrl,
    string? Message,
    bool ReplacedAllCultures = false,
    bool Undone = false);

public sealed class ImportHistory(IScopeProvider scopeProvider) : IImportHistory
{
    public int Create(ImportRecord record)
    {
        var dto = new ImportDto
        {
            Key = record.Key,
            CreatedUtc = record.CreatedUtc,
            UserKey = record.UserKey,
            UserName = record.UserName,
            FileName = record.FileName,
            RootKey = record.RootKey,
            Culture = record.Culture,
            Overwrite = record.Overwrite,
            State = (int)record.State,
            UndoneUtc = record.UndoneUtc,
            UndoneBy = record.UndoneBy,
        };
        SetCounts(dto, record.Counts);

        using var scope = scopeProvider.CreateScope();
        scope.Database.Insert(dto);
        scope.Complete();
        return dto.Id;
    }

    // Joins an ambient scope when there is one, so a row is written together with the redirect change it describes.
    public void AddRow(int importId, ImportRowRecord row)
    {
        using var scope = scopeProvider.CreateScope();
        scope.Database.Insert(new ImportRowDto
        {
            ImportId = importId,
            RowNumber = row.RowNumber,
            OldRoute = row.OldRoute,
            Culture = row.Culture,
            Outcome = (int)row.Outcome,
            NewPageKey = row.NewPageKey,
            ReplacedPageKey = row.ReplacedPageKey,
            ReplacedCreateDateUtc = row.ReplacedCreateDateUtc,
            ReplacedAllCultures = row.ReplacedAllCultures,
            Undone = row.Undone,
            OldUrl = row.OldUrl,
            NewUrl = row.NewUrl,
            Message = row.Message,
        });
        scope.Complete();
    }

    public void Complete(int importId, ImportCounts counts, ImportState state)
    {
        var stateValue = (int)state;
        using var scope = scopeProvider.CreateScope();
        scope.Database.Execute(scope.SqlContext.Sql()
            .Update<ImportDto>(u => u
                .Set(x => x.Added, counts.Added)
                .Set(x => x.Replaced, counts.Replaced)
                .Set(x => x.AlreadyPresent, counts.AlreadyPresent)
                .Set(x => x.Skipped, counts.Skipped)
                .Set(x => x.Failed, counts.Failed)
                .Set(x => x.Changed, counts.Changed)
                .Set(x => x.State, stateValue))
            .Where<ImportDto>(x => x.Id == importId));
        scope.Complete();
    }

    public (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var total = scope.Database.ExecuteScalar<int>(scope.SqlContext.Sql().SelectCount().From<ImportDto>());
        var items = scope.Database.SkipTake<ImportDto>(skip, take, scope.SqlContext.Sql()
            .Select<ImportDto>().From<ImportDto>()
            .OrderByDescending<ImportDto>(x => x.CreatedUtc, x => x.Id));
        return (items.Select(ToRecord).ToList(), total);
    }

    public ImportRecord? Get(Guid key)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = FindImport(scope, key);
        return dto is null ? null : ToRecord(dto);
    }

    public (IReadOnlyList<ImportRowRecord> Items, int Total) Rows(Guid key, ApplyOutcome[]? filter, int skip, int take)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var import = FindImport(scope, key);
        if (import is null)
        {
            return ([], 0);
        }

        // An empty filter matches nothing; null means no filter.
        var outcomes = filter?.Select(o => (int)o).Distinct().ToArray();
        if (outcomes is { Length: 0 })
        {
            return ([], 0);
        }

        var importId = import.Id;

        Sql<ISqlContext> Filtered(Sql<ISqlContext> sql)
        {
            sql = sql.Where<ImportRowDto>(x => x.ImportId == importId);
            return outcomes is null ? sql : sql.WhereIn<ImportRowDto>(x => x.Outcome, outcomes);
        }

        var total = scope.Database.ExecuteScalar<int>(Filtered(scope.SqlContext.Sql().SelectCount().From<ImportRowDto>()));
        var items = scope.Database.SkipTake<ImportRowDto>(skip, take, Filtered(scope.SqlContext.Sql()
                .Select<ImportRowDto>().From<ImportRowDto>())
            .OrderBy<ImportRowDto>(x => x.RowNumber, x => x.Id));
        return (items.Select(ToRecord).ToList(), total);
    }

    public IReadOnlyList<ImportRowRecord> AllRows(Guid key)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var import = FindImport(scope, key);
        if (import is null)
        {
            return [];
        }

        var importId = import.Id;
        return scope.Database.Fetch<ImportRowDto>(scope.SqlContext.Sql()
                .Select<ImportRowDto>().From<ImportRowDto>()
                .Where<ImportRowDto>(x => x.ImportId == importId)
                .OrderBy<ImportRowDto>(x => x.RowNumber, x => x.Id))
            .Select(ToRecord)
            .ToList();
    }

    public void MarkRowUndone(Guid key, int rowNumber)
    {
        using var scope = scopeProvider.CreateScope();
        var import = FindImport(scope, key) ?? throw new InvalidOperationException($"Import {key} does not exist.");
        var importId = import.Id;
        scope.Database.Execute(scope.SqlContext.Sql()
            .Update<ImportRowDto>(u => u.Set(x => x.Undone, true))
            .Where<ImportRowDto>(x => x.ImportId == importId && x.RowNumber == rowNumber));
        scope.Complete();
    }

    public void MarkUndone(Guid key, string userName, DateTime utc)
    {
        var undone = (int)ImportState.Undone;
        DateTime? undoneUtc = utc;
        using var scope = scopeProvider.CreateScope();
        scope.Database.Execute(scope.SqlContext.Sql()
            .Update<ImportDto>(u => u
                .Set(x => x.State, undone)
                .Set(x => x.UndoneUtc, undoneUtc)
                .Set(x => x.UndoneBy, userName))
            .Where<ImportDto>(x => x.Key == key));
        scope.Complete();
    }

    private static ImportDto? FindImport(IScope scope, Guid key)
        => scope.Database.FirstOrDefault<ImportDto>(scope.SqlContext.Sql()
            .Select<ImportDto>().From<ImportDto>().Where<ImportDto>(x => x.Key == key));

    private static void SetCounts(ImportDto dto, ImportCounts counts)
    {
        dto.Added = counts.Added;
        dto.Replaced = counts.Replaced;
        dto.AlreadyPresent = counts.AlreadyPresent;
        dto.Skipped = counts.Skipped;
        dto.Failed = counts.Failed;
        dto.Changed = counts.Changed;
    }

    private static ImportRecord ToRecord(ImportDto dto) => new(
        dto.Key,
        DateTime.SpecifyKind(dto.CreatedUtc, DateTimeKind.Utc),
        dto.UserKey,
        dto.UserName,
        dto.FileName,
        dto.RootKey,
        dto.Culture,
        dto.Overwrite,
        new ImportCounts(dto.Added, dto.Replaced, dto.AlreadyPresent, dto.Skipped, dto.Failed, dto.Changed),
        (ImportState)dto.State,
        dto.UndoneUtc is { } undoneUtc ? DateTime.SpecifyKind(undoneUtc, DateTimeKind.Utc) : null,
        dto.UndoneBy);

    private static ImportRowRecord ToRecord(ImportRowDto dto) => new(
        dto.RowNumber,
        dto.OldRoute,
        dto.Culture,
        (ApplyOutcome)dto.Outcome,
        dto.NewPageKey,
        dto.ReplacedPageKey,
        dto.ReplacedCreateDateUtc is { } created ? DateTime.SpecifyKind(created, DateTimeKind.Utc) : null,
        dto.OldUrl,
        dto.NewUrl,
        dto.Message,
        dto.ReplacedAllCultures,
        dto.Undone);
}
