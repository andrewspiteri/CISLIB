using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

// Explicit non-I/O admission control. It records calls and owns tickets; it never simulates delivery.
internal static class LoggerOutcomeSources
{
    internal sealed class Output(string mode, long seed, int capacity) : ILogOutputAdmission
    {
        private long sequence = seed;
        public bool IsFaulted { get; private set; } = mode is "faulted" or "filtered-faulted";
        internal int Attempts { get; private set; }
        internal int TryCalls { get; private set; }
        internal int Filtered { get; private set; }
        internal int Unavailable { get; private set; }
        internal int QueueFull { get; private set; }
        internal List<EncodedEvent> Events { get; } = [];
        public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence)
        {
            Attempts++; TryCalls++;
            acceptedSequence = null;
            if (mode == "admission-fault")
            {
                IsFaulted = true; Unavailable++;
                return LogOutputWriteStatus.Faulted;
            }
            if (mode == "full" || Events.Count >= capacity)
            {
                QueueFull++;
                return LogOutputWriteStatus.QueueFull;
            }
            Events.Add(captured);
            acceptedSequence = ++sequence;
            return LogOutputWriteStatus.Accepted;
        }
        public void RecordFiltered() => Filtered++;
        public void RecordUnavailable() { Attempts++; Unavailable++; }
    }
}
