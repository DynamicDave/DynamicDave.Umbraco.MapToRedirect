using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Redirects;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

/// <summary>
/// The outcome of an undo: redirects removed (Added rows), previous redirects back in effect (Replaced rows), and the
/// rows left alone because their redirect changed since the import.
/// </summary>
public sealed record UndoResult(int Removed, int Restored, int LeftAlone, IReadOnlyList<ImportRowRecord> LeftAloneRows);

/// <summary>
/// Undoes an import once (spec 4.6). A row is only undone while the redirect in effect for its route and culture is
/// still the one the import wrote (of exactly that culture, to the same page); anything else is left alone and listed.
/// </summary>
public sealed class ImportUndoer(IRedirectStore store, IImportHistory history, TimeProvider timeProvider)
{
    /// <param name="onDone">Called after each row with the number of rows done.</param>
    /// <exception cref="InvalidOperationException">The import does not exist or was already undone.</exception>
    public UndoResult Undo(Guid importKey, string userName, Action<int> onDone)
    {
        var import = history.Get(importKey) ?? throw new InvalidOperationException($"Import {importKey} does not exist.");
        if (import.State == ImportState.Undone) throw new InvalidOperationException($"Import {importKey} was already undone.");

        var removed = 0;
        var restored = 0;
        var leftAlone = new List<ImportRowRecord>();
        var done = 0;
        foreach (var row in history.AllRows(importKey))
        {
            switch (row.Outcome)
            {
                case ApplyOutcome.Added when IsStillOurs(row):
                    store.RemoveExact(row.OldRoute, row.Culture);
                    removed++;
                    break;

                // The culture-less redirect was never deleted: removing ours puts it back in effect.
                case ApplyOutcome.Replaced when row.ReplacedAllCultures && IsStillOurs(row):
                    store.RemoveExact(row.OldRoute, row.Culture);
                    restored++;
                    break;

                case ApplyOutcome.Replaced when !row.ReplacedAllCultures && row.ReplacedPageKey is { } previous && IsStillOurs(row):
                    store.RemoveExact(row.OldRoute, row.Culture);
                    store.Add(row.OldRoute, previous, row.Culture);
                    restored++;
                    break;

                case ApplyOutcome.Added or ApplyOutcome.Replaced:
                    leftAlone.Add(row);
                    break;
            }

            onDone(++done);
        }

        history.MarkUndone(importKey, RedirectApplier.Cut(userName, RedirectApplier.MaxNameLength), timeProvider.GetUtcNow().UtcDateTime);
        return new UndoResult(removed, restored, leftAlone.Count, leftAlone);
    }

    private bool IsStillOurs(ImportRowRecord row) =>
        row.NewPageKey is { } page
        && store.Find(row.OldRoute, row.Culture).FirstOrDefault() is { AllCultures: false } current
        && current.PageKey == page;
}
