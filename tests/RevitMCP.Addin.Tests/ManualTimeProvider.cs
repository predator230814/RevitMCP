namespace RevitMCP.Addin.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan elapsed)
    {
        _timestamp += elapsed.Ticks * TimestampFrequency / TimeSpan.TicksPerSecond;
    }
}
