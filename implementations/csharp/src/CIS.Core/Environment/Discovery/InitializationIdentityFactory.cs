namespace CIS.Configuration;

internal sealed class InitializationIdentityFactory
{
    private readonly TimeProvider clock;
    private readonly Func<Guid> newInstanceId;

    public InitializationIdentityFactory(TimeProvider? clock = null, Func<Guid>? newInstanceId = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.newInstanceId = newInstanceId ?? Guid.NewGuid;
    }

    public InitializationIdentity Create()
    {
        Guid id = newInstanceId();
        if (id == Guid.Empty)
        {
            throw new InvalidOperationException("Initialization identity generation returned an empty UUID.");
        }

        DateTimeOffset initializedAtUtc = clock.GetUtcNow().ToUniversalTime();
        return new InitializationIdentity(id.ToString("D"), initializedAtUtc);
    }
}
