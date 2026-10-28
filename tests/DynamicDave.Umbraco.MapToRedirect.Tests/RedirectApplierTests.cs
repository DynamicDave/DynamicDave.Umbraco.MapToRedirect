using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class RedirectApplierTests
{
    private const string Culture = "nl-NL";

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly SitePage Home = new(Guid.NewGuid(), "/", "", "Home", null);
    private static readonly SitePage About = new(Guid.NewGuid(), "/over-ons", "over-ons", "Over ons", null);
    private static readonly SitePage Contact = new(Guid.NewGuid(), "/contact", "contact", "Contact", null);

    private static readonly SiteInfo Site = new(
        Guid.NewGuid(), 1057, "Home", ["www.klant.nl"], [Culture],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [Culture] = ["www.klant.nl"] });

    private static readonly Guid UserKey = Guid.NewGuid();

    private readonly FakeRedirectStore _store = new();
    private readonly FakeImportHistory _history = new();
    private readonly FakeSiteCatalog _catalog = new(Site, [Home, About, Contact]);
    private readonly FakeScopeProvider _scopes;
    private readonly List<int> _done = [];

    public RedirectApplierTests() => _scopes = new FakeScopeProvider(_store, _history);

    private RedirectPlan Plan(params CsvRow[] rows) =>
        new RedirectPlanner(_store).Plan(Guid.NewGuid(), "redirects.csv", rows, Site, Culture, _catalog.Pages);

    private ApplyResult Apply(RedirectPlan plan, bool overwrite = false, string userName = "Dave") =>
        new RedirectApplier(_store, _history, _catalog, _scopes, new FixedTimeProvider(Now), NullLogger<RedirectApplier>.Instance).Apply(plan, overwrite, UserKey, userName, _done.Add);

    [Fact]
    public void Adds_new_redirects_and_records_history()
    {
        var plan = Plan(new CsvRow(2, "/oud/wie", "/over-ons"), new CsvRow(3, "https://oud.klant.nl/contact-us.php", Contact.Key.ToString()));

        var result = Apply(plan);

        Assert.Equal(About.Key, _store.Effective("1057/oud/wie", Culture)?.PageKey);
        Assert.Equal(Contact.Key, _store.Effective("1057/contact-us.php", Culture)?.PageKey);
        Assert.All(_store.Entries, e => Assert.Equal(Culture, e.Culture));

        Assert.Equal(new ImportCounts(2, 0, 0, 0, 0, 0), result.Counts);
        Assert.Equal(
            [
                new ImportRowRecord(2, "1057/oud/wie", Culture, ApplyOutcome.Added, About.Key, null, null, "/oud/wie", "/over-ons", null),
                new ImportRowRecord(3, "1057/contact-us.php", Culture, ApplyOutcome.Added, Contact.Key, null, null, "https://oud.klant.nl/contact-us.php", Contact.Key.ToString(), null),
            ],
            result.Rows);

        var import = _history.Get(result.ImportKey);
        Assert.NotNull(import);
        Assert.Equal(
            new ImportRecord(result.ImportKey, Now.UtcDateTime, UserKey, "Dave", "redirects.csv", Site.RootKey, Culture, false,
                new ImportCounts(2, 0, 0, 0, 0, 0), ImportState.Applied, null, null),
            import);
        Assert.Equal(result.Rows, _history.AllRows(result.ImportKey));
        Assert.Equal([1, 2], _done);
    }

    [Fact]
    public void Replaces_conflicts_when_overwrite_is_on()
    {
        _store.Add("1057/oud", Contact.Key, Culture);
        _store.Add("1057/oud", Home.Key, Culture);
        var replaced = _store.Effective("1057/oud", Culture)!;
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));

        var result = Apply(plan, overwrite: true);

        var entry = Assert.Single(_store.Entries);
        Assert.Equal(About.Key, entry.PageKey);
        Assert.Equal(Culture, entry.Culture);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ApplyOutcome.Replaced, row.Outcome);
        Assert.Equal(About.Key, row.NewPageKey);
        Assert.Equal(Home.Key, row.ReplacedPageKey);
        Assert.Equal(replaced.CreateDateUtc, row.ReplacedCreateDateUtc);
        Assert.False(row.ReplacedAllCultures);
        Assert.Equal(new ImportCounts(0, 1, 0, 0, 0, 0), result.Counts);
        Assert.Equal(row, Assert.Single(_history.AllRows(result.ImportKey)));
    }

    [Fact]
    public void Replacing_an_all_languages_redirect_keeps_it_and_overrides_it_for_the_culture()
    {
        _store.Add("1057/oud", Contact.Key, "");
        var replaced = _store.Effective("1057/oud", Culture)!;
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));

        var result = Apply(plan, overwrite: true);

        Assert.Equal(2, _store.Entries.Count);
        Assert.Contains(_store.Entries, e => e.Culture == "" && e.PageKey == Contact.Key);
        Assert.Equal(About.Key, _store.Effective("1057/oud", Culture)?.PageKey);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ApplyOutcome.Replaced, row.Outcome);
        Assert.Equal(Contact.Key, row.ReplacedPageKey);
        Assert.Equal(replaced.CreateDateUtc, row.ReplacedCreateDateUtc);
        Assert.True(row.ReplacedAllCultures);
        Assert.NotNull(row.Message);
        Assert.Equal("OverridesAllLanguages", row.MessageCode);
    }

    [Fact]
    public void Skips_conflicts_when_overwrite_is_off()
    {
        _store.Add("1057/oud", Contact.Key, Culture);
        var before = _store.Entries.ToList();
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));

        var result = Apply(plan, overwrite: false);

        Assert.Equal(before, _store.Entries);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ApplyOutcome.Skipped, row.Outcome);
        Assert.Equal(About.Key, row.NewPageKey);
        Assert.Null(row.ReplacedPageKey);
        Assert.Equal("ConflictKept", row.MessageCode);
        Assert.Equal(new ImportCounts(0, 0, 0, 1, 0, 0), result.Counts);
    }

    [Fact]
    public void Already_present_rows_are_recorded_without_a_change()
    {
        _store.Add("1057/oud", About.Key, Culture);
        var before = _store.Entries.ToList();
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));

        var result = Apply(plan);

        Assert.Equal(before, _store.Entries);
        Assert.Equal(ApplyOutcome.AlreadyPresent, Assert.Single(result.Rows).Outcome);
        Assert.Equal(new ImportCounts(0, 0, 1, 0, 0, 0), result.Counts);
    }

    [Fact]
    public void Row_whose_target_was_unpublished_since_the_test_run_is_changed()
    {
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));
        _catalog.Pages = [Home, Contact];

        var result = Apply(plan);

        Assert.Empty(_store.Entries);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ApplyOutcome.Changed, row.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(row.Message));
        Assert.Equal("TargetUnpublished", row.MessageCode);
        Assert.Equal(new ImportCounts(0, 0, 0, 0, 0, 1), result.Counts);
    }

    [Fact]
    public void Row_whose_old_path_was_published_since_the_test_run_is_changed()
    {
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));
        _catalog.Pages = [Home, About, Contact, new SitePage(Guid.NewGuid(), "/oud", "oud", "Oud", null)];

        var result = Apply(plan);

        Assert.Empty(_store.Entries);
        Assert.Equal((ApplyOutcome.Changed, "PageAtOldUrl"), (result.Rows[0].Outcome, result.Rows[0].MessageCode));
    }

    [Fact]
    public void Row_whose_route_got_a_redirect_since_the_test_run_is_changed()
    {
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));
        _store.Add("1057/oud", Contact.Key, Culture);
        var before = _store.Entries.ToList();

        var result = Apply(plan, overwrite: true);

        Assert.Equal(before, _store.Entries);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ApplyOutcome.Changed, row.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(row.Message));
        Assert.Equal("RedirectChanged", row.MessageCode);
    }

    [Fact]
    public void Conflict_whose_redirect_changed_since_the_test_run_is_changed()
    {
        _store.Add("1057/oud", Contact.Key, Culture);
        var plan = Plan(new CsvRow(2, "/oud", "/over-ons"));
        _store.Add("1057/oud", Home.Key, Culture);
        var before = _store.Entries.ToList();

        var result = Apply(plan, overwrite: true);

        Assert.Equal(before, _store.Entries);
        Assert.Equal(ApplyOutcome.Changed, Assert.Single(result.Rows).Outcome);
    }

    [Fact]
    public void Skip_and_error_rows_do_not_touch_the_store()
    {
        var plan = Plan(
            new CsvRow(2, "/oud", ""),
            new CsvRow(3, "/oud2", "/bestaat-niet"),
            new CsvRow(4, "/contact", "/over-ons"));

        var result = Apply(plan);

        Assert.Empty(_store.Entries);
        Assert.Equal(0, _store.FindCalls);
        Assert.Equal([ApplyOutcome.Skipped, ApplyOutcome.Failed, ApplyOutcome.Skipped], result.Rows.Select(r => r.Outcome));
        Assert.All(result.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Message)));
        Assert.Equal(["EmptyNewUrl", "NotPublished", "PageExistsAtPath"], result.Rows.Select(r => r.MessageCode));
        Assert.Equal(new ImportCounts(0, 0, 0, 2, 1, 0), result.Counts);
        Assert.Equal([1, 2, 3], _done);
    }

    [Fact]
    public void Failure_midway_marks_import_partial()
    {
        var plan = Plan(
            new CsvRow(2, "/a", "/over-ons"),
            new CsvRow(3, "/b", "/over-ons"),
            new CsvRow(4, "/c", "/over-ons"),
            new CsvRow(5, "/d", "/over-ons"));
        _store.FailOnAdd = 3;

        Assert.Throws<InvalidOperationException>(() => Apply(plan));

        var import = Assert.Single(_history.List(0, 10).Items);
        Assert.Equal(ImportState.Partial, import.State);
        Assert.Equal(new ImportCounts(2, 0, 0, 0, 0, 0), import.Counts);
        Assert.Equal([2, 3], _history.AllRows(import.Key).Select(r => r.RowNumber));
        Assert.Equal(2, _store.Entries.Count);
        Assert.Equal([1, 2], _done);
    }

    [Fact]
    public void Import_is_created_partial_and_completed_as_applied()
    {
        var result = Apply(Plan(new CsvRow(2, "/a", "/over-ons")));

        Assert.Equal([ImportState.Partial], _history.CreatedStates);
        Assert.Equal(ImportState.Applied, _history.Get(result.ImportKey)!.State);
    }

    [Fact]
    public void Failure_to_record_partial_state_does_not_hide_the_original_error()
    {
        var plan = Plan(new CsvRow(2, "/a", "/over-ons"));
        _store.FailOnAdd = 1;
        _history.FailOnComplete = new TimeoutException("History unavailable.");

        var error = Assert.Throws<InvalidOperationException>(() => Apply(plan));

        Assert.Equal("Database unavailable.", error.Message);
    }

    [Fact]
    public void Pages_are_loaded_once_and_each_executable_row_is_checked_again()
    {
        var plan = Plan(new CsvRow(2, "/a", "/over-ons"), new CsvRow(3, "/b", "/over-ons"), new CsvRow(4, "/c", "/contact"));
        var loadsBefore = _catalog.GetPagesCalls;

        Apply(plan);

        Assert.Equal(1, _catalog.GetPagesCalls - loadsBefore);
        Assert.Equal(3, _store.FindCalls);
    }

    [Fact]
    public void Each_change_and_its_history_row_share_one_completed_scope()
    {
        _store.Add("1057/b", Contact.Key, Culture);
        var plan = Plan(new CsvRow(2, "/a", "/over-ons"), new CsvRow(3, "/b", "/over-ons"));
        _store.Ambient = () => _scopes.Ambient;
        _history.Ambient = () => _scopes.Ambient;

        Apply(plan, overwrite: true);

        // Row 2: Add; row 3: RemoveExact and Add.
        Assert.Equal(3, _store.ChangeScopes.Count);
        Assert.Equal(2, _history.RowScopes.Count);
        var first = Assert.IsType<FakeScope>(_history.RowScopes[0]);
        var second = Assert.IsType<FakeScope>(_history.RowScopes[1]);
        Assert.NotSame(first, second);
        Assert.Same(first, _store.ChangeScopes[0]);
        Assert.Same(second, _store.ChangeScopes[1]);
        Assert.Same(second, _store.ChangeScopes[2]);
        Assert.True(first.Completed && first.Disposed);
        Assert.True(second.Completed && second.Disposed);
    }

    [Fact]
    public void Failed_row_scope_is_not_completed()
    {
        var plan = Plan(new CsvRow(2, "/a", "/over-ons"));
        _store.FailOnAdd = 1;

        Assert.Throws<InvalidOperationException>(() => Apply(plan));

        var scope = Assert.Single(_scopes.Created);
        Assert.False(scope.Completed);
        Assert.True(scope.Disposed);
    }

    [Fact]
    public void Long_values_are_cut_to_the_history_column_lengths()
    {
        var plan = Plan(new CsvRow(2, "/a", "/over-ons")) with { FileName = new string('f', 300), Culture = Culture + new string('x', 30) };

        var result = Apply(plan, userName: new string('u', 300));

        var import = _history.Get(result.ImportKey)!;
        Assert.Equal(new string('u', 255), import.UserName);
        Assert.Equal(new string('f', 255), import.FileName);
        Assert.Equal(20, import.Culture.Length);
    }
}
