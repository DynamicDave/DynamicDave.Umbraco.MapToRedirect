using System.Data;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using IScope = Umbraco.Cms.Infrastructure.Scoping.IScope;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// Scope provider without a database: <see cref="Ambient"/> is the innermost open scope. Each scope snapshots the
/// participants when created and restores them when disposed without Complete, like a transaction rollback.
/// </summary>
internal sealed class FakeScopeProvider(params IFakeTransactional[] participants) : IScopeProvider
{
    private readonly Stack<FakeScope> _open = new();

    public List<FakeScope> Created { get; } = [];

    public FakeScope? Ambient => _open.Count == 0 ? null : _open.Peek();

    public IScopeContext? Context => null;

    public ISqlContext SqlContext => throw new NotSupportedException();

    public IScope CreateScope(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        RepositoryCacheMode repositoryCacheMode = RepositoryCacheMode.Unspecified,
        IEventDispatcher? eventDispatcher = null,
        IScopedNotificationPublisher? scopedNotificationPublisher = null,
        bool? scopeFileSystems = null,
        bool callContext = false,
        bool autoComplete = false)
    {
        var scope = new FakeScope(this, participants.Select(p => (p, p.Snapshot())).ToList());
        Created.Add(scope);
        _open.Push(scope);
        return scope;
    }

    public IScope CreateDetachedScope(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        RepositoryCacheMode repositoryCacheMode = RepositoryCacheMode.Unspecified,
        IEventDispatcher? eventDispatcher = null,
        IScopedNotificationPublisher? scopedNotificationPublisher = null,
        bool? scopeFileSystems = null) => throw new NotSupportedException();

    public void AttachScope(IScope scope, bool callContext = false) => throw new NotSupportedException();

    public IScope DetachScope() => throw new NotSupportedException();

    internal void Close(FakeScope scope)
    {
        if (_open.Count == 0 || _open.Peek() != scope) throw new InvalidOperationException("Scopes must be disposed in order.");
        _open.Pop();
    }
}

internal sealed class FakeScope(FakeScopeProvider provider, IReadOnlyList<(IFakeTransactional Participant, object Snapshot)> snapshots) : IScope
{
    public bool Completed { get; private set; }

    public bool Disposed { get; private set; }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public int CreatedThreadId => Environment.CurrentManagedThreadId;

    public IUmbracoDatabase Database => throw new NotSupportedException();

    public ISqlContext SqlContext => throw new NotSupportedException();

    public ILockingMechanism Locks => throw new NotSupportedException();

    public IScopedNotificationPublisher Notifications => throw new NotSupportedException();

    public RepositoryCacheMode RepositoryCacheMode => RepositoryCacheMode.Unspecified;

    public IsolatedCaches IsolatedCaches => throw new NotSupportedException();

    public bool Complete() => Completed = true;

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        if (!Completed)
        {
            foreach (var (participant, snapshot) in snapshots) participant.Restore(snapshot);
        }

        provider.Close(this);
    }

    public void ReadLock(params int[] lockIds) { }

    public void ReadLock(TimeSpan timeout, int lockId) { }

    public void WriteLock(params int[] lockIds) { }

    public void WriteLock(TimeSpan timeout, int lockId) { }

    public void EagerReadLock(params int[] lockIds) { }

    public void EagerReadLock(TimeSpan timeout, int lockId) { }

    public void EagerWriteLock(params int[] lockIds) { }

    public void EagerWriteLock(TimeSpan timeout, int lockId) { }
}
