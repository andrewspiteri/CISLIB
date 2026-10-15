namespace CIS.Logging;

/// <summary>A destination receiving complete owned JSON Lines records from its independent worker.</summary>
/// <remarks>One runtime invokes a registered writer serially. Cancellation is cooperative. A writer must not
/// call back into a flush or stop operation on its own runtime while servicing that operation.</remarks>
public interface ILogWriter : IAsyncDisposable
{
    /// <summary>Writes one immutable JSON record including its final line feed.</summary>
    ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken);

    /// <summary>Confirms the writer's buffer flush; this does not imply durable storage.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken);
}
