using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Redirects;
using Umbraco.Cms.Infrastructure.Scoping;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

/// <summary>
/// The outcome of an undo: redirects removed (Added rows), previous redirects back in effect (Replaced rows), and the
/// rows left alone because their redirect changed since the import.
/// </summary>
public sealed record UndoResult(int Removed, int Restored, int LeftAlone, IReadOnlyList<ImportRowRecord> LeftAloneRows);

/// <summary>
/// Undoes an import once (spec 4.6). A row is only undone while the redirect in effect for its route and culture is
/// still the one the import wrote (of exactly that culture, to the same page); anything else is left alone and listed.
/// Each row is undone in one scope together with its undone flag, so an undo that stops midway can be run again: rows
/// already undone are counted, not touched. The import is marked Undone only after every row.
/// </summary>
public sealed class ImportUndoer(IRedirectStore store, IImportHistory history, IScopeProvider scopeProvider, TimeProvider timeProvider)
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
            // An earlier undo that stopped midway already undid this row.
            if (row.Undone)
            {
                if (row.Outcome == ApplyOutcome.Added) removed++;
                else restored++;
            }
            else
            {
                // The redirect changes and the row's undone flag are written together, or not at all.
                using var scope = scopeProvider.CreateScope();
                switch (UndoRow(importKey, row))
                {
                    case RowResult.Removed:
                        removed++;
                        break;
                    case RowResult.Restored:
                        restored++;
                        break;
                    case RowResult.LeftAlone:
                        leftAlone.Add(row);
                        break;
                }

                scope.Complete();
            }

            onDone(++done);
        }

        history.MarkUndone(importKey, RedirectApplier.Cut(userName, RedirectApplier.MaxNameLength), timeProvider.GetUtcNow().UtcDateTime);
        return new UndoResult(removed, restored, leftAlone.Count, leftAlone);
    }

    private RowResult UndoRow(Guid importKey, ImportRowRecord row)
    {
        if (row.Outcome is not (ApplyOutcome.Added or ApplyOutcome.Replaced)) return RowResult.Ignored;
        if (!IsStillOurs(row)) return RowResult.LeftAlone;

        RowResult result;
        if (row.Outcome == ApplyOutcome.Added)
        {
            store.RemoveExact(row.OldRoute, row.Culture);
            result = RowResult.Removed;
        }
        else if (row.ReplacedAllCultures)
        {
            // The culture-less redirect was never deleted: removing ours puts it back in effect.
            store.RemoveExact(row.OldRoute, row.Culture);
            result = RowResult.Restored;
        }
        else if (row.ReplacedPageKey is { } previous)
        {
            store.RemoveExact(row.OldRoute, row.Culture);
            store.Add(row.OldRoute, previous, row.Culture);
            result = RowResult.Restored;
        }
        else
        {
            return RowResult.LeftAlone;
        }

        history.MarkRowUndone(importKey, row.RowNumber);
        return result;
    }

    private bool IsStillOurs(ImportRowRecord row) =>
        row.NewPageKey is { } page
        && store.Find(row.OldRoute, row.Culture).FirstOrDefault() is { AllCultures: false } current
        && current.PageKey == page;

    private enum RowResult
    {
        Ignored,
        Removed,
        Restored,
        LeftAlone,
    }
}
