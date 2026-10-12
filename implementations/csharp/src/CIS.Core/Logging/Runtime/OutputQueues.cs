using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// Creates exactly one independent worker for each enabled destination and no disabled worker.
internal sealed class OutputQueues
{
    internal OutputQueues(int capacity, bool consoleEnabled, bool fileEnabled,
        Func<EncodedEvent, ValueTask>? consoleWrite, Func<EncodedEvent, ValueTask>? fileWrite)
    {
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        if (consoleEnabled != (consoleWrite is not null)) { throw new ArgumentException("Writer presence must match enabled console output.", nameof(consoleWrite)); }
        if (fileEnabled != (fileWrite is not null)) { throw new ArgumentException("Writer presence must match enabled file output.", nameof(fileWrite)); }
        if (consoleWrite is not null) { Console = new OutputEventQueue(capacity, consoleWrite); }
        try { if (fileWrite is not null) { File = new OutputEventQueue(capacity, fileWrite); } }
        catch { Console?.Complete(); throw; }
        Completion = Task.WhenAll(Console?.Completion ?? Task.CompletedTask, File?.Completion ?? Task.CompletedTask);
    }
    internal OutputEventQueue? Console { get; }
    internal OutputEventQueue? File { get; }
    internal Task Completion { get; }
    internal void Complete() { Console?.Complete(); File?.Complete(); }
}
