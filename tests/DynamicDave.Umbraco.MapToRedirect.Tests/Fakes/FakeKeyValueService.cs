using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>
/// Same compare-and-set semantics as Umbraco's KeyValueService: TrySetValue fails for a missing key. Thread-safe, as
/// background jobs write while tests read.
/// </summary>
internal sealed class FakeKeyValueService : IKeyValueService
{
    public Dictionary<string, string> Values { get; } = [];

    /// <summary>Runs at the start of TrySetValue (outside the lock), e.g. to change the value between a caller's read and its write.</summary>
    public Action? BeforeTrySet { get; set; }

    public string? GetValue(string key)
    {
        lock (Values) return Values.TryGetValue(key, out var value) ? value : null;
    }

    public IReadOnlyDictionary<string, string?>? FindByKeyPrefix(string keyPrefix)
    {
        lock (Values)
        {
            return Values.Where(kv => kv.Key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        }
    }

    public void SetValue(string key, string value)
    {
        lock (Values) Values[key] = value;
    }

    public void SetValue(string key, string originalValue, string newValue)
    {
        if (!TrySetValue(key, originalValue, newValue))
        {
            throw new InvalidOperationException("Value changed");
        }
    }

    /// <summary>The next this many TrySetValue calls throw, like a transient database error, without writing.</summary>
    public int FailingTrySets { get; set; }

    /// <summary>The next TrySetValue call writes and then throws, like a commit whose acknowledgement was lost.</summary>
    public bool ThrowAfterNextWrite { get; set; }

    public bool TrySetValue(string key, string originalValue, string newValue)
    {
        BeforeTrySet?.Invoke();
        lock (Values)
        {
            if (FailingTrySets > 0)
            {
                FailingTrySets--;
                throw new InvalidOperationException("Database timeout");
            }

            if (!Values.TryGetValue(key, out var current) || current != originalValue)
            {
                return false;
            }

            Values[key] = newValue;
            if (ThrowAfterNextWrite)
            {
                ThrowAfterNextWrite = false;
                throw new InvalidOperationException("Connection lost after commit");
            }

            return true;
        }
    }
}
