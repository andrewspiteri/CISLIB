using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Text;

namespace CIS.Healthcheck.Reporting;

/// <summary>Internal shared protocol boundary for the local server and probe.</summary>
internal static class HealthReportProtocol
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string ViewName(HealthReportView view) => view switch
    {
        HealthReportView.Health => "health",
        HealthReportView.Readiness => "readiness",
        HealthReportView.Details => "details",
        _ => throw new ArgumentOutOfRangeException(nameof(view))
    };

    internal static bool IsErrorCode(string code, bool pipe) => code is
        "ReportTooLarge" or "SerializationFailed" or "ReportingUnavailable" ||
        (pipe ? code is "DetailsDisabled" or "InvalidView" or "UnsupportedVersion" or "InvalidRequest" or "InvalidFrame"
              : code is "Forbidden" or "NotFound" or "MethodNotAllowed");

    internal static bool TryParseRequest(ReadOnlyMemory<byte> payload, out HealthReportView view, out string? errorCode)
    {
        view = default;
        errorCode = null;
        if (payload.Length is < 1 or > 512) { errorCode = "InvalidFrame"; return false; }
        try
        {
            using var document = ParseStrict(payload);
            var root = document.RootElement;
            RequireObject(root);
            int version = Integer(Required(root, "protocolVersion"), int.MinValue, int.MaxValue);
            if (version != 1) { errorCode = "UnsupportedVersion"; return false; }
            string selected = Text(Required(root, "view"), 512);
            switch (selected)
            {
                case "health": view = HealthReportView.Health; break;
                case "readiness": view = HealthReportView.Readiness; break;
                case "details": view = HealthReportView.Details; break;
                default: errorCode = "InvalidView"; return false;
            }
            return true;
        }
        catch (DecoderFallbackException) { errorCode = "InvalidFrame"; }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            errorCode = "InvalidRequest";
        }
        return false;
    }

    internal static HealthParsedResponse ParseResponse(ReadOnlyMemory<byte> payload, HealthReportView expectedView, int maxReportBytes)
    {
        ValidateMaximum(maxReportBytes);
        if (!Enum.IsDefined(expectedView)) { throw new ArgumentOutOfRangeException(nameof(expectedView)); }
        if (payload.Length == 0 || payload.Length > maxReportBytes) { throw Invalid(); }
        try
        {
            using var document = ParseStrict(payload);
            var root = document.RootElement;
            RequireObject(root);
            if (Integer(Required(root, "protocolVersion"), 1, 1) != 1 || Integer(Required(root, "schemaVersion"), 1, 1) != 1) { throw Invalid(); }
            var view = Required(root, "view");
            string kind = Text(Required(root, "kind"), 16);
            if (kind == "error")
            {
                if (root.TryGetProperty("snapshot", out _)) { throw Invalid(); }
                var error = Required(root, "error");
                RequireObject(error);
                string code = Text(Required(error, "code"), 64);
                if (!IsErrorCode(code, pipe: true)) { throw Invalid(); }
                OptionalText(error, "message", 128);
                if (view.ValueKind == JsonValueKind.Null)
                {
                    if (code is not ("InvalidRequest" or "InvalidFrame" or "InvalidView" or "UnsupportedVersion")) { throw Invalid(); }
                }
                else if (Text(view, 32) != ViewName(expectedView)) { throw Invalid(); }
                return new HealthParsedResponse([], true, code, false);
            }
            if (kind != "snapshot" || root.TryGetProperty("error", out _) || Text(view, 32) != ViewName(expectedView)) { throw Invalid(); }
            var snapshot = Required(root, "snapshot");
            bool selectedHealthy = ValidateSnapshot(snapshot, expectedView);
            return new HealthParsedResponse(StrictUtf8.GetBytes(snapshot.GetRawText()), false, null, selectedHealthy);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw Invalid();
        }
    }

    internal static byte[] EncodeFrame(ReadOnlySpan<byte> payload, int maximumPayloadBytes)
    {
        ValidateLength(payload.Length, maximumPayloadBytes);
        byte[] framed = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(framed, (uint)payload.Length);
        payload.CopyTo(framed.AsSpan(4));
        return framed;
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, int maximumPayloadBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateMaximum(maximumPayloadBytes);
        byte[] prefix = new byte[4];
        try
        {
            await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            if (length == 0 || length > maximumPayloadBytes) { throw Invalid(); }
            byte[] payload = new byte[(int)length];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return payload;
        }
        catch (EndOfStreamException) { throw Invalid(); }
    }

    internal static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, int maximumPayloadBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateLength(payload.Length, maximumPayloadBytes);
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private static JsonDocument ParseStrict(ReadOnlyMemory<byte> payload)
    {
        // Validate bytes even in unknown additive fields; JsonDocument's lazy string
        // decoding alone is not a strict UTF-8 or escaped-surrogate boundary.
        StrictUtf8.GetCharCount(payload.Span);
        var reader = new Utf8JsonReader(payload.Span, new JsonReaderOptions { MaxDepth = 16 });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: objects.Push(new HashSet<string>(StringComparer.Ordinal)); break;
                case JsonTokenType.EndObject: objects.Pop(); break;
                case JsonTokenType.PropertyName:
                    string name = reader.GetString()!;
                    if (!UnicodeScalarText.IsValid(name, int.MaxValue, allowControls: true) || !objects.Peek().Add(name)) { throw Invalid(); }
                    break;
                case JsonTokenType.String:
                    if (!UnicodeScalarText.IsValid(reader.GetString()!, int.MaxValue, allowControls: true)) { throw Invalid(); }
                    break;
            }
        }
        return JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
    }

    private static bool ValidateSnapshot(JsonElement snapshot, HealthReportView view)
    {
        RequireObject(snapshot);
        if (snapshot.TryGetProperty("kind", out _)) { throw Invalid(); }
        Integer(Required(snapshot, "schemaVersion"), 1, 1);
        CanonicalDecimal(Required(snapshot, "revision"), signed: false, int64: false);
        Timestamp(Required(snapshot, "capturedAtUtc"));
        var app = Required(snapshot, "application");
        RequireObject(app);
        Text(Required(app, "name"), 255, nonblank: true);
        GuidValue(Required(app, "instanceId"));
        OptionalText(app, "buildVersion", 512);
        OptionalText(app, "serviceVersion", 512);
        OptionalText(app, "hostName", 255);
        OptionalText(app, "osDescription", 1024);
        bool startup = Boolean(Required(app, "startupCompleted"));
        bool stopping = Boolean(Required(app, "isStopping"));
        string health = EnumText<HealthStatus>(Required(snapshot, "healthStatus"));
        bool ready = Boolean(Required(snapshot, "ready"));
        if (ready != (startup && !stopping && health == "Healthy")) { throw Invalid(); }
        var counts = Required(snapshot, "monitorCounts");
        RequireObject(counts);
        int active = Count("active"), critical = Count("critical"), failing = Count("failing"), criticalFailing = Count("criticalFailing"), pending = Count("pending");
        if (critical > active || failing > active || pending > active - failing || criticalFailing > critical || criticalFailing > failing ||
            failing - criticalFailing > active - critical || (health == "Healthy") != (criticalFailing == 0)) { throw Invalid(); }
        if (snapshot.TryGetProperty("monitors", out var monitors))
        {
            RequireArray(monitors, 65536);
            if (monitors.GetArrayLength() != active) { throw Invalid(); }
            int actualCritical = 0, actualFailing = 0, actualCriticalFailing = 0, actualPending = 0;
            var ids = new HashSet<Guid>();
            foreach (var monitor in monitors.EnumerateArray())
            {
                ValidateMonitor(monitor);
                if (!ids.Add(GuidValue(Required(monitor, "monitorId")))) { throw Invalid(); }
                bool isCritical = Boolean(Required(monitor, "isCritical"));
                string state = EnumText<MonitorState>(Required(monitor, "state"));
                if (isCritical) { actualCritical++; }
                if (state == "Unhealthy") { actualFailing++; if (isCritical) { actualCriticalFailing++; } }
                if (state == "Pending") { actualPending++; }
            }
            if (actualCritical != critical || actualFailing != failing || actualCriticalFailing != criticalFailing || actualPending != pending) { throw Invalid(); }
        }
        else if (view == HealthReportView.Details) { throw Invalid(); }
        return view == HealthReportView.Readiness ? ready : health == "Healthy";

        int Count(string name) => Integer(Required(counts, name), 0, 65536);
    }

    private static void ValidateMonitor(JsonElement monitor)
    {
        RequireObject(monitor);
        GuidValue(Required(monitor, "monitorId"));
        OptionalText(monitor, "name", 128);
        string policy = EnumText<HealthPolicyKind>(Required(monitor, "policy"));
        Boolean(Required(monitor, "isCritical"));
        string state = EnumText<MonitorState>(Required(monitor, "state"));
        if (policy == "Condition" && state == "Pending") { throw Invalid(); }
        Timestamp(Required(monitor, "registeredAtUtc"));
        OptionalTimestamp(monitor, "lastObservationAtUtc");
        OptionalDiagnostic(monitor, "diagnostic");
        OptionalDiagnostic(monitor, "registrationDiagnostic");
        bool hasNumeric = monitor.TryGetProperty("numeric", out var numeric);
        if (hasNumeric != (policy == "NumericThreshold")) { throw Invalid(); }
        if (hasNumeric)
        {
            RequireObject(numeric);
            EnumText<NumericComparison>(Required(numeric, "comparison"));
            Number(Required(numeric, "threshold"));
            Text(Required(numeric, "unit"), 32, nonblank: true);
            bool hasValue = numeric.TryGetProperty("lastValue", out var value);
            if (hasValue != numeric.TryGetProperty("lastValueAtUtc", out var at)) { throw Invalid(); }
            if (hasValue && state == "Pending" || !hasValue && state == "Healthy") { throw Invalid(); }
            if (hasValue) { Number(value); Timestamp(at); }
        }
        bool hasOutcomes = monitor.TryGetProperty("outcomes", out var outcomes);
        if (policy == "Condition" && hasOutcomes || policy is "ConsecutiveFailures" or "FirstSuccessAvailability" && !hasOutcomes) { throw Invalid(); }
        if (hasOutcomes) { ValidateOutcomes(outcomes, policy, state); }
        if (monitor.TryGetProperty("collection", out var collection))
        {
            RequireObject(collection);
            Boolean(Required(collection, "inProgress"));
            OptionalTimestamp(collection, "lastCompletedAtUtc");
            OptionalDiagnostic(collection, "lastError");
            OptionalTimestamp(collection, "lastErrorAtUtc");
            if (collection.TryGetProperty("lastError", out _) != collection.TryGetProperty("lastErrorAtUtc", out _)) { throw Invalid(); }
        }
        var measurements = Required(monitor, "measurements");
        RequireArray(measurements, 32);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? previousKey = null;
        foreach (var measurement in measurements.EnumerateArray())
        {
            RequireObject(measurement);
            string key = Text(Required(measurement, "key"), 128, nonblank: true);
            if (!keys.Add(key) || previousKey is not null && StringComparer.Ordinal.Compare(previousKey, key) >= 0) { throw Invalid(); }
            previousKey = key;
            string kind = EnumText<HealthMeasurementValueKind>(Required(measurement, "valueKind"));
            if (measurement.TryGetProperty("unit", out var unit)) { Text(unit, 32, nonblank: true); }
            Timestamp(Required(measurement, "observedAtUtc"));
            if (!Boolean(Required(measurement, "isDiagnostic"))) { throw Invalid(); }
            bool hasValue = measurement.TryGetProperty("value", out var scalar);
            if (kind == "Unavailable") { if (hasValue) { throw Invalid(); } continue; }
            if (!hasValue) { throw Invalid(); }
            switch (kind)
            {
                case "String": Text(scalar, 1024); break;
                case "Boolean": Boolean(scalar); break;
                case "Integer": CanonicalDecimal(scalar, signed: true, int64: true); break;
                case "Number": Number(scalar); break;
            }
        }
    }

    private static void ValidateOutcomes(JsonElement outcomes, string policy, string state)
    {
        RequireObject(outcomes);
        long successful = Counter("successful"), failed = Counter("failed"), skipped = Counter("skipped"), canceled = Counter("expectedCanceled"), streak = Counter("consecutiveFailures");
        bool saturated = Boolean(Required(outcomes, "countersSaturated"));
        if (streak > failed || saturated != (successful == long.MaxValue || failed == long.MaxValue || skipped == long.MaxValue || canceled == long.MaxValue)) { throw Invalid(); }
        // These are definite contradictions in recorded facts, not another policy
        // evaluator. The wire deliberately carries no consecutive-failure threshold.
        if (policy is "ConsecutiveFailures" or "FirstSuccessAvailability" && state == "Pending" && (successful != 0 || failed != 0) ||
            policy == "ConsecutiveFailures" && state == "Unhealthy" && streak == 0 ||
            policy == "FirstSuccessAvailability" && state == "Healthy" && successful == 0 ||
            policy == "ConsecutiveFailures" && state == "Healthy" && successful == 0 && failed == 0) { throw Invalid(); }
        if (outcomes.TryGetProperty("lastOutcome", out var last))
        {
            string outcome = EnumText<HealthExecutionOutcome>(last);
            if (outcome switch
            {
                "Succeeded" => successful == 0 || streak != 0,
                "Failed" => failed == 0 || streak == 0,
                "Skipped" => skipped == 0,
                "ExpectedCancellation" => canceled == 0,
                _ => true
            }) { throw Invalid(); }
        }
        OptionalTimestamp(outcomes, "lastSuccessAtUtc");
        OptionalTimestamp(outcomes, "lastFailureAtUtc");
        OptionalDiagnostic(outcomes, "lastFailureDiagnostic");
        if (!outcomes.TryGetProperty("execution", out var execution)) { return; }
        RequireObject(execution);
        GuidValue(Required(execution, "registrationId"));
        if (CanonicalDecimal(Required(execution, "executionId"), signed: false, int64: true) == "0") { throw Invalid(); }
        string phase = EnumText<HealthExecutionPhase>(Required(execution, "phase"));
        foreach (string name in new[] { "eligibleAtUtc", "admittedAtUtc", "conditionStartedAtUtc", "callbackStartedAtUtc", "workCompletedAtUtc",
                     "errorHandlingStartedAtUtc", "errorHandlingCompletedAtUtc", "cleanupCompletedAtUtc", "settledAtUtc" }) { OptionalTimestamp(execution, name); }
        foreach (string name in new[] { "queueDelayMs", "conditionDurationMs", "callbackDurationMs", "errorHandlingDurationMs", "cleanupDurationMs", "totalDurationMs" })
        {
            if (execution.TryGetProperty(name, out var value) && Number(value) < 0) { throw Invalid(); }
        }
        string handler = EnumText<HealthExecutionStepStatus>(Required(execution, "errorHandlingStatus"));
        string cleanup = EnumText<HealthExecutionStepStatus>(Required(execution, "cleanupStatus"));
        foreach (string name in new[] { "originalError", "errorHandlingError", "cleanupError", "integrationError" }) { OptionalDiagnostic(execution, name); }
        bool settled = execution.TryGetProperty("settledAtUtc", out _);
        if (phase == "Settled" && !settled || settled && (phase is not ("Settled" or "Retired") || handler is "Running" or "NotStarted" || cleanup is "Running" or "NotStarted") ||
            !settled && execution.TryGetProperty("totalDurationMs", out _)) { throw Invalid(); }
        bool handlerStarted = Has("errorHandlingStartedAtUtc"), handlerCompleted = Has("errorHandlingCompletedAtUtc"), handlerDuration = Has("errorHandlingDurationMs");
        if (handler == "NotStarted" && (handlerStarted || handlerCompleted || handlerDuration) || handler == "Running" && handlerCompleted ||
            handler is "NotApplicable" or "NotInvoked" && (handlerStarted || handlerDuration) ||
            Has("errorHandlingError") && handler is "NotStarted" or "Running" or "Succeeded" or "NotApplicable") { throw Invalid(); }
        if (cleanup is "NotStarted" or "Running" && Has("cleanupCompletedAtUtc") ||
            cleanup is "NotApplicable" or "NotInvoked" && Has("cleanupDurationMs") ||
            Has("cleanupError") && cleanup is "NotStarted" or "Running" or "Succeeded" or "NotApplicable") { throw Invalid(); }

        long Counter(string name) => long.Parse(CanonicalDecimal(Required(outcomes, name), signed: false, int64: true), CultureInfo.InvariantCulture);
        bool Has(string name) => execution.TryGetProperty(name, out _);
    }

    private static JsonElement Required(JsonElement value, string name)
    {
        RequireObject(value);
        return value.TryGetProperty(name, out var property) ? property : throw Invalid();
    }

    private static string Text(JsonElement value, int maximum, bool nonblank = false)
    {
        if (value.ValueKind != JsonValueKind.String) { throw Invalid(); }
        string text = value.GetString()!;
        if (!UnicodeScalarText.IsValid(text, maximum, allowControls: true) || nonblank && string.IsNullOrWhiteSpace(text)) { throw Invalid(); }
        return text;
    }

    private static string EnumText<T>(JsonElement value) where T : struct, Enum
    {
        string text = Text(value, 64);
        // Enum.TryParse also accepts numeric strings, which are forbidden on the wire.
        return Enum.TryParse<T>(text, out var parsed) && Enum.IsDefined(parsed) && parsed.ToString() == text ? text : throw Invalid();
    }

    private static void OptionalText(JsonElement value, string name, int maximum)
    {
        if (value.TryGetProperty(name, out var property)) { Text(property, maximum); }
    }

    private static int Integer(JsonElement value, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < minimum || number > maximum) { throw Invalid(); }
        return number;
    }

    private static bool Boolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid()
    };

    private static double Number(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number)) { throw Invalid(); }
        return number;
    }

    private static string CanonicalDecimal(JsonElement value, bool signed, bool int64)
    {
        string text = Text(value, int.MaxValue);
        int start = signed && text.StartsWith('-') ? 1 : 0;
        if (text.Length == start || text[start] == '0' && (text.Length - start != 1 || start == 1)) { throw Invalid(); }
        for (int i = start; i < text.Length; i++) { if (text[i] is < '0' or > '9') { throw Invalid(); } }
        if (int64 && (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) || !signed && number < 0)) { throw Invalid(); }
        return text;
    }

    private static Guid GuidValue(JsonElement value)
    {
        string text = Text(value, 36);
        if (!Guid.TryParseExact(text, "D", out var guid) || guid == Guid.Empty || guid.ToString("D") != text) { throw Invalid(); }
        return guid;
    }

    private static void Timestamp(JsonElement value)
    {
        string text = Text(value, 28);
        if (text.Length is < 20 or > 28 || text[^1] != 'Z' ||
            text.Length != 20 && (text.Length < 22 || text[19] != '.') ||
            !DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _)) { throw Invalid(); }
    }

    private static void OptionalTimestamp(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out var property)) { Timestamp(property); }
    }

    private static void OptionalDiagnostic(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var diagnostic)) { return; }
        RequireObject(diagnostic);
        OptionalText(diagnostic, "code", 64);
        OptionalText(diagnostic, "message", 1024);
        OptionalText(diagnostic, "exceptionType", 256);
        Boolean(Required(diagnostic, "isTruncated"));
    }

    private static void RequireObject(JsonElement value) { if (value.ValueKind != JsonValueKind.Object) { throw Invalid(); } }
    private static void RequireArray(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) { throw Invalid(); }
    }

    private static void ValidateMaximum(int maximum)
    {
        if (maximum is < 1 or > 16777216) { throw new ArgumentOutOfRangeException(nameof(maximum)); }
    }

    private static void ValidateLength(int length, int maximum)
    {
        ValidateMaximum(maximum);
        if (length == 0 || length > maximum) { throw Invalid(); }
    }

    private static FormatException Invalid() => new("Invalid Healthcheck protocol payload.");
}

internal sealed class HealthParsedResponse(ReadOnlySpan<byte> snapshotUtf8, bool isError, string? errorCode, bool selectedHealthy)
{
    internal ReadOnlyMemory<byte> SnapshotUtf8 { get; } = snapshotUtf8.ToArray();
    internal bool IsError { get; } = isError;
    internal string? ErrorCode { get; } = errorCode;
    internal bool SelectedHealthy { get; } = !isError && selectedHealthy;
}
