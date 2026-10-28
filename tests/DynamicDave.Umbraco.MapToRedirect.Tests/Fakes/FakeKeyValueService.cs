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

    public bool TrySetValue(string key, string originalValue, string newValue)
    {
        BeforeTrySet?.Invoke();
        lock (Values)
        {
            if (!Values.TryGetValue(key, out var current) || current != originalValue)
            {
                return false;
            }

            Values[key] = newValue;
            return true;
        }
    }
}
