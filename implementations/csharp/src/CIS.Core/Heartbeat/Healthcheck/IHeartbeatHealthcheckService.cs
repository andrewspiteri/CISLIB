namespace CIS.Heartbeat.Healthcheck;

/// <summary>An optional owner-service capability for monitored registration through public contracts.</summary>
/// <remarks>The production implementation atomically binds Core monitors before work can run.
/// Other implementations define their supported monitor providers and must retain accepted-work settlement.</remarks>
public interface IHeartbeatHealthcheckService : IHeartbeatService
{
    /// <summary>Registers monitored work on this same owner, without invoking subscriber code inline.</summary>
    void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
        HeartbeatHealthcheckOptions monitoring, Func<CancellationToken, Task<bool>>? condition = null);
}
