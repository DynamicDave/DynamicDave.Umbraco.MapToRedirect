namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// In-memory state that takes part in <see cref="FakeScopeProvider"/> scopes: a scope disposed without Complete puts
/// the state back as it was when the scope was created, like a database transaction rollback.
/// </summary>
internal interface IFakeTransactional
{
    object Snapshot();

    void Restore(object snapshot);
}
