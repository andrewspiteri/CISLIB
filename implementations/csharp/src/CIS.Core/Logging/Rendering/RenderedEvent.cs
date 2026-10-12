namespace CIS.Logging.Rendering;

// Keeps the owned array alive through readonly memory; JSONL does not allocate a second byte buffer.
internal readonly struct RenderedEvent
{
    internal RenderedEvent(ReadOnlyMemory<byte> line, bool isTruncated)
    {
        Line = line; IsTruncated = isTruncated;
    }
    internal ReadOnlyMemory<byte> Line { get; }
    internal bool IsTruncated { get; }
}
