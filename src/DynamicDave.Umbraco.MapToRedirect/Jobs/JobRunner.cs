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

    /// <summary><see cref="JobState.Error"/> of a job that failed with an unexpected exception (details are in the log).</summary>
    public const string UnexpectedError = "Unexpected";

    /// <summary><see cref="JobState.Error"/> of a job that stopped after a cancel request (<see cref="RequestCancel"/>).</summary>
    public const string CancelledError = "Cancelled";

    // A running job writes its progress at most every 500 ms, so a few attempts always find a quiet moment.
    private const int CancelAttempts = 20;

    /// <summary>Waits between the attempts of a job's final state write (one attempt plus one retry per delay).</summary>
    internal TimeSpan[] FinalWriteRetryDelays { get; init; } =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)];

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

        // Every claim is a compare-and-set on the raw stored value, which covers other servers; the package migration
        // (SeedJobState) creates the row, so there is a value to compare against. The lock only spares this process a
        // database round trip for a double click.
        lock (_startGate)
        {
            var raw = _keyValues.GetValue(Constants.JobStateKey);
            if (raw is null)
            {
                // Removed after the migration ran (compare-and-set fails for a missing row): recreate it empty, as the
                // migration does, and claim it with the normal compare-and-set.
                _logger.LogWarning(
                    "The job state row {Key} of {Package} was missing; it is recreated", Constants.JobStateKey, Constants.PackageName);
                _keyValues.SetValue(Constants.JobStateKey, string.Empty);
                raw = string.Empty;
            }

            var now = Now();
            var current = Deserialize(raw);
            if (current is { Status: JobStatus.Running } && !IsStale(current, now))
            {
                return null;
            }

            var state = new JobState(Guid.NewGuid(), kind, JobStatus.Running, userKey, userName, now, now, null, 0, 0, string.Empty, null, null, null, null);
            var json = Serialize(state);
            if (!_keyValues.TrySetValue(Constants.JobStateKey, raw, json))
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

    /// <summary>
    /// Asks the running job to stop: stores <see cref="JobState.CancelRequested"/> with compare-and-set, so the request
    /// reaches the job on whichever server runs it. The job sees it on its next progress write or heartbeat, cancels its
    /// token and ends <see cref="JobStatus.Failed"/> with <see cref="CancelledError"/> (or Completed when its work
    /// finished first). Returns the running job's state with the request, or null when no job is running.
    /// </summary>
    public JobState? RequestCancel()
    {
        for (var attempt = 0; attempt < CancelAttempts; attempt++)
        {
            var raw = _keyValues.GetValue(Constants.JobStateKey);
            var current = Deserialize(raw);
            if (raw is null || current is not { Status: JobStatus.Running } || IsStale(current, Now())) return null;
            if (current.CancelRequested) return current;

            var requested = current with { CancelRequested = true };
            if (_keyValues.TrySetValue(Constants.JobStateKey, raw, Serialize(requested))) return requested;
        }

        throw new InvalidOperationException("Could not store the cancel request of map to redirect job; the job state kept changing.");
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
    /// over (this job's heartbeat went stale), the job stops writing and is cancelled. A cancel request
    /// (<see cref="RequestCancel"/>) also makes the compare-and-set fail: the write then adopts the stored request,
    /// cancels the token and writes again.
    /// </summary>
    private sealed class Run(JobRunner runner, JobState started, string startedRaw)
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly JobState _started = started;
        private JobState _state = started;
        private string _raw = startedRaw;
        private bool _closed;
        private volatile bool _cancelRequested;

        // The last write that threw: it may have committed anyway.
        private (JobState State, string Json)? _unconfirmed;

        public async Task ExecuteAsync(
            Func<IServiceProvider, JobProgress, CancellationToken, Task<(Guid? ResultKey, TempKind? ResultKind, string? Summary)>> work)
        {
            var progress = new JobProgress(runner._timeProvider, (done, total, phase) =>
                Write(s => s with { Done = done, Total = total, Phase = phase, HeartbeatUtc = runner.Now() }, close: false));

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
                Func<JobState, JobState> final;
                try
                {
                    await using var scope = runner._scopeFactory.CreateAsyncScope();
                    var (key, kind, summary) = await work(scope.ServiceProvider, progress, _cancel.Token);
                    final = Ended(JobStatus.Completed, s => s with { ResultKey = key, ResultKind = kind, Summary = summary });
                }
                catch (JobFailedException ex)
                {
                    final = Ended(JobStatus.Failed, s => s with { Error = ex.Error, Summary = ex.Summary });
                }
                catch (OperationCanceledException) when (_cancelRequested)
                {
                    runner._logger.LogInformation("Map to redirect {Kind} job {JobId} was cancelled", _started.Kind, _started.Id);
                    final = Ended(JobStatus.Failed, s => s with { Error = CancelledError });
                }
                catch (Exception ex)
                {
                    runner._logger.LogError(ex, "Map to redirect {Kind} job {JobId} failed", _started.Kind, _started.Id);
                    final = Ended(JobStatus.Failed, s => s with { Error = UnexpectedError });
                }

                await WriteFinalAsync(final);
            }

            // The token source is not disposed: a heartbeat that is still running may cancel it after the job ended.
        }

        /// <summary>
        /// Writes the final state, retrying after a failed write (the heartbeat keeps the job alive meanwhile), so a
        /// transient database error does not turn a finished job into an aborted one.
        /// </summary>
        private async Task WriteFinalAsync(Func<JobState, JobState> final)
        {
            var delays = runner.FinalWriteRetryDelays;
            for (var attempt = 0; ; attempt++)
            {
                if (Write(final, close: true) != WriteResult.Failed)
                {
                    return;
                }

                if (attempt >= delays.Length)
                {
                    break;
                }

                await Task.Delay(delays[attempt], runner._timeProvider);
            }

            JobState unsaved;
            lock (_gate)
            {
                _closed = true;
                unsaved = final(_state);
            }

            runner._logger.LogError(
                "Could not save the final state of map to redirect {Kind} job {JobId}: {Status}, result {ResultKey} ({ResultKind}), error {Error}",
                _started.Kind, _started.Id, unsaved.Status, unsaved.ResultKey, unsaved.ResultKind, unsaved.Error);
        }

        private WriteResult Write(Func<JobState, JobState> change, bool close)
        {
            WriteResult result;
            var cancelSeen = false;
            lock (_gate)
            {
                if (_closed)
                {
                    return WriteResult.Closed;
                }

                var next = change(_state);
                var json = Serialize(next);
                result = TrySet(json);

                // A write that threw after it committed makes the next compare-and-set fail against our own value.
                if (result == WriteResult.Lost && _unconfirmed is { } unconfirmed && SafeRead() == unconfirmed.Json)
                {
                    _state = unconfirmed.State;
                    _raw = unconfirmed.Json;
                    next = change(_state);
                    json = Serialize(next);
                    result = TrySet(json);
                }

                // A cancel request for this job changed the stored value: take it over and write again.
                if (result == WriteResult.Lost && AdoptCancelRequest())
                {
                    cancelSeen = true;
                    next = change(_state);
                    json = Serialize(next);
                    result = TrySet(json);
                }

                switch (result)
                {
                    case WriteResult.Written:
                        _state = next;
                        _raw = json;
                        _unconfirmed = null;
                        _closed = close;
                        break;
                    case WriteResult.Failed:
                        _unconfirmed = (next, json);
                        break;
                    case WriteResult.Lost:
                        _closed = true;
                        runner._logger.LogWarning("Map to redirect {Kind} job {JobId} was taken over by another job; it stops", _started.Kind, _started.Id);
                        break;
                }
            }

            if (result == WriteResult.Lost || cancelSeen)
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

            return result;
        }

        // True when the stored value is this job, still running, with a cancel request; it becomes the value to compare against.
        private bool AdoptCancelRequest()
        {
            var raw = SafeRead();
            if (Deserialize(raw) is not { Status: JobStatus.Running, CancelRequested: true } stored || stored.Id != _started.Id) return false;

            _state = stored;
            _raw = raw!;
            _unconfirmed = null;
            _cancelRequested = true;
            return true;
        }

        private WriteResult TrySet(string json)
        {
            try
            {
                return runner._keyValues.TrySetValue(Constants.JobStateKey, _raw, json) ? WriteResult.Written : WriteResult.Lost;
            }
            catch (Exception ex)
            {
                // A failed progress write must not fail the job; the next write or the heartbeat tries again.
                runner._logger.LogWarning(ex, "Could not save the state of map to redirect {Kind} job {JobId}", _started.Kind, _started.Id);
                return WriteResult.Failed;
            }
        }

        private string? SafeRead()
        {
            try
            {
                return runner._keyValues.GetValue(Constants.JobStateKey);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private enum WriteResult
    {
        Written,
        Lost,
        Failed,
        Closed,
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
