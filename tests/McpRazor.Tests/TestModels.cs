namespace McpRazor.Tests;

public enum OrderStatus { Draft, Confirmed }

public sealed record OrderData(string Id, OrderStatus Status, decimal Total);

/// <summary>A clock tests can move forward.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
