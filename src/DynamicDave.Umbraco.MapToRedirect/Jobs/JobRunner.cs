using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.MapToRedirect.Jobs;

/// <summary>
/// Runs the one background job of the installation (generate, apply or undo). The state lives in umbracoKeyValue, so
/// every server behind a load balancer sees it; starting a job claims it with compare-and-set on the stored value.
/// </summary>
public sealed class JobRunner(IKeyValueService keyValues, IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<JobRunner> logger)
{
    /// <summary>A running job whose heartbeat is older than this counts as aborted (e.g. after a restart).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>A running job refreshes its heartbeat at least this often, even without progress.</summary>
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly IKeyValueService _keyValues = keyValues;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<JobRunner> _logger = logger;
    private readonly Lock _startGate = new();

    /// <summary>The background task of the last job this instance started (for tests).</summary>
    internal Task? LastRun { get; private set; }

    /// <summary>The state of the current or last job; a running job with a stale heartbeat is returned as <see cref="JobStatus.Aborted"/>.</summary>
    public JobState? Current()
    {
        var state = Deserialize(_keyValues.GetValue(Constants.JobStateKey));
        return state is not null && IsStale(state, Now()) ? state with { Status = JobStatus.Aborted } : state;
    }

    /// <summary>
    /// Claims the job slot and runs <paramref name="work"/> in the background, in a new DI scope and without the
    /// caller's ambient context (no ambient Umbraco scope). Returns the new state, or null when another job is running.
    /// </summary>
    public JobState? TryStart(
        JobKind kind,
        Guid userKey,
        string userName,
        Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid? ResultKey, TempKind? ResultKind, string? Summary)>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // The lock covers double starts within this process (also on a fresh install, where there is no row to
        // compare against yet); compare-and-set on the raw stored value covers other servers.
        lock (_startGate)
        {
            var raw = _keyValues.GetValue(Constants.JobStateKey);
            var now = Now();
            var current = Deserialize(raw);
            if (current is { Status: JobStatus.Running } && !IsStale(current, now))
            {
                return null;
            }

            var state = new JobState(Guid.NewGuid(), kind, JobStatus.Running, userKey, userName, now, now, null, 0, 0, string.Empty, null, null, null, null);
            var json = Serialize(state);
            if (raw is null)
            {
                _keyValues.SetValue(Constants.JobStateKey, json);
            }
            else if (!_keyValues.TrySetValue(Constants.JobStateKey, raw, json))
            {
                return null;
            }

            var run = new Run(this, state, json);
            using (ExecutionContext.SuppressFlow())
            {
                LastRun = Task.Run(() => run.ExecuteAsync(work));
            }

            return state;
        }
    }

    private static bool IsStale(JobState state, DateTime nowUtc) =>
        state.Status == JobStatus.Running && nowUtc - state.HeartbeatUtc > StaleAfter;

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Serialize(JobState state) => JsonSerializer.Serialize(state, Json);

    private static JobState? Deserialize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JobState>(raw, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// One running job. Every write is compare-and-set against the value this job wrote last: when another server took
    /// over (this job's heartbeat went stale), the job stops writing and is cancelled.
    /// </summary>
    private sealed class Run(JobRunner runner, JobState started, string startedRaw)
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly JobState _started = started;
        private JobState _state = started;
        private string _raw = startedRaw;
        private bool _closed;

        public async Task ExecuteAsync(
            Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid? ResultKey, TempKind? ResultKind, string? Summary)>> work)
        {
            var progress = new JobProgress(runner._timeProvider, (done, total, phase) =>
                Write(s => s with { Done = done, Total = total, Phase = phase, HeartbeatUtc = runner.Now() }, final: false));

            // Taken before Write, never inside it: Report and Beat hold the progress lock while they call Write.
            Func<JobState, JobState> Ended(JobStatus status, Func<JobState, JobState> result)
            {
                var (done, total, phase) = progress.Snapshot();
                return s =>
                {
                    var now = runner.Now();
                    return result(s with { Status = status, Done = done, Total = total, Phase = phase, HeartbeatUtc = now, CompletedUtc = now });
                };
            }

            using (runner._timeProvider.CreateTimer(_ => progress.Beat(), null, HeartbeatInterval, HeartbeatInterval))
            {
                try
                {
                    await using var scope = runner._scopeFactory.CreateAsyncScope();
                    var (key, kind, summary) = await work(scope.ServiceProvider, progress, _cancel.Token);
                    Write(Ended(JobStatus.Completed, s => s with { ResultKey = key, ResultKind = kind, Summary = summary }), final: true);
                }
                catch (JobFailedException ex)
                {
                    Write(Ended(JobStatus.Failed, s => s with { Error = ex.Error, Summary = ex.Summary }), final: true);
                }
                catch (Exception ex)
                {
                    runner._logger.LogError(ex, "Map to redirect {Kind} job {JobId} failed", _started.Kind, _started.Id);
                    Write(Ended(JobStatus.Failed, s => s with { Error = ex.Message }), final: true);
                }
            }

            // The token source is not disposed: a heartbeat that is still running may cancel it after the job ended.
        }

        private void Write(Func<JobState, JobState> change, bool final)
        {
            var lost = false;
            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                var next = change(_state);
                var json = Serialize(next);
                try
                {
                    if (runner._keyValues.TrySetValue(Constants.JobStateKey, _raw, json))
                    {
                        _state = next;
                        _raw = json;
                    }
                    else
                    {
                        lost = true;
                        _closed = true;
                        runner._logger.LogWarning("Map to redirect {Kind} job {JobId} was taken over by another job; it stops", _started.Kind, _started.Id);
                    }
                }
                catch (Exception ex)
                {
                    // A failed progress write must not fail the job; the next write or the heartbeat tries again.
                    runner._logger.LogWarning(ex, "Could not save the state of map to redirect {Kind} job {JobId}", _started.Kind, _started.Id);
                }

                _closed |= final;
            }

            if (lost && !final)
            {
                try
                {
                    _cancel.Cancel();
                }
                catch (AggregateException ex)
                {
                    // Runs on the heartbeat timer too, where an exception would end the process.
                    runner._logger.LogWarning(ex, "Cancelling map to redirect {Kind} job {JobId} failed", _started.Kind, _started.Id);
                }
            }
        }
    }
}

