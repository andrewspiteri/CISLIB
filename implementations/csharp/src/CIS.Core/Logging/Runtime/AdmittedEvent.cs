using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// A bounded value slot owns only the already captured event and this output's successful ticket.
internal readonly record struct AdmittedEvent(EncodedEvent Event, long AcceptedSequence);
