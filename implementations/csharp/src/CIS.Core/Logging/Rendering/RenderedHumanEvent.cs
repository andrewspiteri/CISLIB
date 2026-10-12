namespace CIS.Logging.Rendering;

// Owns display bytes, including their final LF. Loss includes capture and any additional display omission.
internal sealed class RenderedHumanEvent
{
    private readonly byte[] line;
    internal RenderedHumanEvent(byte[] line, bool isTruncated)
    {
        this.line = line; IsTruncated = isTruncated;
    }
    internal ReadOnlyMemory<byte> Line => line;
    internal bool IsTruncated { get; }
}
