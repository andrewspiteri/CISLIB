using CIS.Logging.Events;

namespace CIS.Logging.Rendering;

// Presentation receives only owned capture and explicit choices, never routing/filtering settings.
internal static class EventRenderer
{
    internal static RenderedEvent Render(EncodedEvent captured, LogOutputFormat format, HumanOutputDetail detail, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (format is not (LogOutputFormat.Human or LogOutputFormat.JsonLines)) { throw new ArgumentOutOfRangeException(nameof(format)); }
        if (detail is not (HumanOutputDetail.Normal or HumanOutputDetail.Verbose)) { throw new ArgumentOutOfRangeException(nameof(detail)); }
        if (maximumBytes is < LoggingOptions.MinimumMaxEntryBytes or > LoggingOptions.MaximumMaxEntryBytes)
        { throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Use the configured entry-size range."); }
        if (format == LogOutputFormat.JsonLines)
        {
            if (captured.JsonLine.Length > maximumBytes) { throw new InvalidOperationException("Captured JSONL exceeds the selected output entry limit."); }
            return new RenderedEvent(captured.JsonLine, captured.IsTruncated);
        }
        RenderedHumanEvent human = HumanRenderer.Render(captured, detail, maximumBytes);
        return new RenderedEvent(human.Line, human.IsTruncated);
    }
}
