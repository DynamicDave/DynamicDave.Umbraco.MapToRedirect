using DynamicDave.Umbraco.MapToRedirect.Applying;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

/// <summary>Applied imports and their executed rows. Kept indefinitely.</summary>
public interface IImportHistory
{
    /// <summary>Stores a new import and returns its id, used by <see cref="AddRow"/> and <see cref="Complete"/>.</summary>
    int Create(ImportRecord record);

    void AddRow(int importId, ImportRowRecord row);

    void Complete(int importId, ImportCounts counts, ImportState state);

    /// <summary>Imports, newest first.</summary>
    (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take);

    ImportRecord? Get(Guid key);

    /// <summary>Rows of an import in row order, optionally only those with one of the given outcomes.</summary>
    (IReadOnlyList<ImportRowRecord> Items, int Total) Rows(Guid key, ApplyOutcome[]? filter, int skip, int take);

    IReadOnlyList<ImportRowRecord> AllRows(Guid key);

    /// <summary>
    /// Marks the row with the row number of the import as undone. Joins an ambient scope, so it is written together
    /// with the redirect changes that undo the row.
    /// </summary>
    void MarkRowUndone(Guid key, int rowNumber);

    void MarkUndone(Guid key, string userName, DateTime utc);
}
