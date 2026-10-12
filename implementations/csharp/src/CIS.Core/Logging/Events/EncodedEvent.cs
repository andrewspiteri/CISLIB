namespace CIS.Logging.Events;

// Owns one complete JSONL record. Consumers receive no mutable buffer or caller inputs.
internal sealed class EncodedEvent
{
    private readonly byte[] jsonLine;

    internal EncodedEvent(byte[] jsonLine, bool isTruncated)
    {
        this.jsonLine = jsonLine;
        IsTruncated = isTruncated;
    }

    internal ReadOnlyMemory<byte> JsonLine => jsonLine;
    internal bool IsTruncated { get; }
}
