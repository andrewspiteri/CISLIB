namespace CIS.Tests.UnitTests.Discovery;

internal sealed class InitializationTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
    public int Reads { get; private set; }
    public Exception? Failure { get; init; }

    public override DateTimeOffset GetUtcNow()
    {
        Reads++;
        if (Failure is not null)
        {
            throw Failure;
        }

        return UtcNow;
    }
}
