using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerCaptureSources
{
    // Explicit admission plan: output owns its tickets/counters; no queue worker or delivery.
    internal sealed class Output(LogOutputWriteStatus[] plan, long seed) : ILogOutputAdmission
    {
        private long sequence = seed;
        internal int Attempts { get; private set; }
        internal int QueueFull { get; private set; }
        internal int Unavailable { get; private set; }
        internal List<EncodedEvent> Events { get; } = [];
        public bool IsFaulted { get; private set; }
        public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence)
        {
            LogOutputWriteStatus status = plan[Attempts++];
            acceptedSequence = null;
            if (status == LogOutputWriteStatus.Accepted) { Events.Add(captured); acceptedSequence = ++sequence; }
            else if (status == LogOutputWriteStatus.QueueFull) { QueueFull++; }
            else if (status == LogOutputWriteStatus.Faulted) { Unavailable++; IsFaulted = true; }
            return status;
        }
        public void RecordFiltered() { }
        public void RecordUnavailable() { Attempts++; Unavailable++; }
    }

    internal static LogEventData Data(string payload) => payload switch
    {
        "name-null" => new() { EventName = null!, Message = "original" },
        "name-empty" => new() { EventName = "", Message = "original" },
        "name-control" => new() { EventName = "CIS_PRIVATE_NAME\n", Message = "original" },
        "name-unicode" => new() { EventName = Malformed(), Message = "original" },
        "message-null" => new() { EventName = "capture.event", Message = null! },
        "message-unicode" => new() { EventName = "capture.event", Message = Malformed() },
        "template-unicode" => new() { EventName = "capture.event", Message = "original", MessageTemplate = Malformed() },
        "operation-control" => new() { EventName = "capture.event", Message = "original", OperationId = "CIS_PRIVATE_OPERATION\ninside" },
        "operation-unicode" => new() { EventName = "capture.event", Message = "original", OperationId = Malformed() },
        "attribute-key" => new() { EventName = "capture.event", Message = "original", Attributes = new Dictionary<string, LogValue> { ["CIS_PRIVATE_KEY\n"] = LogValue.String("value") } },
        "attribute-null" => new() { EventName = "capture.event", Message = "original", Attributes = new Dictionary<string, LogValue> { ["probe"] = null! } },
        "attribute-throws" => new() { EventName = "capture.event", Message = "original", Attributes = new LoggerDecisionSources.Attributes([], () => { }, true) },
        "exception-throws" => new() { EventName = "capture.event", Message = "original", Exception = new LoggerDecisionSources.ObservedException([], true, () => { }) },
        "long" => new() { EventName = "capture.event", Message = new string('x', 9000) },
        _ => new() { EventName = "capture.event", Message = "original" }
    };
    private static string Malformed() => "CIS_PRIVATE_UNICODE" + new string((char)0xD800, 1);
}
