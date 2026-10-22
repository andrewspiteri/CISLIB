using System.Globalization;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.Healthcheck.Reporting;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportFormatterMutationQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(2));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DetailedFormattingPreservesEveryActualExecutionCollectionAndIdentityField(bool pipe)
    {
        var clock = new ManualTimeProvider(Epoch);
        var identity = new HealthApplicationIdentity
        {
            Name = "complete-formatter",
            InstanceId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"),
            BuildVersion = "build-17",
            ServiceVersion = "service-23",
            HostName = "host-31",
            OsDescription = "os-47"
        };
        await using var service = new HealthcheckService(new HealthcheckOptions(), identity, clock);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions
        { IsCritical = true, FailureThreshold = 1, Name = "owned-execution" });
        monitor.ReportSuccess();
        monitor.ReportSkipped();
        monitor.ReportExpectedCancellation();
        await using IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid() });
        var original = new HealthDiagnostic("Original", new string('x', 1025), "Original.Error");
        var handler = new HealthDiagnostic("Handler", "handler reason", "Handler.Error");
        var cleanup = new HealthDiagnostic("Cleanup", "cleanup reason", "Cleanup.Error");
        var integration = new HealthDiagnostic("Integration", "integration reason", "Integration.Error");
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            Kind = HealthExecutionFactKind.Outcome,
            ExecutionId = 42,
            OccurredAtUtc = Epoch.AddSeconds(10),
            WorkCompletedAtUtc = Epoch.AddSeconds(10),
            SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Failed,
            OriginalError = original,
            EligibleAtUtc = Epoch.AddSeconds(1),
            AdmittedAtUtc = Epoch.AddSeconds(2),
            ConditionStartedAtUtc = Epoch.AddSeconds(3),
            CallbackStartedAtUtc = Epoch.AddSeconds(4),
            QueueDelayMilliseconds = 1.25,
            ConditionDurationMilliseconds = 2.5,
            CallbackDurationMilliseconds = 3.75
        }));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            Kind = HealthExecutionFactKind.ErrorHandlingStarted,
            ExecutionId = 42,
            OccurredAtUtc = Epoch.AddSeconds(11),
            ErrorHandlingStartedAtUtc = Epoch.AddSeconds(11),
            ErrorHandlingStatus = HealthExecutionStepStatus.Running
        }));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            Kind = HealthExecutionFactKind.ErrorHandlingCompleted,
            ExecutionId = 42,
            OccurredAtUtc = Epoch.AddSeconds(12),
            ErrorHandlingCompletedAtUtc = Epoch.AddSeconds(12),
            ErrorHandlingStatus = HealthExecutionStepStatus.Failed,
            ErrorHandlingDurationMilliseconds = 4.5,
            ErrorHandlingError = handler
        }));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            Kind = HealthExecutionFactKind.CleanupCompleted,
            ExecutionId = 42,
            OccurredAtUtc = Epoch.AddSeconds(13),
            CleanupCompletedAtUtc = Epoch.AddSeconds(13),
            CleanupStatus = HealthExecutionStepStatus.Failed,
            CleanupDurationMilliseconds = 5.75,
            CleanupError = cleanup
        }));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            Kind = HealthExecutionFactKind.IntegrationFault,
            ExecutionId = 42,
            OccurredAtUtc = Epoch.AddSeconds(13),
            IntegrationError = integration
        }));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            Kind = HealthExecutionFactKind.Settled,
            ExecutionId = 42,
            OccurredAtUtc = Epoch.AddSeconds(14),
            SettledAtUtc = Epoch.AddSeconds(14),
            TotalDurationMilliseconds = 15.875,
            ErrorHandlingStatus = HealthExecutionStepStatus.Failed,
            CleanupStatus = HealthExecutionStepStatus.Failed
        }));
        monitor.ReportMeasurement(HealthMeasurement.String("a", "text\n\U0001F680", Epoch.AddSeconds(1), "text"));
        monitor.ReportMeasurement(HealthMeasurement.Boolean("b", false, Epoch.AddSeconds(2)));
        monitor.ReportMeasurement(HealthMeasurement.Integer("c", long.MinValue, Epoch.AddSeconds(3), ""));
        monitor.ReportMeasurement(HealthMeasurement.Number("d", 0.125, Epoch.AddSeconds(4), " \t"));
        monitor.ReportMeasurement(HealthMeasurement.Unavailable("e", Epoch.AddSeconds(5)));
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions { IsRequired = false });
        HealthCollectionReservation reservation = state.TryReserve(target).Reservation!;
        var collection = new HealthDiagnostic("Collection", "optional reason", "Collection.Error");
        try
        {
            Assert.IsTrue(state.TryEnter(reservation));
            state.CommitEvaluationFailure(reservation, collection);
        }
        finally { state.Complete(reservation, new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, Epoch.AddSeconds(20))); }
        service.MarkStartupCompleted();
        clock.Advance(TimeSpan.FromMinutes(1));
        HealthSnapshot snapshot = service.CaptureSnapshot();

        HealthReportBuffer result = pipe ? HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Details, new HealthcheckOptions())
            : HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions());

        Assert.IsFalse(result.IsError);
        Assert.IsFalse(result.SelectedHealthy);
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement root = pipe ? document.RootElement.GetProperty("snapshot") : document.RootElement;
        if (pipe)
        {
            Assert.AreEqual("details", document.RootElement.GetProperty("view").GetString());
            Assert.IsFalse(HealthReportProtocol.ParseResponse(result.Utf8Bytes, HealthReportView.Details, 4_194_304).SelectedHealthy);
        }
        JsonElement application = root.GetProperty("application");
        Assert.AreEqual(identity.Name, application.GetProperty("name").GetString());
        Assert.AreEqual(identity.InstanceId.ToString("D"), application.GetProperty("instanceId").GetString());
        foreach ((string key, string value) in new[] { ("buildVersion", "build-17"), ("serviceVersion", "service-23"), ("hostName", "host-31"), ("osDescription", "os-47") })
        { Assert.AreEqual(value, application.GetProperty(key).GetString(), key); }
        JsonElement detail = root.GetProperty("monitors")[0];
        Assert.AreEqual("owned-execution", detail.GetProperty("name").GetString());
        Assert.AreEqual("Unhealthy", detail.GetProperty("state").GetString());
        TimeEquals(Epoch, detail.GetProperty("registeredAtUtc"));
        TimeEquals(Epoch.AddSeconds(10), detail.GetProperty("lastObservationAtUtc"));
        DiagnosticEquals(original, detail.GetProperty("diagnostic"));
        JsonElement outcomes = detail.GetProperty("outcomes");
        foreach (string key in new[] { "successful", "failed", "skipped", "expectedCanceled", "consecutiveFailures" })
        { Assert.AreEqual("1", outcomes.GetProperty(key).GetString(), key); }
        Assert.AreEqual("Failed", outcomes.GetProperty("lastOutcome").GetString());
        TimeEquals(Epoch, outcomes.GetProperty("lastSuccessAtUtc"));
        TimeEquals(Epoch.AddSeconds(10), outcomes.GetProperty("lastFailureAtUtc"));
        DiagnosticEquals(original, outcomes.GetProperty("lastFailureDiagnostic"));
        JsonElement execution = outcomes.GetProperty("execution");
        Assert.AreEqual(binding.RegistrationId.ToString("D"), execution.GetProperty("registrationId").GetString());
        Assert.AreEqual("42", execution.GetProperty("executionId").GetString());
        Assert.AreEqual("Settled", execution.GetProperty("phase").GetString());
        foreach ((string key, int second) in new[] { ("eligibleAtUtc", 1), ("admittedAtUtc", 2), ("conditionStartedAtUtc", 3), ("callbackStartedAtUtc", 4),
            ("workCompletedAtUtc", 10), ("errorHandlingStartedAtUtc", 11), ("errorHandlingCompletedAtUtc", 12), ("cleanupCompletedAtUtc", 13), ("settledAtUtc", 14) })
        { TimeEquals(Epoch.AddSeconds(second), execution.GetProperty(key)); }
        foreach ((string key, double value) in new[] { ("queueDelayMs", 1.25), ("conditionDurationMs", 2.5), ("callbackDurationMs", 3.75),
            ("errorHandlingDurationMs", 4.5), ("cleanupDurationMs", 5.75), ("totalDurationMs", 15.875) })
        { Assert.AreEqual(value, execution.GetProperty(key).GetDouble(), key); }
        Assert.AreEqual("Failed", execution.GetProperty("errorHandlingStatus").GetString());
        Assert.AreEqual("Failed", execution.GetProperty("cleanupStatus").GetString());
        foreach ((string key, HealthDiagnostic value) in new[] { ("originalError", original), ("errorHandlingError", handler), ("cleanupError", cleanup), ("integrationError", integration) })
        { DiagnosticEquals(value, execution.GetProperty(key)); }
        JsonElement collected = detail.GetProperty("collection");
        Assert.IsFalse(collected.GetProperty("inProgress").GetBoolean());
        TimeEquals(Epoch.AddSeconds(20), collected.GetProperty("lastCompletedAtUtc"));
        TimeEquals(Epoch, collected.GetProperty("lastErrorAtUtc"));
        DiagnosticEquals(collection, collected.GetProperty("lastError"));
        JsonElement measurements = detail.GetProperty("measurements");
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, measurements.EnumerateArray().Select(value => value.GetProperty("key").GetString()).ToArray());
        Assert.AreEqual("text\n\U0001F680", measurements[0].GetProperty("value").GetString());
        Assert.AreEqual("text", measurements[0].GetProperty("unit").GetString());
        Assert.IsFalse(measurements[1].GetProperty("value").GetBoolean());
        Assert.AreEqual(long.MinValue.ToString(CultureInfo.InvariantCulture), measurements[2].GetProperty("value").GetString());
        Assert.AreEqual(0.125, measurements[3].GetProperty("value").GetDouble());
        Assert.IsFalse(measurements[4].TryGetProperty("value", out _));
        for (int index = 0; index < measurements.GetArrayLength(); index++)
        {
            TimeEquals(Epoch.AddSeconds(index + 1), measurements[index].GetProperty("observedAtUtc"));
            Assert.IsTrue(measurements[index].GetProperty("isDiagnostic").GetBoolean());
            if (index != 0) { Assert.IsFalse(measurements[index].TryGetProperty("unit", out _)); }
        }
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public void BoundedWriterCountsCommittedBytesAndAllowsExactlyFullStorage()
    {
        var writer = new BoundedHealthBuffer(4);
        Memory<byte> initial = writer.GetMemory(100);
        new byte[] { 1, 2, 3 }.CopyTo(initial);
        writer.Advance(3);
        Assert.AreEqual(3, writer.WrittenSpan.Length);
        Span<byte> final = writer.GetSpan(1);
        final[0] = 4;
        writer.Advance(1);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, writer.WrittenSpan.ToArray());
        Assert.ThrowsExactly<HealthReportTooLargeException>(() => writer.Advance(1));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, writer.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void BoundedWriterRejectsInvalidAdvanceAndScratchHintsWithoutPublishingBytes()
    {
        var writer = new BoundedHealthBuffer(32);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.GetMemory(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.Advance(-1));
        int scratch = writer.GetMemory().Length;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.Advance(scratch + 1));
        const int largestHint = 32 * 6 + 4096;
        Assert.IsTrue(writer.GetMemory(largestHint).Length >= largestHint);
        Assert.ThrowsExactly<HealthReportTooLargeException>(() => writer.GetMemory(largestHint + 1));
        Assert.AreEqual(0, writer.WrittenSpan.Length);
    }

    private static void TimeEquals(DateTimeOffset expected, JsonElement actual) => Assert.AreEqual(expected.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), actual.GetString());
    private static void DiagnosticEquals(HealthDiagnostic expected, JsonElement actual)
    {
        Assert.AreEqual(expected.Code, actual.GetProperty("code").GetString());
        Assert.AreEqual(expected.Message, actual.GetProperty("message").GetString());
        Assert.AreEqual(expected.ExceptionType, actual.GetProperty("exceptionType").GetString());
        Assert.AreEqual(expected.IsTruncated, actual.GetProperty("isTruncated").GetBoolean());
        CollectionAssert.AreEquivalent(new[] { "code", "message", "exceptionType", "isTruncated" }, actual.EnumerateObject().Select(property => property.Name).ToArray());
    }
}
