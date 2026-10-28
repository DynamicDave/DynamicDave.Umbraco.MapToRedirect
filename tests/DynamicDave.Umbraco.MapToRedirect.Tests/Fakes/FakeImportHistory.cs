using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Persistence;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// In-memory import history that takes part in <see cref="FakeScopeProvider"/> scopes. <see cref="Ambient"/>, when
/// set, is read on every AddRow and stored in <see cref="RowScopes"/>. <see cref="CreatedStates"/> keeps the state
/// each import was created with; <see cref="FailOnComplete"/> makes Complete throw.
/// </summary>
internal sealed class FakeImportHistory : IImportHistory, IFakeTransactional
{
    private List<Import> _imports = [];

    public Func<object?>? Ambient { get; set; }

    public List<object?> RowScopes { get; } = [];

    public List<ImportState> CreatedStates { get; } = [];

    public int MarkUndoneCalls { get; private set; }

    public Exception? FailOnComplete { get; set; }

    public int Create(ImportRecord record)
    {
        var id = _imports.Count + 1;
        CreatedStates.Add(record.State);
        _imports.Add(new Import(id, record, []));
        return id;
    }

    public void AddRow(int importId, ImportRowRecord row)
    {
        if (Ambient is not null) RowScopes.Add(Ambient());
        ById(importId).Rows.Add(row);
    }

    public void Complete(int importId, ImportCounts counts, ImportState state)
    {
        if (FailOnComplete is not null) throw FailOnComplete;
        var import = ById(importId);
        import.Record = import.Record with { Counts = counts, State = state };
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

    public void MarkRowUndone(Guid key, int rowNumber)
    {
        var rows = ByKey(key)!.Rows;
        var index = rows.FindIndex(r => r.RowNumber == rowNumber);
        rows[index] = rows[index] with { Undone = true };
    }

    public void MarkUndone(Guid key, string userName, DateTime utc)
    {
        MarkUndoneCalls++;
        var import = ByKey(key)!;
        import.Record = import.Record with { State = ImportState.Undone, UndoneUtc = utc, UndoneBy = userName };
    }

    public object Snapshot() => _imports.Select(i => new Import(i.Id, i.Record, i.Rows.ToList())).ToList();

    public void Restore(object snapshot) => _imports = (List<Import>)snapshot;

    private Import ById(int id) => _imports.Single(i => i.Id == id);

    private Import? ByKey(Guid key) => _imports.FirstOrDefault(i => i.Record.Key == key);

    private sealed class Import(int id, ImportRecord record, List<ImportRowRecord> rows)
    {
        public int Id { get; } = id;

        public ImportRecord Record { get; set; } = record;

        public List<ImportRowRecord> Rows { get; } = rows;
    }
}