/// <summary>
/// Progress of a running job. <see cref="Report"/> saves at most every 500 ms, and always when the phase changes; the
/// job's heartbeat also saves the last reported values. Safe to call from any thread.
/// </summary>
public sealed class JobProgress
{
    internal static readonly TimeSpan SaveInterval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _timeProvider;
    private readonly Action<int, int, string> _save;
    private readonly Lock _gate = new();
    private int _done;
    private int _total;
    private string _phase = string.Empty;
    private DateTimeOffset? _lastSave;
    private string? _lastPhase;

    internal JobProgress(TimeProvider timeProvider, Action<int, int, string> save)
    {
        _timeProvider = timeProvider;
        _save = save;
    }

    /// <param name="phase">One of <see cref="JobPhases"/>.</param>
    public void Report(int done, int total, string phase)
    {
        lock (_gate)
        {
            _done = done;
            _total = total;
            _phase = phase;
            var now = _timeProvider.GetUtcNow();
            if (_lastSave is { } last && phase == _lastPhase && now - last < SaveInterval)
            {
                return;
            }

            Save(now);
        }
    }

    /// <summary>Saves the last reported values (refreshing the heartbeat).</summary>
    internal void Beat()
    {
        lock (_gate)
        {
            Save(_timeProvider.GetUtcNow());
        }
    }

    internal (int Done, int Total, string Phase) Snapshot()
    {
        lock (_gate)
        {
            return (_done, _total, _phase);
        }
    }

    private void Save(DateTimeOffset now)
    {
        _lastSave = now;
        _lastPhase = _phase;
        _save(_done, _total, _phase);
    }
}
