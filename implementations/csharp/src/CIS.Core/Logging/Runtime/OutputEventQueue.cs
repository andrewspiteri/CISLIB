using System.Threading.Channels;
using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// One queue and one awaited writer operation. Only trusted internal composition supplies the writer.
internal sealed class OutputEventQueue
{
    private readonly Channel<EncodedEvent> channel;
    private readonly Func<EncodedEvent, ValueTask> write;
    internal OutputEventQueue(int capacity, Func<EncodedEvent, ValueTask> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        this.write = write;
        channel = Channel.CreateBounded<EncodedEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        // Captured events already contain their context. A worker must not retain its creator's flow.
        if (ExecutionContext.IsFlowSuppressed()) { Completion = Task.Run(ProcessAsync); }
        else { using (ExecutionContext.SuppressFlow()) { Completion = Task.Run(ProcessAsync); } }
    }
    internal Task Completion { get; }
    internal int PendingCount => channel.Reader.Count;
    internal bool TryEnqueue(EncodedEvent captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        return channel.Writer.TryWrite(captured);
    }
    // Primitive queue closure, not the public runtime's flush/deadline/shutdown protocol.
    internal void Complete() => channel.Writer.TryComplete();
    private async Task ProcessAsync()
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out EncodedEvent? captured)) { await write(captured).ConfigureAwait(false); }
            }
        }
        finally
        {
            // A failed callback cannot leave a live admission queue or retained unattempted work.
            // Later output accounting owns the terminal failed/abandoned observations.
            channel.Writer.TryComplete();
            while (channel.Reader.TryRead(out _)) { }
        }
    }
}
