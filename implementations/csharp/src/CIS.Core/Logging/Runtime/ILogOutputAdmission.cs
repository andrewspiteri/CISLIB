using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// Trusted Core implementation boundary. Calls occur under the runtime gate and must be
// nonblocking, perform no I/O/caller callbacks, and schedule no synchronous continuations.
// The output owns its ticket and counters. Running/fault reads must be thread-safe.
internal interface ILogOutputAdmission
{
    bool IsFaulted { get; }
    LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence);
    void RecordFiltered();
    void RecordUnavailable();
}
