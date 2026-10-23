using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthRegistrationDiagnosticTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 17, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RegistrationDiagnosticsAreBoundedDetachedAndIndependentOfHealthAndExecutionErrors()
    {
        await using var scope = new Scope();
        IHealthExecutionBinding binding = scope.Bind();
        HealthSnapshot initial = scope.Service.CaptureSnapshot();
        HealthDiagnostic registration = HealthDiagnostic.FromException(new InvalidOperationException("\uD800" + new string('x', 1_025)), "Registration");

        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, registration)));

        HealthSnapshot observed = scope.Service.CaptureSnapshot();
        HealthMonitorSnapshot monitor = observed.Monitors.Single();
        Assert.AreEqual(initial.Revision + 1, observed.Revision);
        Assert.IsNull(initial.Monitors.Single().RegistrationDiagnostic);
        Assert.AreSame(registration, monitor.RegistrationDiagnostic);
        Assert.IsTrue(registration.IsTruncated);
        Assert.AreEqual("\uFFFD" + new string('x', 1_023), registration.Message);
        Assert.IsNull(monitor.Diagnostic);
        Assert.IsNull(monitor.LastObservationAtUtc);
        Assert.IsNull(monitor.Outcomes!.Execution);
        Assert.AreEqual(MonitorState.Pending, monitor.State);
        Assert.AreEqual(initial.HealthStatus, observed.HealthStatus);
        Assert.AreEqual(initial.Ready, observed.Ready);
        Assert.AreEqual(0L, monitor.Outcomes.Successful);
        Assert.AreEqual(0L, monitor.Outcomes.Failed);

        var policy = new HealthDiagnostic("Policy");
        var execution = new HealthDiagnostic("ExecutionIntegration");
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch,
            WorkCompletedAtUtc = Epoch,
            SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Failed,
            OriginalError = policy
        }));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.IntegrationFault,
            OccurredAtUtc = Epoch,
            IntegrationError = execution
        }));
        HealthMonitorSnapshot failed = scope.Snapshot();
        Assert.AreSame(policy, failed.Diagnostic);
        Assert.AreSame(policy, failed.Outcomes!.LastFailureDiagnostic);
        Assert.AreSame(execution, failed.Outcomes.Execution!.IntegrationError);
        Assert.AreSame(registration, failed.RegistrationDiagnostic);

        scope.Monitor.ReportSuccess();
        HealthMonitorSnapshot recovered = scope.Snapshot();
        Assert.IsNull(recovered.Diagnostic);
        Assert.AreSame(registration, recovered.RegistrationDiagnostic);
        Assert.AreSame(execution, recovered.Outcomes!.Execution!.IntegrationError);
        Assert.IsNull(monitor.Outcomes.Execution);
        Assert.AreSame(registration, monitor.RegistrationDiagnostic);
    }

    [TestMethod]
    [DataRow("code")]
    [DataRow("message")]
    [DataRow("type")]
    [DataRow("truncated")]
    [DataRow("time")]
    [DataRow("origin")]
    public async Task IdenticalNormalizedEventsAreDuplicateWhileEveryChangedDiagnosticEventCommits(string changedField)
    {
        await using var scope = new Scope();
        IHealthExecutionBinding binding = scope.Bind();
        var first = new HealthDiagnostic("code", new string('x', 1_024), "type");
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, first)));
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Fault(binding,
            new HealthDiagnostic("code", new string('x', 1_024), "type"), Epoch.ToOffset(TimeSpan.FromHours(2)))));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);

        var changed = new HealthDiagnostic(changedField == "code" ? "changed" : "code",
            changedField == "message" ? "changed" : new string('x', changedField == "truncated" ? 1_025 : 1_024),
            changedField == "type" ? "changed" : "type");
        HealthExecutionFact next = Fault(binding, changed, changedField == "time" ? Epoch.AddTicks(-1) : Epoch,
            changedField == "origin" ? HealthExecutionPhase.Registered : null);
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(next));
        Assert.AreEqual(before.Revision + 1, scope.Service.CaptureSnapshot().Revision);
        Assert.AreSame(changed, scope.Snapshot().RegistrationDiagnostic);
        Assert.AreSame(first, before.Monitors.Single().RegistrationDiagnostic);
        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(next));
        Assert.AreEqual(before.Revision + 1, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
        Assert.IsNull(scope.Snapshot().LastObservationAtUtc);
    }

    [TestMethod]
    public async Task DuplicateIdentityUsesTheNormalizedBoundedDiagnosticIncludingAbsentFields()
    {
        await using var scope = new Scope();
        IHealthExecutionBinding binding = scope.Bind();
        var empty = new HealthDiagnostic();
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, empty)));
        HealthSnapshot first = scope.Service.CaptureSnapshot();
        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Fault(binding, new HealthDiagnostic())));
        Assert.AreEqual(first.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreSame(empty, scope.Snapshot().RegistrationDiagnostic);

        var truncated = new HealthDiagnostic(message: new string('x', 1_025));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, truncated)));
        HealthSnapshot second = scope.Service.CaptureSnapshot();
        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Fault(binding,
            new HealthDiagnostic(message: new string('x', 2_048)), Epoch.ToOffset(TimeSpan.FromHours(-3)))));
        Assert.AreEqual(second.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreSame(truncated, scope.Snapshot().RegistrationDiagnostic);
    }

    [TestMethod]
    public async Task MonitorCommitOrderWinsAcrossBindingsAndAnOldDuplicateCannotOverwriteIt()
    {
        await using var scope = new Scope();
        IHealthExecutionBinding a = scope.Bind(), b = scope.Bind();
        Assert.AreEqual(HealthFactDisposition.Recorded, a.Report(Fault(a, new HealthDiagnostic("A"), Epoch.AddDays(1))));
        var second = new HealthDiagnostic("B");
        Assert.AreEqual(HealthFactDisposition.Recorded, b.Report(Fault(b, second, Epoch.AddDays(-1))));
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.AreSame(second, before.Monitors.Single().RegistrationDiagnostic);

        Assert.AreEqual(HealthFactDisposition.Duplicate, a.Report(Fault(a, new HealthDiagnostic("A"), Epoch.AddDays(1))));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreSame(second, scope.Snapshot().RegistrationDiagnostic);
        var last = new HealthDiagnostic("C");
        Assert.AreEqual(HealthFactDisposition.Recorded, a.Report(Fault(a, last, Epoch.AddDays(-2))));
        Assert.AreEqual(before.Revision + 1, scope.Service.CaptureSnapshot().Revision);
        Assert.AreSame(last, scope.Snapshot().RegistrationDiagnostic);
        Assert.AreSame(second, before.Monitors.Single().RegistrationDiagnostic);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExternalRetirementPublishesAnOptionalReasonAndLaterFactsCannotReplaceIt(bool withReason)
    {
        await using var scope = new Scope();
        IHealthExecutionBinding binding = scope.Bind();
        var first = new HealthDiagnostic("Initial");
        var reason = new HealthDiagnostic("Retirement");
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, first)));
        HealthSnapshot prior = scope.Service.CaptureSnapshot();
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        { RegistrationId = binding.RegistrationId, Kind = HealthExecutionFactKind.Retired, OccurredAtUtc = Epoch, IntegrationError = withReason ? reason : null }));

        HealthSnapshot retired = scope.Service.CaptureSnapshot();
        Assert.AreEqual(prior.Revision + 1, retired.Revision);
        Assert.AreEqual(1, retired.MonitorCounts.Active);
        Assert.AreSame(withReason ? reason : first, retired.Monitors.Single().RegistrationDiagnostic);
        Assert.AreSame(first, prior.Monitors.Single().RegistrationDiagnostic);
        Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(Fault(binding, new HealthDiagnostic("Late"))));
        await DisposeExternalAssociationAsync(binding);
        Assert.AreEqual(retired.Revision, scope.Service.CaptureSnapshot().Revision);
        scope.Monitor.ReportSuccess();
        Assert.AreSame(withReason ? reason : first, scope.Snapshot().RegistrationDiagnostic);
    }

    [TestMethod]
    public async Task OwnedRetirementWithdrawsCurrentStateAndPreservesTheEarlierImmutableDiagnostic()
    {
        await using var service = new HealthcheckService(new(), Identity(), new ManualTimeProvider(Epoch));
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 1 }, new() { RegistrationId = Guid.NewGuid() });
        try
        {
            var first = new HealthDiagnostic("Owned");
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, first)));
            HealthSnapshot prior = service.CaptureSnapshot();
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = binding.RegistrationId, Kind = HealthExecutionFactKind.Retired, OccurredAtUtc = Epoch, IntegrationError = new HealthDiagnostic("FinalOwned") }));
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.AreSame(first, prior.Monitors.Single().RegistrationDiagnostic);
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(Fault(binding, new HealthDiagnostic("Late"))));
            await binding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Task disposal = binding.DisposeAsync().AsTask();
            if (disposal.IsCompleted) { await disposal; }
        }
    }

    [TestMethod]
    public async Task RegistrationExternalUnbindingAndOptionalCollectionNeverClearTheRegistrationDiagnostic()
    {
        var scope = new Scope();
        HealthCollectorRunner? runner = null;
        IHealthCollectionRegistration? collection = null;
        Exception? primary = null;
        try
        {
            IHealthExecutionBinding binding = scope.Bind();
            var diagnostic = new HealthDiagnostic("Retained");
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, diagnostic)));
            var registered = new HealthExecutionFact { RegistrationId = binding.RegistrationId, Kind = HealthExecutionFactKind.Registered, OccurredAtUtc = Epoch };
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(registered));
            HealthSnapshot prior = scope.Service.CaptureSnapshot();
            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(registered));
            Assert.AreEqual(prior.Revision, scope.Service.CaptureSnapshot().Revision);
            await DisposeExternalAssociationAsync(binding);
            Assert.AreSame(diagnostic, scope.Snapshot().RegistrationDiagnostic);

            runner = new HealthCollectorRunner(scope.Service);
            collection = runner.Register(scope.Monitor, new FailingCollector(), new() { IsRequired = false });
            await runner.StartAsync().WaitAsync(Deadline);
            await collection.CollectAsync().WaitAsync(Deadline);
            HealthMonitorSnapshot after = scope.Snapshot();
            Assert.IsNotNull(after.Collection);
            Assert.IsNotNull(after.Collection.LastError, "The failing optional collection must retain its own evaluation diagnostic.");
            Assert.AreEqual("Healthcheck.CollectionEvaluation", after.Collection.LastError.Code);
            Assert.AreSame(diagnostic, after.RegistrationDiagnostic);
            Assert.IsNull(after.Diagnostic);
            Assert.IsNull(after.Outcomes!.Execution);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try
            {
                Task registration = DrainRegistrationAsync();
                Task collector = DrainRunnerAsync();
                Task state = DrainScopeAsync();
                await Task.WhenAll(registration, collector, state).WaitAsync(Deadline);
            }
            catch (Exception cleanup) when (primary is not null)
            { throw new AggregateException("The receiving body failed and actual cleanup did not complete successfully.", primary, cleanup); }

            async Task DrainRegistrationAsync() { if (collection is not null) { await collection.DisposeAsync(); } }
            async Task DrainRunnerAsync() { if (runner is not null) { await runner.DisposeAsync(); } }
            async Task DrainScopeAsync() => await scope.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealRegistrationDiagnosticsRoundTripInDetailsAndRemainAbsentFromCompactViews(bool pipe)
    {
        await using var scope = new Scope();
        IHealthExecutionBinding binding = scope.Bind();
        using (JsonDocument before = JsonDocument.Parse(Format(scope.Service.CaptureSnapshot(), pipe).Utf8Bytes))
        { Assert.IsFalse(Root(before, pipe).GetProperty("monitors")[0].TryGetProperty("registrationDiagnostic", out _)); }
        HealthDiagnostic diagnostic = HealthDiagnostic.FromException(new InvalidOperationException("\uD800" + new string('x', 1_025)), "RegistrationIssue");
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Fault(binding, diagnostic)));
        HealthReportBuffer report = Format(scope.Service.CaptureSnapshot(), pipe);
        Assert.IsFalse(report.IsError);
        using JsonDocument document = JsonDocument.Parse(report.Utf8Bytes);
        JsonElement actual = Root(document, pipe).GetProperty("monitors")[0].GetProperty("registrationDiagnostic");
        Assert.AreEqual(diagnostic.Code, actual.GetProperty("code").GetString());
        Assert.AreEqual(diagnostic.Message, actual.GetProperty("message").GetString());
        Assert.AreEqual(diagnostic.ExceptionType, actual.GetProperty("exceptionType").GetString());
        Assert.IsTrue(actual.GetProperty("isTruncated").GetBoolean());
        if (pipe)
        {
            HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(report.Utf8Bytes, HealthReportView.Details, 4_194_304);
            Assert.IsFalse(parsed.IsError);
            using JsonDocument detached = JsonDocument.Parse(parsed.SnapshotUtf8);
            Assert.AreEqual(diagnostic.Code, detached.RootElement.GetProperty("monitors")[0].GetProperty("registrationDiagnostic").GetProperty("code").GetString());
        }
        HealthReportBuffer compact = pipe ? HealthReportFormatter.FormatPipeResponse(scope.Service.CaptureSnapshot(), HealthReportView.Health, new())
            : HealthReportFormatter.FormatSnapshot(scope.Service.CaptureSnapshot(), HealthReportView.Health, new());
        using JsonDocument summary = JsonDocument.Parse(compact.Utf8Bytes);
        Assert.IsFalse(Root(summary, pipe).TryGetProperty("monitors", out _));
    }

    [TestMethod]
    [DataRow("code", 64)]
    [DataRow("message", 1_024)]
    [DataRow("exceptionType", 256)]
    public void RegistrationDiagnosticFieldsHaveTheSameScalarBoundsAndTypesAsOtherDiagnostics(string field, int maximum)
    {
        JsonObject envelope = Fixture();
        JsonNode diagnostic = envelope["snapshot"]!["monitors"]![0]!["registrationDiagnostic"]!;
        foreach (string valid in new[] { new string('x', maximum), string.Concat(Enumerable.Repeat("\U0001F680", maximum)) })
        { diagnostic[field] = valid; Assert.IsFalse(Parse(envelope).IsError); }
        diagnostic[field] = new string('x', maximum + 1);
        Reject(envelope);
        diagnostic[field] = null;
        Reject(envelope);
        diagnostic[field] = true;
        Reject(envelope);
        diagnostic[field] = 1;
        Reject(envelope);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("array")]
    [DataRow("string")]
    [DataRow("number")]
    [DataRow("missing-flag")]
    [DataRow("wrong-flag")]
    public void PresentRegistrationDiagnosticsRequireAnObjectAndTheBooleanTruncationFlag(string invalid)
    {
        JsonObject envelope = Fixture();
        Assert.IsFalse(Parse(envelope).IsError);
        JsonNode monitor = envelope["snapshot"]!["monitors"]![0]!;
        switch (invalid)
        {
            case "null": monitor["registrationDiagnostic"] = null; break;
            case "array": monitor["registrationDiagnostic"] = new JsonArray(); break;
            case "string": monitor["registrationDiagnostic"] = "invalid"; break;
            case "number": monitor["registrationDiagnostic"] = 1; break;
            case "missing-flag": monitor["registrationDiagnostic"]!.AsObject().Remove("isTruncated"); break;
            default: monitor["registrationDiagnostic"]!["isTruncated"] = "false"; break;
        }
        Reject(envelope);
    }

    [TestMethod]
    public void RegistrationDiagnosticFixturesAndEarlierOmissionRemainCompatibleAndMalformedUnicodeIsRejected()
    {
        JsonObject envelope = Fixture();
        Assert.IsFalse(Parse(envelope).IsError);
        envelope["snapshot"]!["monitors"]![0]!.AsObject().Remove("registrationDiagnostic");
        Assert.IsFalse(Parse(envelope).IsError);
        envelope["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"));
        Assert.IsFalse(Parse(envelope).IsError);
        envelope["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("invalid", "registration-diagnostic-null.json"));
        Reject(envelope);
        string malformed = Fixture().ToJsonString().Replace("RegistrationIssue", "\\ud800", StringComparison.Ordinal);
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(malformed), HealthReportView.Details, 4_194_304));
        JsonObject boundedEmpty = Fixture();
        boundedEmpty["snapshot"]!["monitors"]![0]!["registrationDiagnostic"] = new JsonObject { ["isTruncated"] = false };
        Assert.IsFalse(Parse(boundedEmpty).IsError);
    }

    private static HealthExecutionFact Fault(IHealthExecutionBinding binding, HealthDiagnostic diagnostic, DateTimeOffset? at = null, HealthExecutionPhase? origin = null)
        => new() { RegistrationId = binding.RegistrationId, Kind = HealthExecutionFactKind.IntegrationFault, OccurredAtUtc = at ?? Epoch, SourcePhase = origin, IntegrationError = diagnostic };
    private static HealthApplicationIdentity Identity() => new() { Name = "registration diagnostics", InstanceId = Guid.NewGuid() };
    private static HealthReportBuffer Format(HealthSnapshot snapshot, bool pipe) => pipe
        ? HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Details, new())
        : HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new());
    private static JsonElement Root(JsonDocument document, bool pipe) => pipe ? document.RootElement.GetProperty("snapshot") : document.RootElement;
    private static JsonObject Fixture() => new()
    {
        ["protocolVersion"] = 1,
        ["schemaVersion"] = 1,
        ["view"] = "details",
        ["kind"] = "snapshot",
        ["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "registration-diagnostic-details.json"))
    };
    private static HealthParsedResponse Parse(JsonObject envelope) => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(envelope.ToJsonString()), HealthReportView.Details, 4_194_304);
    private static void Reject(JsonObject envelope) => Assert.ThrowsExactly<FormatException>(() => Parse(envelope));

    private static async Task DisposeExternalAssociationAsync(IHealthExecutionBinding binding)
    {
        // Request release without assuming a synchronous wrapper completion.
        // Scope.DisposeAsync always joins the actual service drain separately.
        Task disposal = binding.DisposeAsync().AsTask();
        if (disposal.IsCompleted) { await disposal; }
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IHealthExecutionBinding> bindings = [];
        internal Scope()
        {
            Service = new HealthcheckService(new(), Identity(), new ManualTimeProvider(Epoch));
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Service.MarkStartupCompleted();
        }
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal IHealthExecutionBinding Bind()
        {
            IHealthExecutionBinding binding = HealthExecutionBinder.Bind(Monitor, new() { RegistrationId = Guid.NewGuid() });
            bindings.Add(binding);
            return binding;
        }
        internal HealthMonitorSnapshot Snapshot() => Service.CaptureSnapshot().Monitors.Single();
        public async ValueTask DisposeAsync()
        {
            try { await Task.WhenAll(bindings.Select(DisposeExternalAssociationAsync)); }
            finally
            {
                try { await Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                finally { GC.KeepAlive(Monitor); }
            }
        }
    }

    private sealed class FailingCollector : IHealthCollector
    {
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(); return true; }
        private sealed class Lease : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
                => Task.FromException<HealthCollectionResult>(new InvalidOperationException("optional read"));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
