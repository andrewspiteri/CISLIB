using System.Collections;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

// Cooperative payload probes and explicit admission controls; never a delivery simulation.
internal static class LoggerDecisionSources
{
    internal sealed class Attributes(List<string> trace, Action callback, bool poison) : IReadOnlyDictionary<string, LogValue>
    {
        private readonly Dictionary<string, LogValue> values = new() { ["probe"] = LogValue.String("original") };
        public int Count { get { Touch(); return values.Count; } }
        public IEnumerable<string> Keys { get { Touch(); return values.Keys; } }
        public IEnumerable<LogValue> Values { get { Touch(); return values.Values; } }
        public LogValue this[string key] { get { Touch(); return values[key]; } }
        public bool ContainsKey(string key) { Touch(); return values.ContainsKey(key); }
        public bool TryGetValue(string key, out LogValue value) { Touch(); return values.TryGetValue(key, out value!); }
        public IEnumerator<KeyValuePair<string, LogValue>> GetEnumerator()
        {
            Touch();
            return values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private void Touch()
        {
            trace.Add("attributes");
            if (poison) { throw new IOException("CIS_PAYLOAD_MUST_NOT_BE_READ"); }
            callback();
        }
    }

    internal sealed class ObservedException(List<string> trace, bool poison, Action callback) : Exception
    {
        public override string Message
        {
            get
            {
                trace.Add("exception");
                if (poison) { throw new IOException("CIS_EXCEPTION_MUST_NOT_BE_READ"); }
                callback();
                return "exception-original";
            }
        }
        public override string? StackTrace
        {
            get
            {
                trace.Add("stack");
                if (poison) { throw new IOException("CIS_STACK_MUST_NOT_BE_READ"); }
                return null;
            }
        }
    }

    internal sealed class Clock(List<string> trace, Action callback) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            trace.Add("clock");
            callback();
            return EventQualificationFixture.Instant;
        }
    }

    internal sealed class Output(string name, List<string> trace) : ILogOutputAdmission
    {
        internal bool Faulted { get; set; }
        internal bool Full { get; set; }
        internal int FaultReads { get; private set; }
        internal int Attempts { get; private set; }
        internal int Filtered { get; private set; }
        internal int Unavailable { get; private set; }
        internal List<EncodedEvent> Events { get; } = [];
        public bool IsFaulted { get { FaultReads++; return Faulted; } }
        public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence)
        {
            trace.Add(name + ".admit");
            Attempts++;
            acceptedSequence = null;
            if (Full) { return LogOutputWriteStatus.QueueFull; }
            Events.Add(captured);
            acceptedSequence = Events.Count;
            return LogOutputWriteStatus.Accepted;
        }
        public void RecordFiltered() => Filtered++;
        public void RecordUnavailable() { Attempts++; Unavailable++; }
    }
}
