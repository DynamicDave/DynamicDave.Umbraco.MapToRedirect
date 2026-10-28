using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class ImportUndoerTests
{
    private const string Culture = "nl-NL";

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid About = Guid.NewGuid();
    private static readonly Guid Contact = Guid.NewGuid();
    private static readonly Guid Home = Guid.NewGuid();

    private readonly FakeRedirectStore _store = new();
    private readonly FakeImportHistory _history = new();
    private readonly List<int> _done = [];
    private readonly FakeScopeProvider _scopes;

    public ImportUndoerTests() => _scopes = new FakeScopeProvider(_store, _history);

    private Guid Import(params ImportRowRecord[] rows)
    {
        var key = Guid.NewGuid();
        var id = _history.Create(new ImportRecord(key, Now.UtcDateTime.AddDays(-1), Guid.NewGuid(), "Dave", "redirects.csv", Guid.NewGuid(), Culture,
            true, new ImportCounts(0, 0, 0, 0, 0, 0), ImportState.Applied, null, null));
        foreach (var row in rows) _history.AddRow(id, row);
        return key;
    }

    private static ImportRowRecord Added(int rowNumber, string route, Guid page) =>
        new(rowNumber, route, Culture, ApplyOutcome.Added, page, null, null, "/old", "/new", null);

    private static ImportRowRecord Replaced(int rowNumber, string route, Guid page, ExistingRedirect replaced) =>
        new(rowNumber, route, Culture, ApplyOutcome.Replaced, page, replaced.PageKey, replaced.CreateDateUtc, "/old", "/new", null, replaced.AllCultures);

    private UndoResult Undo(Guid key, string userName = "Eve") =>
        new ImportUndoer(_store, _history, _scopes, new FixedTimeProvider(Now)).Undo(key, userName, _done.Add);

    [Fact]
    public void Undo_removes_added_redirects()
    {
        _store.Add("1057/a", About, Culture);
        _store.Add("1057/b", Contact, Culture);
        var key = Import(Added(2, "1057/a", About), Added(3, "1057/b", Contact));

        var result = Undo(key);

        Assert.Empty(_store.Entries);
        Assert.Equal((2, 0, 0), (result.Removed, result.Restored, result.LeftAlone));
        Assert.Empty(result.LeftAloneRows);
        var import = _history.Get(key)!;
        Assert.Equal(ImportState.Undone, import.State);
        Assert.Equal("Eve", import.UndoneBy);
        Assert.Equal(Now.UtcDateTime, import.UndoneUtc);
        Assert.Equal([1, 2], _done);
    }

    [Fact]
    public void Undo_restores_replaced_redirects()
    {
        _store.Add("1057/oud", Contact, Culture);
        var replaced = _store.Effective("1057/oud", Culture)!;
        _store.RemoveExact("1057/oud", Culture);
        _store.Add("1057/oud", About, Culture);
        var key = Import(Replaced(2, "1057/oud", About, replaced));

        var result = Undo(key);

        var entry = Assert.Single(_store.Entries);
        Assert.Equal(Contact, entry.PageKey);
        Assert.Equal(Culture, entry.Culture);
        Assert.Equal(0, result.Removed);
        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.LeftAlone);
    }

    [Fact]
    public void Undo_of_an_all_languages_override_only_removes_our_redirect()
    {
        _store.Add("1057/oud", Contact, "");
        var replaced = _store.Effective("1057/oud", Culture)!;
        _store.Add("1057/oud", About, Culture);
        var key = Import(Replaced(2, "1057/oud", About, replaced));

        var result = Undo(key);

        var entry = Assert.Single(_store.Entries);
        Assert.Equal("", entry.Culture);
        Assert.Equal(Contact, entry.PageKey);
        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.LeftAlone);
    }

    [Fact]
    public void Undo_leaves_redirects_changed_since_the_import()
    {
        _store.Add("1057/a", About, Culture);
        _store.Add("1057/oud", Contact, Culture);
        var replaced = _store.Effective("1057/oud", Culture)!;
        _store.RemoveExact("1057/oud", Culture);
        _store.Add("1057/oud", About, Culture);
        var added = Added(2, "1057/a", About);
        var replacedRow = Replaced(3, "1057/oud", About, replaced);
        var key = Import(added, replacedRow);

        // Someone else points both routes elsewhere after the import.
        _store.Add("1057/a", Home, Culture);
        _store.Add("1057/oud", Home, Culture);
        var before = _store.Entries.ToList();

        var result = Undo(key);

        Assert.Equal(before, _store.Entries);
        Assert.Equal(0, result.Removed);
        Assert.Equal(0, result.Restored);
        Assert.Equal(2, result.LeftAlone);
        Assert.Equal([added, replacedRow], result.LeftAloneRows);
        Assert.Equal(ImportState.Undone, _history.Get(key)!.State);
    }

    [Fact]
    public void Undo_leaves_an_added_redirect_whose_own_row_was_deleted_since_the_import()
    {
        // Only an all-languages redirect to the same page is left: it is not ours.
        _store.Add("1057/a", About, "");
        var added = Added(2, "1057/a", About);
        var key = Import(added);
        var before = _store.Entries.ToList();

        var result = Undo(key);

        Assert.Equal(before, _store.Entries);
        Assert.Equal([added], result.LeftAloneRows);
    }

    [Fact]
    public void Undo_ignores_rows_that_changed_nothing()
    {
        _store.Add("1057/a", About, Culture);
        var before = _store.Entries.ToList();
        var key = Import(
            new ImportRowRecord(2, "1057/a", Culture, ApplyOutcome.AlreadyPresent, About, null, null, "/a", "/over-ons", null),
            new ImportRowRecord(3, "1057/a", Culture, ApplyOutcome.Skipped, About, null, null, "/a", "/over-ons", "Skipped"),
            new ImportRowRecord(4, "", Culture, ApplyOutcome.Failed, null, null, null, "x", "y", "Failed"),
            new ImportRowRecord(5, "1057/a", Culture, ApplyOutcome.Changed, About, null, null, "/a", "/over-ons", "Changed"));

        var result = Undo(key);

        Assert.Equal(before, _store.Entries);
        Assert.Equal((0, 0, 0), (result.Removed, result.Restored, result.LeftAlone));
        Assert.Empty(result.LeftAloneRows);
    }

    [Fact]
    public void Failure_between_removing_and_restoring_keeps_the_redirect_in_place()
    {
        _store.Add("1057/oud", Contact, Culture);
        var replaced = _store.Effective("1057/oud", Culture)!;
        _store.RemoveExact("1057/oud", Culture);
        _store.Add("1057/oud", About, Culture);
        var key = Import(Replaced(2, "1057/oud", About, replaced));
        var before = _store.Entries.ToList();
        _store.FailOnAdd = _store.Adds + 1;

        Assert.Throws<InvalidOperationException>(() => Undo(key));

        // RemoveExact ran, then Add failed: the scope rolled the removal back.
        Assert.Equal(before, _store.Entries);
        Assert.False(Assert.Single(_history.AllRows(key)).Undone);
        Assert.Equal(ImportState.Applied, _history.Get(key)!.State);
        var scope = Assert.Single(_scopes.Created);
        Assert.False(scope.Completed);

        _store.FailOnAdd = null;
        var retry = Undo(key);

        Assert.Equal(1, retry.Restored);
        Assert.Equal(0, retry.LeftAlone);
        Assert.Equal(Contact, Assert.Single(_store.Entries).PageKey);
    }

    [Fact]
    public void Retry_after_a_partial_undo_counts_rows_already_undone()
    {
        _store.Add("1057/a", About, Culture);
        _store.Add("1057/b", Contact, Culture);
        _store.Add("1057/oud", Contact, Culture);
        var replaced = _store.Effective("1057/oud", Culture)!;
        _store.RemoveExact("1057/oud", Culture);
        _store.Add("1057/oud", About, Culture);
        var key = Import(Added(2, "1057/a", About), Added(3, "1057/b", Contact), Replaced(4, "1057/oud", About, replaced));
        _store.FailOnAdd = _store.Adds + 1;

        Assert.Throws<InvalidOperationException>(() => Undo(key));

        Assert.Equal([true, true, false], _history.AllRows(key).Select(r => r.Undone));
        Assert.Equal(ImportState.Applied, _history.Get(key)!.State);
        Assert.Equal(0, _history.MarkUndoneCalls);

        _store.FailOnAdd = null;
        var retry = Undo(key);

        Assert.Equal((2, 1, 0), (retry.Removed, retry.Restored, retry.LeftAlone));
        Assert.Empty(retry.LeftAloneRows);
        Assert.Equal(Contact, Assert.Single(_store.Entries).PageKey);
        Assert.All(_history.AllRows(key), r => Assert.True(r.Undone));
        Assert.Equal(ImportState.Undone, _history.Get(key)!.State);
    }

    [Fact]
    public void Each_row_is_undone_in_one_completed_scope()
    {
        _store.Add("1057/a", About, Culture);
        _store.Add("1057/b", Home, Culture);
        var key = Import(Added(2, "1057/a", About), Added(3, "1057/b", Contact));

        Undo(key);

        Assert.Equal(2, _scopes.Created.Count);
        Assert.All(_scopes.Created, s => Assert.True(s.Completed && s.Disposed));
        Assert.Equal([true, false], _history.AllRows(key).Select(r => r.Undone));
    }

    [Fact]
    public void Undo_twice_is_refused()
    {
        _store.Add("1057/a", About, Culture);
        var key = Import(Added(2, "1057/a", About));
        Undo(key);
        _store.Add("1057/a", About, Culture);

        Assert.Throws<InvalidOperationException>(() => Undo(key));

        Assert.Single(_store.Entries);
        Assert.Equal(1, _history.MarkUndoneCalls);
    }

    [Fact]
    public void Undo_of_an_unknown_import_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => Undo(Guid.NewGuid()));

    [Fact]
    public void Long_user_names_are_cut_to_the_column_length()
    {
        var key = Import();

        Undo(key, new string('u', 300));

        Assert.Equal(new string('u', 255), _history.Get(key)!.UndoneBy);
    }
}
