// WP02-S03 nonshipping, reference-only runtime/result signatures. No implementation.
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CIS.Logging;

public enum LogWriteOutcome : int
{
    Accepted = 0,
    PartiallyAccepted = 1,
    Filtered = 2,
    Rejected = 3,
    Stopped = 4,
    InvalidEvent = 5,
}

public enum LogOutputWriteStatus : int
{
    Accepted = 0,
    Filtered = 1,
    QueueFull = 2,
    Faulted = 3,
    Stopped = 4,
}

public enum LogCompletion : int
{
    Completed = 0,
    TimedOut = 1,
}

public enum LogOutputState : int
{
    Running = 0,
    Faulted = 1,
    Stopping = 2,
    Stopped = 3,
}

public enum CoreLogState : int
{
    Running = 0,
    Stopping = 1,
    Stopped = 2,
}

public enum LogWriterFlushStatus : int
{
    NotRequested = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
}

public sealed class LogWriteResult
{
    private LogWriteResult() { }
    public extern LogWriteOutcome Outcome { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern IReadOnlyList<LogOutputWriteResult> Outputs { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern string? ErrorCode { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool IsTruncated { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class LogOutputWriteResult
{
    private LogOutputWriteResult() { }
    public extern string OutputName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern LogOutputWriteStatus Status { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long? AcceptedSequence { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class LogFlushResult
{
    private LogFlushResult() { }
    public extern LogCompletion Completion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool Succeeded { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool LossObserved { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset BoundaryCapturedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset ObservedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long InvalidEventCount { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long TruncatedEventCount { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern IReadOnlyList<LogOutputDrainResult> Outputs { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class LogStopResult
{
    private LogStopResult() { }
    public extern LogCompletion Completion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool Succeeded { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool LossObserved { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset BoundaryCapturedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset ObservedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long InvalidEventCount { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long TruncatedEventCount { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern IReadOnlyList<LogOutputDrainResult> Outputs { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class LogOutputDrainResult
{
    private LogOutputDrainResult() { }
    public extern string OutputName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long AcceptedSequenceBoundary { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Written { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Failed { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Abandoned { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Pending { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern LogWriterFlushStatus WriterFlushStatus { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool WriterFlushSucceeded { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern LogOutputRejectionSnapshot Rejections { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class LogOutputRejectionSnapshot
{
    private LogOutputRejectionSnapshot() { }
    public extern long Filtered { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long QueueFull { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Unavailable { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class CoreLogDiagnostics
{
    private CoreLogDiagnostics() { }
    public extern CoreLogState State { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool AdmissionClosed { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset ObservedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset? ShutdownStartedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long InvalidEventCount { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long TruncatedEventCount { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern IReadOnlyList<LogOutputDiagnostics> Outputs { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern LogStopResult? LastStopResult { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class LogOutputDiagnostics
{
    private LogOutputDiagnostics() { }
    public extern string OutputName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern LogOutputState State { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long AcceptedSequence { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Filtered { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long AdmissionAttempts { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Accepted { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long QueueFull { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Unavailable { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Written { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Failed { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Abandoned { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern long Pending { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern string? LastFaultCode { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern DateTimeOffset? LastFaultAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public sealed class CoreLogInitializationException : Exception
{
    private CoreLogInitializationException() { }
    public extern string OutputName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern string ErrorCode { [MethodImpl(MethodImplOptions.InternalCall)] get; }
}

public static class LogErrorCodes
{
    public const string InvalidEventName = "InvalidEventName";
    public const string InvalidMessage = "InvalidMessage";
    public const string InvalidOperationId = "InvalidOperationId";
    public const string InvalidAttributeKey = "InvalidAttributeKey";
    public const string UnsupportedValue = "UnsupportedValue";
    public const string InvalidUnicode = "InvalidUnicode";
    public const string InvalidFactoryResult = "InvalidFactoryResult";
    public const string EnvelopeTooLarge = "EnvelopeTooLarge";
    public const string OutputInitializationFailed = "OutputInitializationFailed";
    public const string OutputOwnershipUnavailable = "OutputOwnershipUnavailable";
    public const string WriteFailed = "WriteFailed";
    public const string RenderFailed = "RenderFailed";
    public const string FlushFailed = "FlushFailed";
}

public sealed class CoreLogRuntime : IAsyncDisposable
{
    private CoreLogRuntime() { }
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern CoreLogRuntime Create(EnvironmentSnapshot environment);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern CoreLogger CreateLogger(string component);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern IDisposable BeginScope(OperationContextData context);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern Task<LogFlushResult> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern Task<LogStopResult> StopAsync(CancellationToken cancellationToken);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern CoreLogDiagnostics GetDiagnostics();
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern ValueTask DisposeAsync();
}

public sealed class CoreLogger
{
    private CoreLogger() { }
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern bool IsEnabled(CoreLogLevel level);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern LogWriteResult Write(CoreLogLevel level, LogEventData entry);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern LogWriteResult WriteLazy(CoreLogLevel level, Func<LogEventData> factory);
}
