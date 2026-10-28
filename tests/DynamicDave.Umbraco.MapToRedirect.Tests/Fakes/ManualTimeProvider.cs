namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>A clock the test moves forward by hand. Timers are real (they never fire within a test's lifetime).</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
