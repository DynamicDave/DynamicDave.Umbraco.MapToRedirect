using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class JobRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserKey = Guid.NewGuid();

    private static readonly AsyncLocal<string?> Ambient = new();

    private readonly FakeKeyValueService _keyValues = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly ServiceProvider _services;
    private readonly JobRunner _runner;

    public JobRunnerTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedMarker>();
        _services = services.BuildServiceProvider();
        _runner = new JobRunner(_keyValues, _services.GetRequiredService<IServiceScopeFactory>(), _time, NullLogger<JobRunner>.Instance);
    }

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
        var other = new JobRunner(_keyValues, _services.GetRequiredService<IServiceScopeFactory>(), _time, NullLogger<JobRunner>.Instance);
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
        Assert.Equal("Database gone", state.Error);
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
}
