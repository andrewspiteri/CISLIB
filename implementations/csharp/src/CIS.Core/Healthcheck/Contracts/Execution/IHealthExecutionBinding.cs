namespace CIS.Healthcheck;

/// <summary>Accepts detached scheduling facts for one suitable health monitor.</summary>
public interface IHealthExecutionBinding : IAsyncDisposable
{
    /// <summary>Gets the correlated scheduling registration identity.</summary>
    Guid RegistrationId { get; }

    /// <summary>Gets the associated health monitor identity.</summary>
    Guid MonitorId { get; }

    /// <summary>Synchronously commits or classifies one fact without I/O or delivery queuing.</summary>
    /// <exception cref="ArgumentException">The fact is malformed or belongs to another registration.</exception>
    /// <exception cref="InvalidOperationException">A terminal fact contradicts a retained terminal payload; the message is a bounded safe integration reason.</exception>
    /// <remarks>The caller isolates reporting faults from callback execution and its error handler.</remarks>
    HealthFactDisposition Report(HealthExecutionFact fact);
}
