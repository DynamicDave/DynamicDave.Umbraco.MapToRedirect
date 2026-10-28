using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class JobRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserKey = Guid.NewGuid();

    private static readonly AsyncLocal<string?> Ambient = new();

    private readonly FakeKeyValueService _keyValues = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly ListLogger<JobRunner> _log = new();
    private readonly ServiceProvider _services;
    private readonly JobRunner _runner;

    public JobRunnerTests()
    {
        // As left by the package migration (SeedJobState): the row exists with an empty value.
        _keyValues.Values[Constants.JobStateKey] = string.Empty;
        var services = new ServiceCollection();
        services.AddScoped<ScopedMarker>();
        _services = services.BuildServiceProvider();
        _runner = Runner();
    }

    private JobRunner Runner() =>
        new(_keyValues, _services.GetRequiredService<IServiceScopeFactory>(), _time, _log)
        {
            FinalWriteRetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
        };

    private sealed class ScopedMarker : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid?, TempKind?, string?)>> Returning(
        Guid? key = null, TempKind? kind = null, string? summary = null) =>
        (_, _, _) => Task.FromResult((key, kind, summary));

    private async Task<JobState> RunToEnd(JobKind kind, Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid?, TempKind?, string?)>> work)
    {
        Assert.NotNull(_runner.TryStart(kind, UserKey, "Dave", work));
        await _runner.LastRun!;
        return _runner.Current()!;
    }

    [Fact]
    public async Task Second_job_is_refused_while_one_runs()
    {
        var release = new TaskCompletionSource();
        var first = _runner.TryStart(JobKind.Generate, UserKey, "Dave", async (_, _, _) =>
        {
            await release.Task;
            return (null, null, null);
        });

        var second = _runner.TryStart(JobKind.Undo, Guid.NewGuid(), "Eve", Returning());

        Assert.NotNull(first);
        Assert.Null(second);
        var current = _runner.Current()!;
        Assert.Equal(JobStatus.Running, current.Status);
        Assert.Equal(JobKind.Generate, current.Kind);
        Assert.Equal("Dave", current.UserName);
        Assert.Equal(UserKey, current.UserKey);
        Assert.Equal(first.Id, current.Id);

        release.SetResult();
        await _runner.LastRun!;
        Assert.Equal(JobStatus.Completed, _runner.Current()!.Status);
        Assert.NotNull(_runner.TryStart(JobKind.Undo, Guid.NewGuid(), "Eve", Returning()));
        await _runner.LastRun!;
    }

    [Fact]
    public async Task Stale_heartbeat_reads_as_aborted()
    {
        var release = new TaskCompletionSource();
        var first = _runner.TryStart(JobKind.Apply, UserKey, "Dave", async (_, _, _) =>
        {
            await release.Task;
            return (null, null, null);
        })!;
        var firstRun = _runner.LastRun!;

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(JobStatus.Running, _runner.Current()!.Status);

        _time.Advance(TimeSpan.FromSeconds(1));
        var current = _runner.Current()!;
        Assert.Equal(JobStatus.Aborted, current.Status);
        Assert.Equal(first.Id, current.Id);

        // An aborted job does not block a new one, and the stale job can no longer overwrite the new job's state.
        var second = _runner.TryStart(JobKind.Generate, UserKey, "Eve", (_, _, _) => new TaskCompletionSource<(Guid?, TempKind?, string?)>().Task);
        Assert.NotNull(second);
        release.SetResult();
        await firstRun;

        var after = _runner.Current()!;
        Assert.Equal(second.Id, after.Id);
        Assert.Equal(JobStatus.Running, after.Status);
    }

    [Fact]
    public void Two_starters_on_the_same_stored_value_cannot_both_win()
    {
        var other = Runner();
        var never = (Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid?, TempKind?, string?)>>)
            ((_, _, _) => new TaskCompletionSource<(Guid?, TempKind?, string?)>().Task);

        Assert.NotNull(_runner.TryStart(JobKind.Generate, UserKey, "Dave", never));
        _time.Advance(TimeSpan.FromMinutes(5));

        // Both see the stale job; the stored value changes under the second starter between its read and its write.
        _keyValues.BeforeTrySet = () =>
        {
            _keyValues.BeforeTrySet = null;
            Assert.NotNull(other.TryStart(JobKind.Undo, UserKey, "Eve", never));
        };

        Assert.Null(_runner.TryStart(JobKind.Apply, UserKey, "Dave", never));
        Assert.Equal(JobKind.Undo, _runner.Current()!.Kind);
    }

    [Fact]
    public void First_starters_on_a_fresh_install_cannot_both_win()
    {
        var other = Runner();
        var never = (Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid?, TempKind?, string?)>>)
            ((_, _, _) => new TaskCompletionSource<(Guid?, TempKind?, string?)>().Task);
        JobState? otherStarted = null;
        _keyValues.BeforeTrySet = () =>
        {
            _keyValues.BeforeTrySet = null;
            otherStarted = other.TryStart(JobKind.Undo, UserKey, "Eve", never);
        };

        var started = _runner.TryStart(JobKind.Generate, UserKey, "Dave", never);

        Assert.NotNull(otherStarted);
        Assert.Null(started);
        Assert.Equal(otherStarted.Id, _runner.Current()!.Id);
    }

    // The row is created by the package migration (SeedJobState); when it is gone anyway the job slot heals itself.
    [Fact]
    public async Task A_missing_state_row_is_recreated_with_a_warning_and_the_job_starts()
    {
        _keyValues.Values.Clear();

        var started = _runner.TryStart(JobKind.Generate, UserKey, "Dave", Returning());

        Assert.NotNull(started);
        await _runner.LastRun!;
        Assert.Equal((started.Id, JobStatus.Completed), (_runner.Current()!.Id, _runner.Current()!.Status));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(Constants.JobStateKey));
    }

    [Fact]
    public async Task Final_write_is_retried_after_a_transient_failure()
    {
        var key = Guid.NewGuid();

        var state = await RunToEnd(JobKind.Generate, (_, _, _) =>
        {
            _keyValues.FailingTrySets = 3;
            return Task.FromResult<(Guid?, TempKind?, string?)>((key, TempKind.GeneratedCsv, "{}"));
        });

        Assert.Equal(JobStatus.Completed, state.Status);
        Assert.Equal(key, state.ResultKey);
    }

    [Fact]
    public async Task Final_write_whose_acknowledgement_was_lost_counts_as_written()
    {
        var state = await RunToEnd(JobKind.Generate, (_, _, _) =>
        {
            _keyValues.ThrowAfterNextWrite = true;
            return Task.FromResult<(Guid?, TempKind?, string?)>((null, null, "{}"));
        });

        Assert.Equal(JobStatus.Completed, state.Status);
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Error || e.Message.Contains("taken over"));
    }

    [Fact]
    public async Task Final_write_that_keeps_failing_is_logged_with_the_result_key()
    {
        var key = Guid.NewGuid();

        var state = await RunToEnd(JobKind.Generate, (_, _, _) =>
        {
            _keyValues.FailingTrySets = 4;
            return Task.FromResult<(Guid?, TempKind?, string?)>((key, TempKind.GeneratedCsv, "{}"));
        });

        Assert.Equal(JobStatus.Running, state.Status);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(key.ToString()) && e.Message.Contains("Completed"));
    }

    [Fact]
    public async Task Completed_job_stores_result_and_summary()
    {
        var key = Guid.NewGuid();

        var state = await RunToEnd(JobKind.Generate, async (_, progress, _) =>
        {
            progress.Report(3, 10, JobPhases.Matching);
            await Task.Yield();
            _time.Advance(TimeSpan.FromSeconds(5));
            return (key, TempKind.GeneratedCsv, "{\"urls\":10}");
        });

        Assert.Equal(JobStatus.Completed, state.Status);
        Assert.Equal(key, state.ResultKey);
        Assert.Equal(TempKind.GeneratedCsv, state.ResultKind);
        Assert.Equal("{\"urls\":10}", state.Summary);
        Assert.Null(state.Error);
        Assert.Equal(Start.UtcDateTime, state.StartedUtc);
        Assert.Equal(Start.UtcDateTime.AddSeconds(5), state.CompletedUtc);
        Assert.Equal(JobPhases.Matching, state.Phase);
        Assert.Equal(3, state.Done);
        Assert.Equal(10, state.Total);
        Assert.Contains("\"Status\":\"Completed\"", _keyValues.Values[Constants.JobStateKey]);
    }

    [Fact]
    public async Task Failing_work_marks_failed_with_error()
    {
        var state = await RunToEnd(JobKind.Undo, (_, _, _) => throw new InvalidOperationException("Database gone"));

        Assert.Equal(JobStatus.Failed, state.Status);
        Assert.Equal(JobRunner.UnexpectedError, state.Error);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("failed"));
        Assert.Null(state.ResultKey);
        Assert.Equal(Start.UtcDateTime, state.CompletedUtc);
    }

    [Fact]
    public async Task Job_failure_keeps_its_error_code_and_summary()
    {
        var state = await RunToEnd(JobKind.Generate, (_, _, _) => throw new JobFailedException("Unreachable", "{\"urls\":0}"));

        Assert.Equal(JobStatus.Failed, state.Status);
        Assert.Equal("Unreachable", state.Error);
        Assert.Equal("{\"urls\":0}", state.Summary);
    }

    [Fact]
    public async Task Work_runs_in_a_new_di_scope_without_the_callers_ambient_context()
    {
        Ambient.Value = "request scope";
        ScopedMarker? marker = null;
        string? seen = "not run";

        await RunToEnd(JobKind.Apply, (services, _, _) =>
        {
            marker = services.GetRequiredService<ScopedMarker>();
            seen = Ambient.Value;
            return Task.FromResult<(Guid?, TempKind?, string?)>((null, null, null));
        });

        Assert.Null(seen);
        Assert.NotNull(marker);
        Assert.True(marker.Disposed);
    }

    [Fact]
    public async Task Progress_is_saved_while_running()
    {
        var reported = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _runner.TryStart(JobKind.Apply, UserKey, "Dave", async (_, progress, _) =>
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            progress.Report(4, 8, JobPhases.Applying);
            reported.SetResult();
            await release.Task;
            return (null, null, null);
        });

        await reported.Task;
        var running = _runner.Current()!;
        Assert.Equal(JobStatus.Running, running.Status);
        Assert.Equal((4, 8, JobPhases.Applying), (running.Done, running.Total, running.Phase));
        Assert.Equal(Start.UtcDateTime.AddSeconds(1), running.HeartbeatUtc);

        release.SetResult();
        await _runner.LastRun!;
    }

    [Fact]
    public void Report_saves_at_most_every_500_ms_and_always_on_phase_change()
    {
        var saves = new List<(int Done, int Total, string Phase)>();
        var progress = new JobProgress(_time, (done, total, phase) => saves.Add((done, total, phase)));

        progress.Report(1, 10, JobPhases.Matching);
        progress.Report(2, 10, JobPhases.Matching);
        _time.Advance(TimeSpan.FromMilliseconds(499));
        progress.Report(3, 10, JobPhases.Matching);
        progress.Report(0, 4, JobPhases.FetchingTitles);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        progress.Report(1, 4, JobPhases.FetchingTitles);

        Assert.Equal([(1, 10, JobPhases.Matching), (0, 4, JobPhases.FetchingTitles), (1, 4, JobPhases.FetchingTitles)], saves);
    }

    [Fact]
    public void Heartbeat_saves_the_last_reported_progress()
    {
        var saves = new List<(int Done, int Total, string Phase)>();
        var progress = new JobProgress(_time, (done, total, phase) => saves.Add((done, total, phase)));

        progress.Report(1, 10, JobPhases.FetchingTitles);
        progress.Report(2, 10, JobPhases.FetchingTitles);
        progress.Beat();

        Assert.Equal([(1, 10, JobPhases.FetchingTitles), (2, 10, JobPhases.FetchingTitles)], saves);
        Assert.True(JobRunner.HeartbeatInterval <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Cancel_request_stops_the_running_job_as_failed_cancelled()
    {
        var running = new TaskCompletionSource();
        var cancelRequested = new TaskCompletionSource();
        var started = _runner.TryStart(JobKind.Apply, UserKey, "Dave", async (_, progress, ct) =>
        {
            progress.Report(1, 10, JobPhases.Applying);
            running.SetResult();
            await cancelRequested.Task;

            // The next progress write sees the request and cancels the token.
            _time.Advance(TimeSpan.FromSeconds(1));
            progress.Report(2, 10, JobPhases.Applying);
            await Task.Delay(Timeout.Infinite, ct);
            return (null, null, null);
        })!;
        await running.Task;

        var requested = _runner.RequestCancel();
        cancelRequested.SetResult();
        await _runner.LastRun!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(requested);
        Assert.Equal((started.Id, JobStatus.Running, true), (requested.Id, requested.Status, requested.CancelRequested));
        var state = _runner.Current()!;
        Assert.Equal((started.Id, JobStatus.Failed, JobRunner.CancelledError), (state.Id, state.Status, state.Error));
        Assert.Equal((2, 10), (state.Done, state.Total));
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Error || e.Message.Contains("taken over"));
    }

    [Fact]
    public async Task Cancel_request_is_seen_by_the_final_write_of_a_job_that_ignores_its_token()
    {
        var running = new TaskCompletionSource();
        var cancelRequested = new TaskCompletionSource();
        _runner.TryStart(JobKind.Undo, UserKey, "Dave", async (_, _, _) =>
        {
            running.SetResult();
            await cancelRequested.Task;
            return (null, null, "{}");
        });
        await running.Task;

        Assert.NotNull(_runner.RequestCancel());
        cancelRequested.SetResult();
        await _runner.LastRun!;

        // The work finished anyway: its result stands, and the job is not taken over.
        var state = _runner.Current()!;
        Assert.Equal((JobStatus.Completed, "{}"), (state.Status, state.Summary));
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("taken over"));
    }

    [Fact]
    public async Task Cancel_request_without_a_running_job_finds_nothing()
    {
        Assert.Null(_runner.RequestCancel());

        await RunToEnd(JobKind.Generate, Returning());
        Assert.Null(_runner.RequestCancel());
        Assert.Equal(JobStatus.Completed, _runner.Current()!.Status);
    }

    [Fact]
    public void Cancel_request_for_a_stale_job_finds_nothing()
    {
        _runner.TryStart(JobKind.Generate, UserKey, "Dave", (_, _, _) => new TaskCompletionSource<(Guid?, TempKind?, string?)>().Task);
        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.Null(_runner.RequestCancel());
        Assert.Equal(JobStatus.Aborted, _runner.Current()!.Status);
    }
}
