using System.Buffers;
using System.Text.Json;

namespace CIS.Logging.Events;

internal static class EventEncoder
{
    internal static EncodedEvent Encode(CapturedEventFacts facts, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (maximumBytes is < LoggingOptions.MinimumMaxEntryBytes or > LoggingOptions.MaximumMaxEntryBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Use the configured entry-size range.");
        }
        int size = EventWireFormat.Size(facts, maximumBytes);
        if (size > maximumBytes)
        {
            facts = EventByteProjection.Fit(facts, maximumBytes);
            size = EventWireFormat.Size(facts, maximumBytes);
        }
        if (size > maximumBytes) { throw new InvalidOperationException("Projected event exceeds its byte budget."); }
        var buffer = new ArrayBufferWriter<byte>(size);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JsonTextBytes.Encoder }))
        {
            EventWireFormat.Write(writer, facts); writer.Flush();
        }
        buffer.GetSpan(1)[0] = (byte)'\n'; buffer.Advance(1);
        if (buffer.WrittenCount != size)
        {
            throw new InvalidOperationException("Measured and encoded event sizes differ.");
        }
        return new EncodedEvent(buffer.WrittenSpan.ToArray(), facts.IsTruncated);
    }
}

internal sealed class EventEnvelopeTooLargeException() : ArgumentException("The protected event envelope exceeds the configured entry limit.");
