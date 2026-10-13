using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// A bounded slot keeps owned capture, its successful ticket and optional prepared output bytes alive.
internal readonly record struct AdmittedEvent(EncodedEvent Event, long AcceptedSequence, ReadOnlyMemory<byte>? PreparedLine = null);
