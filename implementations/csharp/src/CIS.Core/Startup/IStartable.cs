namespace CIS.Startup;

/// <summary>Initializes one explicitly enrolled root singleton before application readiness.</summary>
/// <remarks>
/// Register the instance as an ordinary unkeyed singleton IStartable. Constructors capture dependencies;
/// this method performs required asynchronous initialization. The returned task includes all work needed
/// by subsequent participants and readiness, rather than the lifetime of an endless background loop.
/// The application and DI container retain disposal responsibility.
/// </remarks>
public interface IStartable
{
    /// <summary>Completes mandatory initialization before the next startup participant is invoked.</summary>
    /// <param name="cancellationToken">The first startup operation's host-owned cancellation, including application stopping.</param>
    /// <returns>A nonnull task representing actual initialization and its owned cleanup.</returns>
    Task StartAsync(CancellationToken cancellationToken);
}
