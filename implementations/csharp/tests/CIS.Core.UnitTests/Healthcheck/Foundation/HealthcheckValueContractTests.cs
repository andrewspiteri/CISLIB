using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class HealthcheckValueContractTests
{
    private static readonly DateTimeOffset Observation = new(2026, 10, 20, 10, 15, 30, TimeSpan.FromHours(2));

    [TestMethod]
    public void EmptyDiagnosticDoesNotInventOptionalValues()
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Omitted optional diagnostic values must be accepted.");
        Assert.IsNotNull(diagnostic);

        Assert.IsNull(diagnostic.Code);
        Assert.IsNull(diagnostic.Message);
        Assert.IsNull(diagnostic.ExceptionType);
        Assert.IsFalse(diagnostic.IsTruncated);
    }

    [TestMethod]
    public void DiagnosticLimitsCountUnicodeScalarsAndTruncateFreeTextAtScalarBoundaries()
    {
        string code = string.Concat(Enumerable.Repeat("\U0001F600", 64));
        string exceptionType = string.Concat(Enumerable.Repeat("\U0001F600", 256));
        string message = string.Concat(Enumerable.Repeat("\U0001F600", 1_024));
        HealthDiagnostic? exact = null;
        ArgumentException? rejection = null;
        try { exact = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic text at the exact scalar bounds must be accepted.");
        Assert.IsNotNull(exact);

        Assert.AreEqual(code, exact.Code);
        Assert.AreEqual(exceptionType, exact.ExceptionType);
        Assert.AreEqual(message, exact.Message);
        Assert.IsFalse(exact.IsTruncated);

        var truncated = new HealthDiagnostic(message: message + "\U0001F600");
        Assert.AreEqual(message, truncated.Message);
        Assert.IsTrue(truncated.IsTruncated);
        Assert.ThrowsExactly<ArgumentException>(() => new HealthDiagnostic(code: code + "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new HealthDiagnostic(exceptionType: exceptionType + "x"));
    }

    [TestMethod]
    [DataRow("high")]
    [DataRow("low")]
    [DataRow("interrupted")]
    public void DirectDiagnosticTextRejectsMalformedUnicodeEvenBeyondTheTruncatedPrefix(string scenario)
    {
        string malformed = scenario switch
        {
            "high" => new string((char)0xD800, 1),
            "low" => new string((char)0xDC00, 1),
            _ => new string([(char)0xD800, 'x', (char)0xDC00])
        };
        Assert.ThrowsExactly<ArgumentException>(() => new HealthDiagnostic(code: malformed));
        Assert.ThrowsExactly<ArgumentException>(() => new HealthDiagnostic(exceptionType: malformed));
        Assert.ThrowsExactly<ArgumentException>(() => new HealthDiagnostic(message: malformed));
        Assert.ThrowsExactly<ArgumentException>(() => new HealthDiagnostic(message: new string('x', 1_024) + malformed));
    }

    [TestMethod]
    public void RuntimeExceptionProjectionReplacesUnmatchedSurrogatesAndRetainsOnlyBoundedText()
    {
        var source = new InvalidOperationException("before\uD800middle\uDC00after");
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = HealthDiagnostic.FromException(source, code: "EvaluationFailed"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid caller code and malformed runtime exception text must produce a sanitized diagnostic.");
        Assert.IsNotNull(diagnostic);

        Assert.AreEqual("EvaluationFailed", diagnostic.Code);
        Assert.AreEqual("before\uFFFDmiddle\uFFFDafter", diagnostic.Message);
        Assert.AreEqual("System.InvalidOperationException", diagnostic.ExceptionType);
        Assert.IsTrue(diagnostic.IsTruncated);

        string retained = string.Concat(Enumerable.Repeat("\U0001F600", 1_024));
        var boundedSource = new Exception(retained + "extra");
        HealthDiagnostic? bounded = null;
        ArgumentException? boundedRejection = null;
        try { bounded = HealthDiagnostic.FromException(boundedSource); }
        catch (ArgumentException error) { boundedRejection = error; }
        Assert.IsNull(boundedRejection, "Oversized valid runtime exception text must produce a scalar-bounded diagnostic.");
        Assert.IsNotNull(bounded);
        Assert.AreEqual(retained, bounded.Message);
        Assert.IsTrue(bounded.IsTruncated);
    }

    [TestMethod]
    public void ExplicitExceptionMessageUsesDirectTextValidationAndMayOverrideRuntimeMessage()
    {
        var source = new Exception("runtime\uD800message");
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = HealthDiagnostic.FromException(source, message: "Owner supplied reason."); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A valid explicit caller message must override malformed runtime exception text.");
        Assert.IsNotNull(diagnostic);

        Assert.AreEqual("Owner supplied reason.", diagnostic.Message);
        Assert.IsFalse(diagnostic.IsTruncated);
        Assert.ThrowsExactly<ArgumentException>(() => HealthDiagnostic.FromException(source, message: "bad\uD800"));
        Assert.ThrowsExactly<ArgumentException>(() => HealthDiagnostic.FromException(source, code: new string('x', 65)));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthDiagnostic.FromException(null!));
    }

    [TestMethod]
    public void RuntimeExceptionMessageGetterFailureIsOmittedAndExplicitTextAvoidsThatGetter()
    {
        var source = new ThrowingMessageException();
        HealthDiagnostic? omitted = null;
        ArgumentException? rejection = null;
        try { omitted = HealthDiagnostic.FromException(source, code: "EvaluationFailed"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid caller code must permit projection when the runtime message getter fails.");
        Assert.IsNotNull(omitted);

        Assert.AreEqual("EvaluationFailed", omitted.Code);
        Assert.IsNull(omitted.Message);
        Assert.AreEqual(typeof(ThrowingMessageException).FullName, omitted.ExceptionType);
        Assert.IsTrue(omitted.IsTruncated);
        Assert.AreEqual(1, source.MessageReadCount);

        HealthDiagnostic? supplied = null;
        ArgumentException? suppliedRejection = null;
        try { supplied = HealthDiagnostic.FromException(source, message: "Detached fallback."); }
        catch (ArgumentException error) { suppliedRejection = error; }
        Assert.IsNull(suppliedRejection, "A valid explicit message must permit projection without the runtime message getter.");
        Assert.IsNotNull(supplied);
        Assert.AreEqual("Detached fallback.", supplied.Message);
        Assert.IsFalse(supplied.IsTruncated);
        Assert.AreEqual(1, source.MessageReadCount);
    }

    [TestMethod]
    public void ProjectedDiagnosticDoesNotRetainExceptionInnerExceptionOrDataAttachments()
    {
        (HealthDiagnostic diagnostic, WeakReference[] references) = CreateDetachedDiagnostic();
        for (int attempt = 0; attempt < 5 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        Assert.IsTrue(references.All(reference => !reference.IsAlive));
        Assert.AreEqual("Outer failure.", diagnostic.Message);
        Assert.AreEqual("System.InvalidOperationException", diagnostic.ExceptionType);
        GC.KeepAlive(diagnostic);
    }

    [TestMethod]
    public void UnavailableMeasurementHasNoFabricatedScalarOrUnitAndPreservesObservationInstant()
    {
        var measurement = HealthMeasurement.Unavailable("queue", Observation);

        Assert.AreEqual("queue", measurement.Key);
        Assert.AreEqual(HealthMeasurementValueKind.Unavailable, measurement.ValueKind);
        Assert.IsNull(measurement.StringValue);
        Assert.IsNull(measurement.BooleanValue);
        Assert.IsNull(measurement.IntegerValue);
        Assert.IsNull(measurement.NumberValue);
        Assert.IsNull(measurement.Unit);
        Assert.IsTrue(measurement.IsDiagnostic);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 20, 8, 15, 30, TimeSpan.Zero), measurement.ObservedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, measurement.ObservedAtUtc.Offset);
    }

    [TestMethod]
    public void AvailableMeasurementScalarsKeepTheirActualTypedValuesAndObservationTime()
    {
        var text = HealthMeasurement.String("state", "cached", Observation, "label");
        var boolean = HealthMeasurement.Boolean("available", false, Observation);
        var integer = HealthMeasurement.Integer("items", long.MaxValue, Observation, "items");
        var number = HealthMeasurement.Number("load", 0, Observation, "%");

        Assert.AreEqual(HealthMeasurementValueKind.String, text.ValueKind);
        Assert.AreEqual("cached", text.StringValue);
        Assert.IsNull(text.BooleanValue);
        Assert.IsNull(text.IntegerValue);
        Assert.IsNull(text.NumberValue);
        Assert.AreEqual(HealthMeasurementValueKind.Boolean, boolean.ValueKind);
        Assert.AreEqual(false, boolean.BooleanValue);
        Assert.IsNull(boolean.StringValue);
        Assert.IsNull(boolean.IntegerValue);
        Assert.IsNull(boolean.NumberValue);
        Assert.AreEqual(HealthMeasurementValueKind.Integer, integer.ValueKind);
        Assert.AreEqual(long.MaxValue, integer.IntegerValue);
        Assert.IsNull(integer.StringValue);
        Assert.IsNull(integer.BooleanValue);
        Assert.IsNull(integer.NumberValue);
        Assert.AreEqual(HealthMeasurementValueKind.Number, number.ValueKind);
        Assert.AreEqual(0d, number.NumberValue);
        Assert.IsNull(number.StringValue);
        Assert.IsNull(number.BooleanValue);
        Assert.IsNull(number.IntegerValue);
        Assert.AreEqual("label", text.Unit);
        Assert.AreEqual("items", integer.Unit);
        Assert.AreEqual("%", number.Unit);
        foreach (var measurement in new[] { text, boolean, integer, number })
        {
            Assert.AreEqual(Observation.ToUniversalTime(), measurement.ObservedAtUtc);
            Assert.AreEqual(TimeSpan.Zero, measurement.ObservedAtUtc.Offset);
            Assert.IsTrue(measurement.IsDiagnostic);
        }
        Assert.AreEqual(long.MinValue, HealthMeasurement.Integer("minimum", long.MinValue, Observation).IntegerValue);
    }

    [TestMethod]
    public void MeasurementTextBoundsCountScalarsAndRejectOversizedInputs()
    {
        string key = string.Concat(Enumerable.Repeat("\U0001F600", 128));
        string unit = string.Concat(Enumerable.Repeat("\U0001F600", 32));
        string value = string.Concat(Enumerable.Repeat("\U0001F600", 1_024));
        var measurement = HealthMeasurement.String(key, value, Observation, unit);

        Assert.AreEqual(key, measurement.Key);
        Assert.AreEqual(unit, measurement.Unit);
        Assert.AreEqual(value, measurement.StringValue);
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.Unavailable(key + "x", Observation));
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.Unavailable("key", Observation, unit + "x"));
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.String("key", value + "x", Observation));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t\r\n")]
    public void MeasurementKeysMustBeNonblank(string key)
    {
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.Unavailable(key, Observation));
    }

    [TestMethod]
    public void MeasurementDirectTextRejectsNullRequiredValuesAndMalformedUnicode()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthMeasurement.Unavailable(null!, Observation));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthMeasurement.String("key", null!, Observation));
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.Unavailable("key\uD800", Observation));
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.Unavailable("key", Observation, "unit\uDC00"));
        Assert.ThrowsExactly<ArgumentException>(() => HealthMeasurement.String("key", "value\uD800", Observation));
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void NonfiniteNumbersAreRejectedBeforeTheyBecomeMeasurementsOrPolicyResults(double value)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthMeasurement.Number("load", value, Observation));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthCollectionResult.Number(value));
    }

    [TestMethod]
    public void CollectionFactoriesRepresentExactlyTheirSelectedPolicyObservation()
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic("RefreshFailed", "The refresh failed."); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A valid refresh diagnostic code and message must be accepted.");
        Assert.IsNotNull(diagnostic);
        var condition = HealthCollectionResult.Condition(true, diagnostic);
        Assert.AreEqual(HealthCollectionResultKind.Condition, condition.Kind);
        Assert.AreEqual(true, condition.IsFlagged);
        Assert.IsNull(condition.Outcome);
        Assert.IsNull(condition.Value);
        Assert.AreSame(diagnostic, condition.Diagnostic);
        Assert.IsEmpty(condition.Measurements);

        var execution = HealthCollectionResult.Execution(HealthExecutionOutcome.ExpectedCancellation);
        Assert.AreEqual(HealthCollectionResultKind.Execution, execution.Kind);
        Assert.AreEqual(HealthExecutionOutcome.ExpectedCancellation, execution.Outcome);
        Assert.IsNull(execution.IsFlagged);
        Assert.IsNull(execution.Value);

        var number = HealthCollectionResult.Number(0);
        Assert.AreEqual(HealthCollectionResultKind.Number, number.Kind);
        Assert.AreEqual(0d, number.Value);
        Assert.IsNull(number.IsFlagged);
        Assert.IsNull(number.Outcome);

        var published = HealthCollectionResult.AvailabilityPublished();
        Assert.AreEqual(HealthCollectionResultKind.AvailabilityPublished, published.Kind);
        Assert.IsNull(published.Diagnostic);
        Assert.IsNull(published.IsFlagged);
        Assert.IsNull(published.Outcome);
        Assert.IsNull(published.Value);

        var refreshFailure = HealthCollectionResult.AvailabilityRefreshFailed(diagnostic);
        Assert.AreEqual(HealthCollectionResultKind.AvailabilityRefreshFailed, refreshFailure.Kind);
        Assert.AreSame(diagnostic, refreshFailure.Diagnostic);
        Assert.IsNull(refreshFailure.IsFlagged);
        Assert.IsNull(refreshFailure.Outcome);
        Assert.IsNull(refreshFailure.Value);

        var optional = HealthCollectionResult.Diagnostics(Array.Empty<HealthMeasurement>());
        Assert.AreEqual(HealthCollectionResultKind.Diagnostics, optional.Kind);
        Assert.IsNull(optional.IsFlagged);
        Assert.IsNull(optional.Outcome);
        Assert.IsNull(optional.Value);
    }

    [TestMethod]
    public void CollectionExecutionRejectsUnsupportedEnumValues()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthCollectionResult.Execution((HealthExecutionOutcome)(-1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthCollectionResult.Execution((HealthExecutionOutcome)4));
    }

    [TestMethod]
    public void CollectionMeasurementsAreCopiedAndExposeOnlyReadOnlyOwnedStorage()
    {
        var first = HealthMeasurement.Integer("items", 7, Observation);
        var replacement = HealthMeasurement.Integer("replacement", 8, Observation);
        var supplied = new List<HealthMeasurement> { first };
        var result = HealthCollectionResult.Diagnostics(supplied);
        supplied[0] = replacement;
        supplied.Clear();

        Assert.HasCount(1, result.Measurements);
        Assert.AreSame(first, result.Measurements[0]);
        if (result.Measurements is IList<HealthMeasurement> mutableView)
        {
            Assert.IsTrue(mutableView.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(() => mutableView[0] = replacement);
            Assert.ThrowsExactly<NotSupportedException>(() => mutableView.Add(replacement));
        }
        Assert.AreSame(first, result.Measurements[0]);
        Assert.AreEqual(Observation.ToUniversalTime(), result.Measurements[0].ObservedAtUtc);
    }

    [TestMethod]
    public void CollectionAllowsExactly32OrdinalUniqueMeasurementKeys()
    {
        HealthMeasurement[] maximum = Enumerable.Range(0, 32)
            .Select(index => HealthMeasurement.Integer("item-" + index, index, Observation))
            .ToArray();
        Assert.HasCount(32, HealthCollectionResult.Diagnostics(maximum).Measurements);
        Assert.ThrowsExactly<ArgumentException>(() => HealthCollectionResult.Diagnostics(
            maximum.Append(HealthMeasurement.Integer("overflow", 33, Observation)).ToArray()));

        HealthMeasurement[] distinct =
        [
            HealthMeasurement.Integer("temperature", 1, Observation),
            HealthMeasurement.Integer("Temperature", 2, Observation),
            HealthMeasurement.Integer("\u00E9", 3, Observation),
            HealthMeasurement.Integer("e\u0301", 4, Observation)
        ];
        Assert.HasCount(4, HealthCollectionResult.Diagnostics(distinct).Measurements);
        Assert.ThrowsExactly<ArgumentException>(() => HealthCollectionResult.Diagnostics(
            [distinct[0], HealthMeasurement.Integer("temperature", 9, Observation)]));
    }

    [TestMethod]
    public void CollectionRejectsNullAndNondiagnosticMeasurementInputs()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthCollectionResult.Diagnostics(null!));
        Assert.ThrowsExactly<ArgumentException>(() => HealthCollectionResult.Diagnostics([null!]));
        var nondiagnostic = HealthMeasurement.Integer("required", 1, Observation, isDiagnostic: false);
        Assert.IsFalse(nondiagnostic.IsDiagnostic);
        Assert.ThrowsExactly<ArgumentException>(() => HealthCollectionResult.Diagnostics([nondiagnostic]));
        Assert.ThrowsExactly<ArgumentException>(() => HealthCollectionResult.Condition(false).WithMeasurements([nondiagnostic]));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthCollectionResult.Condition(false).WithMeasurements(null!));
    }

    [TestMethod]
    public void WithMeasurementsCopiesReplacementWithoutChangingPriorPolicyResult()
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic("Failed"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A valid execution diagnostic code must be accepted.");
        Assert.IsNotNull(diagnostic);
        var original = HealthCollectionResult.Execution(HealthExecutionOutcome.Failed, diagnostic);
        var first = HealthMeasurement.Integer("first", 1, Observation);
        var second = HealthMeasurement.Integer("second", 2, Observation);
        HealthMeasurement[] supplied = [first];
        var attached = original.WithMeasurements(supplied);
        var replaced = attached.WithMeasurements([second]);
        supplied[0] = second;

        Assert.IsEmpty(original.Measurements);
        Assert.HasCount(1, attached.Measurements);
        Assert.AreSame(first, attached.Measurements[0]);
        Assert.HasCount(1, replaced.Measurements);
        Assert.AreSame(second, replaced.Measurements[0]);
        Assert.AreEqual(HealthCollectionResultKind.Execution, replaced.Kind);
        Assert.AreEqual(HealthExecutionOutcome.Failed, replaced.Outcome);
        Assert.AreSame(diagnostic, replaced.Diagnostic);
        Assert.ThrowsExactly<InvalidOperationException>(() => HealthCollectionResult.Diagnostics([]).WithMeasurements([first]));
    }

    [TestMethod]
    public void ExecutionSnapshotNormalizesActualTimesWithoutInventingMissingFactsOrDurations()
    {
        Guid registrationId = Guid.NewGuid();
        HealthDiagnostic? originalError = null;
        ArgumentException? rejection = null;
        try { originalError = new HealthDiagnostic("CallbackFailed"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A valid callback diagnostic code must be accepted.");
        Assert.IsNotNull(originalError);
        var snapshot = new HealthExecutionSnapshot(new HealthExecutionSnapshotData
        {
            RegistrationId = registrationId,
            ExecutionId = 7,
            Phase = HealthExecutionPhase.Callback,
            EligibleAtUtc = Observation,
            AdmittedAtUtc = Observation.AddSeconds(1),
            CallbackStartedAtUtc = Observation.AddSeconds(2),
            WorkCompletedAtUtc = Observation.AddSeconds(3),
            CallbackDurationMilliseconds = 1_000,
            ErrorHandlingStatus = HealthExecutionStepStatus.NotStarted,
            CleanupStatus = HealthExecutionStepStatus.NotStarted,
            OriginalError = originalError
        });

        Assert.AreEqual(registrationId, snapshot.RegistrationId);
        Assert.AreEqual(7L, snapshot.ExecutionId);
        Assert.AreEqual(HealthExecutionPhase.Callback, snapshot.Phase);
        Assert.AreEqual(Observation.ToUniversalTime(), snapshot.EligibleAtUtc);
        Assert.AreEqual(Observation.AddSeconds(1).ToUniversalTime(), snapshot.AdmittedAtUtc);
        Assert.AreEqual(Observation.AddSeconds(2).ToUniversalTime(), snapshot.CallbackStartedAtUtc);
        Assert.AreEqual(Observation.AddSeconds(3).ToUniversalTime(), snapshot.WorkCompletedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, snapshot.EligibleAtUtc!.Value.Offset);
        Assert.AreEqual(TimeSpan.Zero, snapshot.AdmittedAtUtc!.Value.Offset);
        Assert.AreEqual(TimeSpan.Zero, snapshot.CallbackStartedAtUtc!.Value.Offset);
        Assert.AreEqual(TimeSpan.Zero, snapshot.WorkCompletedAtUtc!.Value.Offset);
        Assert.AreEqual(1_000d, snapshot.CallbackDurationMilliseconds);
        Assert.AreSame(originalError, snapshot.OriginalError);
        Assert.IsNull(snapshot.ConditionStartedAtUtc);
        Assert.IsNull(snapshot.ErrorHandlingStartedAtUtc);
        Assert.IsNull(snapshot.ErrorHandlingCompletedAtUtc);
        Assert.IsNull(snapshot.CleanupCompletedAtUtc);
        Assert.IsNull(snapshot.SettledAtUtc);
        Assert.IsNull(snapshot.QueueDelayMilliseconds);
        Assert.IsNull(snapshot.ConditionDurationMilliseconds);
        Assert.IsNull(snapshot.ErrorHandlingDurationMilliseconds);
        Assert.IsNull(snapshot.CleanupDurationMilliseconds);
        Assert.IsNull(snapshot.TotalDurationMilliseconds);
    }

    [TestMethod]
    public void SnapshotAndMonitorMeasurementCollectionsAreDetachedFromStateInputLists()
    {
        var measurement = HealthMeasurement.Integer("items", 7, Observation);
        var replacement = HealthMeasurement.Integer("replacement", 9, Observation.AddHours(1));
        var measurements = new List<HealthMeasurement> { measurement };
        var monitor = new HealthMonitorSnapshot(new HealthMonitorSnapshotData
        {
            MonitorId = Guid.NewGuid(),
            Name = "Country cache",
            Policy = HealthPolicyKind.Condition,
            IsCritical = true,
            State = MonitorState.Healthy,
            RegisteredAtUtc = Observation.AddDays(-1),
            LastObservationAtUtc = Observation,
            Measurements = measurements
        });
        var monitors = new List<HealthMonitorSnapshot> { monitor };
        var snapshot = new HealthSnapshot(new HealthSnapshotData
        {
            Revision = 7,
            CapturedAtUtc = Observation.AddDays(1),
            Application = new HealthApplicationSnapshot(new HealthApplicationIdentity
            {
                Name = "Test host",
                InstanceId = Guid.NewGuid()
            }, startupCompleted: true, isStopping: false),
            HealthStatus = HealthStatus.Healthy,
            Ready = true,
            MonitorCounts = new HealthMonitorCounts(active: 1, critical: 1, failing: 0, criticalFailing: 0, pending: 0),
            Monitors = monitors
        });
        measurements[0] = replacement;
        measurements.Clear();
        monitors.Clear();

        Assert.HasCount(1, snapshot.Monitors);
        Assert.AreSame(monitor, snapshot.Monitors[0]);
        Assert.HasCount(1, snapshot.Monitors[0].Measurements);
        Assert.AreSame(measurement, snapshot.Monitors[0].Measurements[0]);
        Assert.AreEqual(Observation.ToUniversalTime(), snapshot.Monitors[0].Measurements[0].ObservedAtUtc);
        Assert.AreEqual(Observation.AddDays(1).ToUniversalTime(), snapshot.CapturedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, snapshot.CapturedAtUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, monitor.RegisteredAtUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, monitor.LastObservationAtUtc!.Value.Offset);
        if (snapshot.Monitors is IList<HealthMonitorSnapshot> mutableMonitors)
        {
            Assert.IsTrue(mutableMonitors.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(() => mutableMonitors.Clear());
        }
        if (monitor.Measurements is IList<HealthMeasurement> mutableMeasurements)
        {
            Assert.IsTrue(mutableMeasurements.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(() => mutableMeasurements[0] = replacement);
        }
        Assert.HasCount(1, snapshot.Monitors);
        Assert.AreSame(measurement, snapshot.Monitors[0].Measurements[0]);
    }

    [TestMethod]
    public void ReportBufferCopiesPayloadAndReportingErrorsCannotSelectHealthy()
    {
        byte[] supplied = [(byte)'{', (byte)'}'];
        var success = new HealthReportBuffer(supplied, isError: false, errorCode: null, selectedHealthy: true);
        supplied[0] = (byte)'x';

        Assert.AreEqual((byte)'{', success.Utf8Bytes.Span[0]);
        Assert.AreEqual((byte)'}', success.Utf8Bytes.Span[1]);
        Assert.IsTrue(success.SelectedHealthy);
        Assert.IsFalse(success.IsError);
        Assert.IsNull(success.ErrorCode);

        var error = new HealthReportBuffer(supplied, isError: true, errorCode: "ReportTooLarge", selectedHealthy: true);
        Assert.IsTrue(error.IsError);
        Assert.AreEqual("ReportTooLarge", error.ErrorCode);
        Assert.IsFalse(error.SelectedHealthy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (HealthDiagnostic Diagnostic, WeakReference[] References) CreateDetachedDiagnostic()
    {
        var inner = new Exception("Inner failure.");
        var outer = new InvalidOperationException("Outer failure.", inner);
        var attachment = new object();
        outer.Data["attachment"] = attachment;
        var references = new[]
        {
            new WeakReference(outer),
            new WeakReference(inner),
            new WeakReference(outer.Data),
            new WeakReference(attachment)
        };
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = HealthDiagnostic.FromException(outer); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A valid runtime exception graph must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return (diagnostic, references);
    }

    private sealed class ThrowingMessageException : Exception
    {
        internal int MessageReadCount { get; private set; }

        public override string Message
        {
            get
            {
                MessageReadCount++;
                throw new InvalidOperationException("The custom message accessor failed.");
            }
        }
    }
}
