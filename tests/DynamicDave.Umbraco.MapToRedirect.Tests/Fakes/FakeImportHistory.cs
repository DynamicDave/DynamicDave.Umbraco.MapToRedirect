using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Persistence;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// In-memory import history. <see cref="Ambient"/>, when set, is read on every AddRow and stored in
/// <see cref="RowScopes"/>, so tests can check which scope a row was written in.
/// </summary>
internal sealed class FakeImportHistory : IImportHistory
{
    private readonly List<(int Id, ImportRecord Record, List<ImportRowRecord> Rows)> _imports = [];

    public Func<object?>? Ambient { get; set; }

    public List<object?> RowScopes { get; } = [];

    public int MarkUndoneCalls { get; private set; }

    public int Create(ImportRecord record)
    {
        var id = _imports.Count + 1;
        _imports.Add((id, record, []));
        return id;
    }

    public void AddRow(int importId, ImportRowRecord row)
    {
        if (Ambient is not null) RowScopes.Add(Ambient());
        ById(importId).Rows.Add(row);
    }

    public void Complete(int importId, ImportCounts counts, ImportState state)
    {
        var index = _imports.FindIndex(i => i.Id == importId);
        var import = _imports[index];
        _imports[index] = import with { Record = import.Record with { Counts = counts, State = state } };
    }

    public (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take) =>
        (_imports.Select(i => i.Record).Reverse().Skip(skip).Take(take).ToList(), _imports.Count);

    public ImportRecord? Get(Guid key) => ByKey(key)?.Record;

    public (IReadOnlyList<ImportRowRecord> Items, int Total) Rows(Guid key, ApplyOutcome[]? filter, int skip, int take)
    {
        var rows = AllRows(key).Where(r => filter is null || filter.Contains(r.Outcome)).ToList();
        return (rows.Skip(skip).Take(take).ToList(), rows.Count);
    }

    public IReadOnlyList<ImportRowRecord> AllRows(Guid key) =>
        ByKey(key)?.Rows.OrderBy(r => r.RowNumber).ToList() ?? [];

    public void MarkUndone(Guid key, string userName, DateTime utc)
    {
        MarkUndoneCalls++;
        var index = _imports.FindIndex(i => i.Record.Key == key);
        var import = _imports[index];
        _imports[index] = import with { Record = import.Record with { State = ImportState.Undone, UndoneUtc = utc, UndoneBy = userName } };
    }

    private (int Id, ImportRecord Record, List<ImportRowRecord> Rows) ById(int id) => _imports.Single(i => i.Id == id);

    private (int Id, ImportRecord Record, List<ImportRowRecord> Rows)? ByKey(Guid key) =>
        _imports.Where(i => i.Record.Key == key).Select(i => ((int, ImportRecord, List<ImportRowRecord>)?)i).FirstOrDefault();
}
