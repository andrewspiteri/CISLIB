namespace CIS.Logging.Rendering;

// Owns display bytes, including their final LF. Rendering loss is separate from capture loss.
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
