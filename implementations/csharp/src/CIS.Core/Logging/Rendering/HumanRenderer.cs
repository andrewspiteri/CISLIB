using System.Text;
using System.Text.Json;
using CIS.Logging.Events;

namespace CIS.Logging.Rendering;

internal static class HumanRenderer
{
    internal static RenderedHumanEvent Render(EncodedEvent captured, HumanOutputDetail detail, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (detail is not (HumanOutputDetail.Normal or HumanOutputDetail.Verbose)) { throw new ArgumentOutOfRangeException(nameof(detail)); }
        if (maximumBytes is < LoggingOptions.MinimumMaxEntryBytes or > LoggingOptions.MaximumMaxEntryBytes)
        { throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Use the configured entry-size range."); }
        using JsonDocument document = JsonDocument.Parse(captured.JsonLine);
        JsonElement root = document.RootElement;
        var output = new Writer(maximumBytes, captured.IsTruncated);
        output.Field("timestamp", root.GetProperty("timestamp"), required: true);
        output.Field("severity", root.GetProperty("status"), required: true);
        output.Field("component", root.GetProperty("component"), required: true);
        output.Field("event_name", root.GetProperty("event_name"), required: true);
        if (detail == HumanOutputDetail.Verbose)
        {
            foreach (string name in new[] { "service", "env", "version", "operation_id", "trace_id", "span_id" })
            {
                if (root.TryGetProperty(name, out JsonElement value)) { output.Field(name, value, required: true); }
            }
            foreach (JsonProperty property in root.GetProperty("resource").EnumerateObject())
            {
                if (!Descriptive(property.Name)) { output.Field("resource." + property.Name, property.Value, required: true); }
            }
        }
        output.Message(root.GetProperty("message").GetString()!);
        if (detail == HumanOutputDetail.Verbose)
        {
            output.Field("attributes", root.GetProperty("attributes"));
            foreach (string name in new[] { "exception", "message_template" })
            {
                if (root.TryGetProperty(name, out JsonElement value)) { output.Field(name, value); }
            }
            foreach (JsonProperty property in root.GetProperty("resource").EnumerateObject())
            {
                if (Descriptive(property.Name)) { output.Field("resource." + property.Name, property.Value); }
            }
        }
        return output.Finish();
    }

    private static bool Descriptive(string name) => name is "os_description" or "runtime_description";

    private sealed class Writer(int maximumBytes, bool isTruncated)
    {
        // Always reserve the loss marker, final LF and required empty message before any payload.
        private const string Marker = " truncated=true";
        private const int MessageOverhead = 11; // Space, message= and two quotes.
        private readonly StringBuilder text = new(Math.Min(maximumBytes, 1024));
        private int bytes;
        private bool hasMessage;
        private bool truncated = isTruncated;
        private int Available => maximumBytes - bytes - Marker.Length - 1 - (hasMessage ? 0 : MessageOverhead);

        internal void Field(string name, JsonElement value, bool required = false)
        {
            int overhead = (text.Length == 0 ? 0 : 1) + name.Length + 1; // All labels are owned ASCII.
            int size = HumanText.ValueSize(value, Math.Max(0, Available - overhead));
            if (overhead + size > Available)
            {
                if (required) { throw new EventEnvelopeTooLargeException(); }
                truncated = true; return; // Keep typed payloads whole; never shorten numeric strings or keys.
            }
            if (text.Length != 0) { text.Append(' '); }
            text.Append(name).Append('='); HumanText.AppendValue(text, value); bytes += overhead + size;
        }

        internal void Message(string message)
        {
            int contentBytes = Available;
            if (contentBytes < 0) { throw new EventEnvelopeTooLargeException(); }
            text.Append(" message=");
            int size = HumanText.AppendText(text, message, contentBytes, out bool clipped);
            bytes += MessageOverhead + size; hasMessage = true; truncated |= clipped;
        }

        internal RenderedHumanEvent Finish()
        {
            if (truncated) { text.Append(Marker); bytes += Marker.Length; }
            text.Append('\n'); bytes++;
            byte[] line = Encoding.UTF8.GetBytes(text.ToString());
            if (line.Length != bytes || bytes > maximumBytes) { throw new InvalidOperationException("Measured and rendered human sizes differ."); }
            return new RenderedHumanEvent(line, truncated);
        }
    }
}
