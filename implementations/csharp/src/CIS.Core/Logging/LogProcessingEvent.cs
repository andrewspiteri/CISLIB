using System.Text.Json;

namespace CIS.Logging;

/// <summary>An immutable owned event view supplied to background processing callbacks.</summary>
/// <remarks>Record is projected to the configured entry-byte limit before each callback. Omitted data never
/// reappears after later changes. Inspect the truncated marker when choosing an occurrence key.</remarks>
public sealed class LogProcessingEvent
{
    internal LogProcessingEvent(CoreLogLevel level, JsonElement record) { Level = level; Record = record; }
    /// <summary>The captured method severity.</summary>
    public CoreLogLevel Level { get; }
    /// <summary>The complete captured schema record; JsonElement is an immutable owned view.</summary>
    public JsonElement Record { get; }
    /// <summary>The captured event identity.</summary>
    public string EventName => Record.GetProperty("event_name").GetString()!;
    /// <summary>The component that captured this event.</summary>
    public string Component => Record.GetProperty("component").GetString()!;
    /// <summary>The message after preceding interceptors.</summary>
    public string Message => Record.GetProperty("message").GetString()!;
}
