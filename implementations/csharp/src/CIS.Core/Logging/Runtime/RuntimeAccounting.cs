namespace CIS.Logging.Runtime;

// One owned live boundary, not a registered flush/stop target or public diagnostics result.
internal readonly record struct RuntimeAccounting(bool AdmissionClosed, long InvalidEventCount, long TruncatedEventCount,
    OutputAccounting? Console, OutputAccounting? File);
