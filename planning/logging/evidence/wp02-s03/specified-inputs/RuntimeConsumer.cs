#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CIS;
using CIS.Logging;

internal static class RuntimeConsumer
{
    public static async Task Use(EnvironmentSnapshot environment, CancellationToken cancellationToken)
    {
        await using CoreLogRuntime runtime = CoreLogRuntime.Create(environment: environment);
        CoreLogger logger = runtime.CreateLogger(component: "Orders");
        bool enabled = logger.IsEnabled(level: CoreLogLevel.Information);
        using IDisposable scope = runtime.BeginScope(context: new OperationContextData { OperationId = "op-1" });
        LogWriteResult direct = logger.Write(level: CoreLogLevel.Information, data: new LogEventData { EventName = "order.created", Message = "Order created" });
        LogWriteResult deferred = logger.WriteLazy(level: CoreLogLevel.Information, factory: () => new LogEventData { EventName = "order.created", Message = "Order created" });
        LogFlushResult flush = await runtime.FlushAsync(timeout: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken);
        LogStopResult stop = await runtime.StopAsync(cancellationToken: cancellationToken);
        CoreLogDiagnostics diagnostics = runtime.GetDiagnostics();
        IAsyncDisposable disposable = runtime;
        ValueTask disposal = disposable.DisposeAsync();
    }

    private static void ReadLogWriteResult(LogWriteResult value)
    {
        LogWriteOutcome readOutcome = value.Outcome;
        IReadOnlyList<LogOutputWriteResult> readOutputs = value.Outputs;
        string? readErrorCode = value.ErrorCode;
        bool readIsTruncated = value.IsTruncated;
    }

    private static void ReadLogOutputWriteResult(LogOutputWriteResult value)
    {
        string readOutputName = value.OutputName;
        LogOutputWriteStatus readStatus = value.Status;
        long? readAcceptedSequence = value.AcceptedSequence;
    }

    private static void ReadLogFlushResult(LogFlushResult value)
    {
        LogCompletion readCompletion = value.Completion;
        bool readSucceeded = value.Succeeded;
        bool readLossObserved = value.LossObserved;
        DateTimeOffset readBoundaryCapturedAtUtc = value.BoundaryCapturedAtUtc;
        DateTimeOffset readObservedAtUtc = value.ObservedAtUtc;
        long readInvalidEventCount = value.InvalidEventCount;
        long readTruncatedEventCount = value.TruncatedEventCount;
        IReadOnlyList<LogOutputDrainResult> readOutputs = value.Outputs;
    }

    private static void ReadLogStopResult(LogStopResult value)
    {
        LogCompletion readCompletion = value.Completion;
        bool readSucceeded = value.Succeeded;
        bool readLossObserved = value.LossObserved;
        DateTimeOffset readBoundaryCapturedAtUtc = value.BoundaryCapturedAtUtc;
        DateTimeOffset readObservedAtUtc = value.ObservedAtUtc;
        long readInvalidEventCount = value.InvalidEventCount;
        long readTruncatedEventCount = value.TruncatedEventCount;
        IReadOnlyList<LogOutputDrainResult> readOutputs = value.Outputs;
    }

    private static void ReadLogOutputDrainResult(LogOutputDrainResult value)
    {
        string readOutputName = value.OutputName;
        long readAcceptedSequenceBoundary = value.AcceptedSequenceBoundary;
        long readWritten = value.Written;
        long readFailed = value.Failed;
        long readAbandoned = value.Abandoned;
        long readPending = value.Pending;
        LogWriterFlushStatus readWriterFlushStatus = value.WriterFlushStatus;
        bool readWriterFlushSucceeded = value.WriterFlushSucceeded;
        LogOutputRejectionSnapshot readRejections = value.Rejections;
    }

    private static void ReadLogOutputRejectionSnapshot(LogOutputRejectionSnapshot value)
    {
        long readFiltered = value.Filtered;
        long readQueueFull = value.QueueFull;
        long readUnavailable = value.Unavailable;
    }

    private static void ReadCoreLogDiagnostics(CoreLogDiagnostics value)
    {
        CoreLogState readState = value.State;
        bool readAdmissionClosed = value.AdmissionClosed;
        DateTimeOffset readObservedAtUtc = value.ObservedAtUtc;
        DateTimeOffset? readShutdownStartedAtUtc = value.ShutdownStartedAtUtc;
        long readInvalidEventCount = value.InvalidEventCount;
        long readTruncatedEventCount = value.TruncatedEventCount;
        IReadOnlyList<LogOutputDiagnostics> readOutputs = value.Outputs;
        LogStopResult? readLastStopResult = value.LastStopResult;
    }

    private static void ReadLogOutputDiagnostics(LogOutputDiagnostics value)
    {
        string readOutputName = value.OutputName;
        LogOutputState readState = value.State;
        long readAcceptedSequence = value.AcceptedSequence;
        long readFiltered = value.Filtered;
        long readAdmissionAttempts = value.AdmissionAttempts;
        long readAccepted = value.Accepted;
        long readQueueFull = value.QueueFull;
        long readUnavailable = value.Unavailable;
        long readWritten = value.Written;
        long readFailed = value.Failed;
        long readAbandoned = value.Abandoned;
        long readPending = value.Pending;
        string? readLastFaultCode = value.LastFaultCode;
        DateTimeOffset? readLastFaultAtUtc = value.LastFaultAtUtc;
    }

    private static void ReadCoreLogInitializationException(CoreLogInitializationException value)
    {
        string readOutputName = value.OutputName;
        string readErrorCode = value.ErrorCode;
    }

    private static void NullCallPatterns(CoreLogger logger)
    {
        LogWriteResult direct = logger.Write(level: CoreLogLevel.None, data: null!);
        LogWriteResult deferred = logger.WriteLazy(level: CoreLogLevel.None, factory: null!);
        string error = LogErrorCodes.EnvelopeTooLarge;
        GC.KeepAlive(error);
    }
}
