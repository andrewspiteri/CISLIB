using System.Collections.Concurrent;
using System.Text;

namespace CIS.TestSupport;

/// <summary>Blocks asynchronous writes at an observable signal until explicitly released or failed.</summary>
public sealed class ControlledTextWriter(TimeSpan deadline) : TextWriter
{
    private readonly AsyncSignal released = new();
    private readonly ConcurrentQueue<string> writes = new();
    private Exception? failure;

    public override Encoding Encoding => Encoding.UTF8;
    public AsyncSignal Entered { get; } = new();
    public IReadOnlyList<string> Writes => writes.ToArray();

    public void Release() => released.Set();

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Exchange(ref failure, exception);
        released.Set();
    }

    public override Task WriteAsync(string? value) => WriteAsync(value.AsMemory());

    public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        string captured = buffer.ToString();
        Entered.Set();
        await released.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
        Exception? exception = Volatile.Read(ref failure);
        if (exception is not null) { throw exception; }
        writes.Enqueue(captured);
    }
}
