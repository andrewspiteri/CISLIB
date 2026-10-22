using System.Globalization;
using System.Text.Json;
using CIS.Healthcheck.Reporting;

namespace CIS.Healthcheck;

/// <summary>Formats one detached snapshot into a complete bounded UTF-8 report.</summary>
public static class HealthReportFormatter
{
    /// <summary>Formats the selected HTTP representation; details include every monitor.</summary>
    /// <remarks>Transport authorization belongs to the adapter. Options are validated without reading environment overrides.</remarks>
    public static HealthReportBuffer FormatSnapshot(HealthSnapshot snapshot, HealthReportView view, HealthcheckOptions options)
        => Format(snapshot, view, options, pipe: false);

    /// <summary>Formats a pipe JSON envelope, excluding its four-byte framing prefix.</summary>
    /// <remarks>The entire envelope counts against MaxReportBytes. Framing is applied only after successful complete serialization.</remarks>
    public static HealthReportBuffer FormatPipeResponse(HealthSnapshot snapshot, HealthReportView view, HealthcheckOptions options)
        => Format(snapshot, view, options, pipe: true);

    private static HealthReportBuffer Format(HealthSnapshot snapshot, HealthReportView view, HealthcheckOptions options, bool pipe)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(view)) { throw new ArgumentOutOfRangeException(nameof(view)); }
        int limit = options.ValidatedCopy().MaxReportBytes;
        try
        {
            // Bound decimal conversion itself before asking an arbitrary-precision
            // revision to allocate text. Four bits per budget byte is conservative.
            if (snapshot.Revision.GetBitLength() > (long)limit * 4) { throw new HealthReportTooLargeException(); }
            var buffer = new BoundedHealthBuffer(limit);
            using var writer = new Utf8JsonWriter(buffer);
            if (pipe)
            {
                writer.WriteStartObject();
                writer.WriteNumber("protocolVersion", 1);
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteString("view", HealthReportProtocol.ViewName(view));
                writer.WriteString("kind", "snapshot");
                writer.WritePropertyName("snapshot");
            }
            WriteSnapshot(writer, snapshot, view);
            if (pipe) { writer.WriteEndObject(); }
            writer.Flush();
            return new HealthReportBuffer(buffer.WrittenSpan, false, null,
                view == HealthReportView.Readiness ? snapshot.Ready : snapshot.HealthStatus == HealthStatus.Healthy);
        }
        catch (HealthReportTooLargeException)
        {
            return FormatError("ReportTooLarge", pipe, view);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or FormatException)
        {
            return FormatError("SerializationFailed", pipe, view);
        }
    }

    internal static HealthReportBuffer FormatError(string code, bool pipe, HealthReportView? view = null)
    {
        // Stable adapter-selected codes only; caller exception messages never enter a report.
        if (!HealthReportProtocol.IsErrorCode(code, pipe)) { throw new ArgumentOutOfRangeException(nameof(code)); }
        var buffer = new BoundedHealthBuffer(1024);
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        if (pipe) { writer.WriteNumber("protocolVersion", 1); }
        writer.WriteNumber("schemaVersion", 1);
        if (pipe)
        {
            if (view.HasValue) { writer.WriteString("view", HealthReportProtocol.ViewName(view.Value)); }
            else { writer.WriteNull("view"); }
        }
        writer.WriteString("kind", "error");
        writer.WriteStartObject("error");
        writer.WriteString("code", code);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return new HealthReportBuffer(buffer.WrittenSpan, true, code, false);
    }

    private static void WriteSnapshot(Utf8JsonWriter writer, HealthSnapshot snapshot, HealthReportView view)
    {
        if (snapshot.SchemaVersion != 1 || snapshot.Revision.Sign < 0) { throw new InvalidOperationException("Unsupported snapshot version or revision."); }
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", snapshot.SchemaVersion);
        writer.WriteString("revision", snapshot.Revision.ToString(CultureInfo.InvariantCulture));
        WriteTime(writer, "capturedAtUtc", snapshot.CapturedAtUtc);
        writer.WriteStartObject("application");
        var app = snapshot.Application;
        writer.WriteString("name", app.Name);
        WriteText(writer, "buildVersion", app.BuildVersion);
        WriteText(writer, "serviceVersion", app.ServiceVersion);
        writer.WriteString("instanceId", app.InstanceId.ToString("D"));
        WriteText(writer, "hostName", app.HostName);
        WriteText(writer, "osDescription", app.OsDescription);
        writer.WriteBoolean("startupCompleted", app.StartupCompleted);
        writer.WriteBoolean("isStopping", app.IsStopping);
        writer.WriteEndObject();
        WriteEnum(writer, "healthStatus", snapshot.HealthStatus);
        writer.WriteBoolean("ready", snapshot.Ready);
        writer.WriteStartObject("monitorCounts");
        var counts = snapshot.MonitorCounts;
        writer.WriteNumber("active", counts.Active);
        writer.WriteNumber("critical", counts.Critical);
        writer.WriteNumber("failing", counts.Failing);
        writer.WriteNumber("criticalFailing", counts.CriticalFailing);
        writer.WriteNumber("pending", counts.Pending);
        writer.WriteEndObject();
        if (view == HealthReportView.Details)
        {
            writer.WriteStartArray("monitors");
            foreach (var monitor in snapshot.Monitors) { WriteMonitor(writer, monitor); }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static void WriteMonitor(Utf8JsonWriter writer, HealthMonitorSnapshot monitor)
    {
        writer.WriteStartObject();
        writer.WriteString("monitorId", monitor.MonitorId.ToString("D"));
        WriteText(writer, "name", monitor.Name);
        WriteEnum(writer, "policy", monitor.Policy);
        writer.WriteBoolean("isCritical", monitor.IsCritical);
        WriteEnum(writer, "state", monitor.State);
        WriteTime(writer, "registeredAtUtc", monitor.RegisteredAtUtc);
        WriteTime(writer, "lastObservationAtUtc", monitor.LastObservationAtUtc);
        WriteDiagnostic(writer, "diagnostic", monitor.Diagnostic);
        WriteDiagnostic(writer, "registrationDiagnostic", monitor.RegistrationDiagnostic);
        if (monitor.Outcomes is { } outcomes)
        {
            writer.WriteStartObject("outcomes");
            WriteCounter(writer, "successful", outcomes.Successful);
            WriteCounter(writer, "failed", outcomes.Failed);
            WriteCounter(writer, "skipped", outcomes.Skipped);
            WriteCounter(writer, "expectedCanceled", outcomes.ExpectedCanceled);
            WriteCounter(writer, "consecutiveFailures", outcomes.ConsecutiveFailures);
            writer.WriteBoolean("countersSaturated", outcomes.CountersSaturated);
            if (outcomes.LastOutcome.HasValue) { WriteEnum(writer, "lastOutcome", outcomes.LastOutcome.Value); }
            WriteTime(writer, "lastSuccessAtUtc", outcomes.LastSuccessAtUtc);
            WriteTime(writer, "lastFailureAtUtc", outcomes.LastFailureAtUtc);
            WriteDiagnostic(writer, "lastFailureDiagnostic", outcomes.LastFailureDiagnostic);
            if (outcomes.Execution is { } execution) { WriteExecution(writer, execution); }
            writer.WriteEndObject();
        }
        if (monitor.Collection is { } collection)
        {
            writer.WriteStartObject("collection");
            writer.WriteBoolean("inProgress", collection.InProgress);
            WriteTime(writer, "lastCompletedAtUtc", collection.LastCompletedAtUtc);
            WriteDiagnostic(writer, "lastError", collection.LastError);
            WriteTime(writer, "lastErrorAtUtc", collection.LastErrorAtUtc);
            writer.WriteEndObject();
        }
        if (monitor.Numeric is { } numeric)
        {
            writer.WriteStartObject("numeric");
            WriteEnum(writer, "comparison", numeric.Comparison);
            writer.WriteNumber("threshold", numeric.Threshold);
            writer.WriteString("unit", numeric.Unit);
            if (numeric.LastValue.HasValue) { writer.WriteNumber("lastValue", numeric.LastValue.Value); }
            WriteTime(writer, "lastValueAtUtc", numeric.LastValueAtUtc);
            writer.WriteEndObject();
        }
        writer.WriteStartArray("measurements");
        foreach (var measurement in monitor.Measurements)
        {
            writer.WriteStartObject();
            writer.WriteString("key", measurement.Key);
            WriteEnum(writer, "valueKind", measurement.ValueKind);
            switch (measurement.ValueKind)
            {
                case HealthMeasurementValueKind.Unavailable: break;
                case HealthMeasurementValueKind.String: writer.WriteString("value", measurement.StringValue); break;
                case HealthMeasurementValueKind.Boolean: writer.WriteBoolean("value", measurement.BooleanValue!.Value); break;
                case HealthMeasurementValueKind.Integer: writer.WriteString("value", measurement.IntegerValue!.Value.ToString(CultureInfo.InvariantCulture)); break;
                case HealthMeasurementValueKind.Number: writer.WriteNumber("value", measurement.NumberValue!.Value); break;
                default: throw new InvalidOperationException("Unsupported measurement kind.");
            }
            if (!string.IsNullOrWhiteSpace(measurement.Unit)) { writer.WriteString("unit", measurement.Unit); }
            WriteTime(writer, "observedAtUtc", measurement.ObservedAtUtc);
            writer.WriteBoolean("isDiagnostic", measurement.IsDiagnostic);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteExecution(Utf8JsonWriter writer, HealthExecutionSnapshot execution)
    {
        writer.WriteStartObject("execution");
        writer.WriteString("registrationId", execution.RegistrationId.ToString("D"));
        WriteCounter(writer, "executionId", execution.ExecutionId);
        WriteEnum(writer, "phase", execution.Phase);
        WriteTime(writer, "eligibleAtUtc", execution.EligibleAtUtc);
        WriteTime(writer, "admittedAtUtc", execution.AdmittedAtUtc);
        WriteTime(writer, "conditionStartedAtUtc", execution.ConditionStartedAtUtc);
        WriteTime(writer, "callbackStartedAtUtc", execution.CallbackStartedAtUtc);
        WriteTime(writer, "workCompletedAtUtc", execution.WorkCompletedAtUtc);
        WriteTime(writer, "errorHandlingStartedAtUtc", execution.ErrorHandlingStartedAtUtc);
        WriteTime(writer, "errorHandlingCompletedAtUtc", execution.ErrorHandlingCompletedAtUtc);
        WriteTime(writer, "cleanupCompletedAtUtc", execution.CleanupCompletedAtUtc);
        WriteTime(writer, "settledAtUtc", execution.SettledAtUtc);
        WriteDuration(writer, "queueDelayMs", execution.QueueDelayMilliseconds);
        WriteDuration(writer, "conditionDurationMs", execution.ConditionDurationMilliseconds);
        WriteDuration(writer, "callbackDurationMs", execution.CallbackDurationMilliseconds);
        WriteDuration(writer, "errorHandlingDurationMs", execution.ErrorHandlingDurationMilliseconds);
        WriteDuration(writer, "cleanupDurationMs", execution.CleanupDurationMilliseconds);
        WriteDuration(writer, "totalDurationMs", execution.TotalDurationMilliseconds);
        WriteEnum(writer, "errorHandlingStatus", execution.ErrorHandlingStatus);
        WriteEnum(writer, "cleanupStatus", execution.CleanupStatus);
        WriteDiagnostic(writer, "originalError", execution.OriginalError);
        WriteDiagnostic(writer, "errorHandlingError", execution.ErrorHandlingError);
        WriteDiagnostic(writer, "cleanupError", execution.CleanupError);
        WriteDiagnostic(writer, "integrationError", execution.IntegrationError);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostic(Utf8JsonWriter writer, string name, HealthDiagnostic? diagnostic)
    {
        if (diagnostic is null) { return; }
        writer.WriteStartObject(name);
        WriteText(writer, "code", diagnostic.Code);
        WriteText(writer, "message", diagnostic.Message);
        WriteText(writer, "exceptionType", diagnostic.ExceptionType);
        writer.WriteBoolean("isTruncated", diagnostic.IsTruncated);
        writer.WriteEndObject();
    }

    private static void WriteCounter(Utf8JsonWriter writer, string name, long value)
    {
        if (value < 0) { throw new InvalidOperationException("Negative counter."); }
        writer.WriteString(name, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteDuration(Utf8JsonWriter writer, string name, double? value)
    {
        if (value.HasValue)
        {
            if (!double.IsFinite(value.Value) || value.Value < 0) { throw new InvalidOperationException("Invalid measured duration."); }
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null) { writer.WriteString(name, value); }
    }

    private static void WriteTime(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value.HasValue) { writer.WriteString(name, value.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)); }
    }

    private static void WriteEnum<T>(Utf8JsonWriter writer, string name, T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) { throw new InvalidOperationException("Unsupported enum value."); }
        writer.WriteString(name, value.ToString());
    }
}
