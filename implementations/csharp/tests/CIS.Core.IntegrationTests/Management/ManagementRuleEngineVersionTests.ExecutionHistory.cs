using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Configuration;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Messaging;
using CIS.RuleEngine;
using CIS.Security;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Audit;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Tests.MessagingCrashFixture;
using Npgsql;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementRuleEngineVersionTests
{
    [TestMethod]
    public async Task AuthenticatedAuditMergesExactPhasesAndRetainsHistoryAcrossReceiverOutage()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: RuleDeployment(retainedUnreferencedReleases: 0), ruleAuditManagedApplication: "managed-a");
        async Task StartReceiver() => await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected,
            fixture.ApplicationA, secondEditor: false, deployment: true, executionHistory: true));

        await StartReceiver();
        HttpClient actor = fixture.Client("operator-a");
        HttpClient reader = fixture.Client("operator-b");
        HttpClient workload = fixture.Client("workload-a");
        string application = fixture.ApplicationA;
        await Apply(actor, await Catalog(actor), "history-catalog");
        string registration = await Register(workload);
        string first = await SaveCountry(actor, application, "history-save-first", "MLT");
        string savedOnly = await SaveCountry(actor, application, "history-save-second", "ITA", first);
        JsonElement deployment = await DeployAccepted(actor, application, "history-deploy-first",
            (await Assignment(actor, application)).ETag, first);
        string definitionJson = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + first), "definitionJson");
        ManagementConfigurationDesiredContext desired = await ReadWorkloadDesired(workload, registration);
        var target = new ConfigurationSnapshotTarget(desired.AuthorityId, desired.ApplicationId, desired.Environment,
            desired.RegistrationId, desired.HostApplicationName, desired.CohortId);
        var limits = new AuditLimits();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        await using var resources = new MessagingResourceBudget(AuditNativeTestSupport.Snapshot(limits).Settings);
        await using var captureEngine = new AuditCaptureEngine(identity, limits, TimeProvider.System, resources);
        IAuditCapture capture = new AuditCapture(captureEngine, null);
        var inputs = new RuleInputSnapshot([new("country", RuleValue.FromString("MLT"))]);
        var facts = new RuleExecutionAuditFacts(Guid.NewGuid(), target, identity, RulesetId, SchemaRevision, first,
            "owned-native-history", DateTimeOffset.UtcNow);
        CapturedAuditRecord start = CaptureExecutionStart(capture, facts, inputs);
        PreparedRuleDefinition prepared = RuleDefinitionValidator.Prepare(RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(definitionJson)));
        RuleEvaluationTrace trace = RuleMatcher.Evaluate(prepared, inputs);
        Assert.HasCount(1, trace.SelectedRowIndices);
        DateTimeOffset finished = DateTimeOffset.UtcNow;
        CapturedAuditRecord completion = capture.CaptureLocalSystem(RuleExecutionAudit.Complete,
            new RuleExecutionAuditCompletion(facts, trace, finished, null),
            RuleExecutionAudit.CaptureOptions(facts, AuditOutcome.Succeeded, finished, start.Record.AuditId));

        await PublishAndAwaitExecutionReceipt(fixture, completion);
        JsonElement detail = await Get(actor, ExecutionDetailsRoute(application, facts.ExecutionId));
        AssertExecution(detail.GetProperty("execution"), facts, "Incomplete", false, true);
        Assert.AreEqual(ExactAuditJson(completion), ReadText(detail, "completionAuditJson"));
        Assert.IsFalse(detail.TryGetProperty("startAuditJson", out _));

        await PublishAndAwaitExecutionReceipt(fixture, start);
        await PublishAndAwaitExecutionReceipt(fixture, start);
        await PublishAndAwaitExecutionReceipt(fixture, completion);
        detail = await Get(actor, ExecutionDetailsRoute(application, facts.ExecutionId));
        AssertExecution(detail.GetProperty("execution"), facts, "Completed", true, true, returnedMatchCount: 1);
        Assert.AreEqual(ExactAuditJson(start), ReadText(detail, "startAuditJson"));
        Assert.AreEqual(ExactAuditJson(completion), ReadText(detail, "completionAuditJson"));
        Assert.AreEqual(definitionJson, ReadText(detail, "definitionJson"));
        Assert.AreEqual(2L, await Count(fixture, fixture.Options.Persistence.Schema, "rule_engine_execution_event"));

        CapturedAuditRecord conflict = CaptureExecutionStart(capture, facts with
        {
            CorrelationId = "different-original"
        }, inputs);
        await PublishAndAwaitExecutionReceipt(fixture, conflict, refusal: "management.rule-engine.execution_conflict");
        var foreign = facts with
        {
            ExecutionId = Guid.NewGuid(),
            Target = new ConfigurationSnapshotTarget(target.AuthorityId,
            fixture.ApplicationB, target.Environment, registration, target.HostApplicationName)
        };
        await PublishAndAwaitExecutionReceipt(fixture, CaptureExecutionStart(capture, foreign, inputs),
            refusal: "management.rule-engine.execution_invalid");
        var undeployed = facts with
        {
            ExecutionId = Guid.NewGuid(),
            Version = savedOnly
        };
        CapturedAuditRecord undeployedStart = CaptureExecutionStart(capture, undeployed, inputs);
        await PublishAndAwaitExecutionReceipt(fixture, undeployedStart, refusal: "management.rule-engine.execution_invalid");
        var untrusted = facts with
        {
            ExecutionId = Guid.NewGuid()
        };
        await PublishAndAwaitExecutionReceipt(fixture, CaptureExecutionStart(capture, untrusted, inputs), trusted: false);
        await AssertExecutionHttpStatus(actor, ExecutionDetailsRoute(application, untrusted.ExecutionId), HttpStatusCode.NotFound);
        await AssertExecutionHttpStatus(actor, ExecutionDetailsRoute(application, undeployed.ExecutionId), HttpStatusCode.NotFound);
        await AssertExecutionHttpStatus(reader, ExecutionDetailsRoute(application, facts.ExecutionId), HttpStatusCode.NotFound);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "rule_engine_execution"));

        await DeployAccepted(actor, application, "history-deploy-second", ReadText(deployment, "assignmentETag"), savedOnly);
        await PublishAndAwaitExecutionReceipt(fixture, undeployedStart, refusal: "management.rule-engine.execution_invalid");
        await AssertExecutionHttpStatus(actor, ExecutionDetailsRoute(application, undeployed.ExecutionId), HttpStatusCode.NotFound);
        await fixture.StopReplicasAsync();
        await PruneSupersededCarrier(fixture);
        var incomplete = facts with
        {
            ExecutionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = null
        };
        (EncodedMessage buffered, string childInstanceId) = await InterruptAfterAuditStart(incomplete);
        string incompleteStartJson = Encoding.UTF8.GetString(buffered.Envelope.CopyPayload());
        await fixture.PublishRuleAuditAsync(buffered);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "rule_engine_execution"));

        int restarted = fixture.Origins.Count;
        await StartReceiver();
        await AwaitExecutionReceipt(fixture, buffered.Envelope.MessageId, trusted: true);
        actor = fixture.Client("operator-a", restarted);
        reader = fixture.Client("operator-b", restarted);
        detail = await Get(actor, ExecutionDetailsRoute(application, incomplete.ExecutionId));
        AssertExecution(detail.GetProperty("execution"), incomplete, "Incomplete", true, false, childInstanceId);
        Assert.AreEqual(incompleteStartJson, ReadText(detail, "startAuditJson"));
        Assert.IsFalse(detail.TryGetProperty("completionAuditJson", out _));
        Assert.AreEqual(definitionJson, ReadText(detail, "definitionJson"));
        Assert.AreEqual(definitionJson, ReadText(await Get(actor, ExecutionDetailsRoute(application, facts.ExecutionId)), "definitionJson"));

        string listRoute = RuleRoute(application, "executions");
        string filteredRoute = listRoute + $"&status=Incomplete&version={first}&executionId={incomplete.ExecutionId:D}"
            + "&instanceId=" + Uri.EscapeDataString(childInstanceId);
        JsonElement filtered = (await Get(reader, filteredRoute)).GetProperty("items");
        Assert.AreEqual(1, filtered.GetArrayLength());
        AssertExecution(filtered[0], incomplete, "Incomplete", true, false, childInstanceId);
        Assert.AreEqual(0, (await Get(actor, listRoute + "&version=" + savedOnly)).GetProperty("items").GetArrayLength());
        JsonElement page = await Get(actor, listRoute + "&pageSize=1");
        Assert.AreEqual(1, page.GetProperty("items").GetArrayLength());
        string next = listRoute + "&pageSize=1&cursor=" + Uri.EscapeDataString(ReadText(page, "nextCursor"));
        JsonElement lastPage = await Get(actor, next);
        Assert.AreEqual(1, lastPage.GetProperty("items").GetArrayLength());
        Assert.AreNotEqual(ReadText(page.GetProperty("items")[0], "executionId"), ReadText(lastPage.GetProperty("items")[0], "executionId"));
        Assert.IsFalse(lastPage.TryGetProperty("nextCursor", out _));
        await AssertExecutionHttpStatus(reader, next, HttpStatusCode.BadRequest);

        var cancelled = facts with
        {
            ExecutionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        CapturedAuditRecord cancelledStart = CaptureExecutionStart(capture, cancelled, inputs);
        var cancellation = new RuleEvaluationError("Cancelled", "The test interrupted the row before its first cell.");
        DateTimeOffset cancelledAt = DateTimeOffset.UtcNow;
        CapturedAuditRecord CaptureCancellation(RuleCellOutcome cellOutcome)
        {
            var cancelledTrace = new RuleEvaluationTrace(RuleExecutionStatus.Cancelled, [], [],
                [new(0, trace.Rows[0].State, RuleRowOutcome.Error, [new(0, cellOutcome)],
                    cancellation with { RowIndex = 0, ColumnIndex = 0 })], [], cancellation);
            return capture.CaptureLocalSystem(RuleExecutionAudit.Complete,
                new RuleExecutionAuditCompletion(cancelled, cancelledTrace, cancelledAt, null),
                RuleExecutionAudit.CaptureOptions(cancelled, AuditOutcome.Canceled, cancelledAt, cancelledStart.AuditId));
        }
        Assert.IsFalse(RuleEngineAuditObservationCodec.TryDecode(CaptureCancellation(RuleCellOutcome.Error).Record, out _));
        CapturedAuditRecord cancelledCompletion = CaptureCancellation(RuleCellOutcome.NotEvaluated);
        await PublishAndAwaitExecutionReceipt(fixture, cancelledStart);
        await PublishAndAwaitExecutionReceipt(fixture, cancelledCompletion);
        JsonElement cancelledDetail = await Get(actor, ExecutionDetailsRoute(application, cancelled.ExecutionId));
        AssertExecution(cancelledDetail.GetProperty("execution"), cancelled, "Cancelled", true, true);
        Assert.AreEqual(ExactAuditJson(cancelledCompletion), ReadText(cancelledDetail, "completionAuditJson"));

        var empty = facts with
        {
            ExecutionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        var nonMatchingInputs = new RuleInputSnapshot([new("country", RuleValue.FromString("ITA"))]);
        RuleEvaluationTrace emptyTrace = RuleMatcher.Evaluate(prepared, nonMatchingInputs);
        Assert.AreEqual(RuleExecutionStatus.Completed, emptyTrace.Status);
        Assert.IsEmpty(emptyTrace.SelectedRowIndices);
        CapturedAuditRecord emptyStart = CaptureExecutionStart(capture, empty, nonMatchingInputs);
        DateTimeOffset emptyFinished = DateTimeOffset.UtcNow;
        CapturedAuditRecord emptyCompletion = capture.CaptureLocalSystem(RuleExecutionAudit.Complete,
            new RuleExecutionAuditCompletion(empty, emptyTrace, emptyFinished, null),
            RuleExecutionAudit.CaptureOptions(empty, AuditOutcome.Succeeded, emptyFinished, emptyStart.AuditId));
        await PublishAndAwaitExecutionReceipt(fixture, emptyStart);
        await PublishAndAwaitExecutionReceipt(fixture, emptyCompletion);
        JsonElement emptyDetail = await Get(actor, ExecutionDetailsRoute(application, empty.ExecutionId));
        AssertExecution(emptyDetail.GetProperty("execution"), empty, "Completed", true, true, returnedMatchCount: 0);
        Assert.AreEqual(ExactAuditJson(emptyCompletion), ReadText(emptyDetail, "completionAuditJson"));
        JsonElement completed = (await Get(reader, listRoute + "&status=Completed")).GetProperty("items");
        Assert.AreEqual(2, completed.GetArrayLength());
        AssertExecution(completed[0], empty, "Completed", true, true, returnedMatchCount: 0);
        AssertExecution(completed[1], facts, "Completed", true, true, returnedMatchCount: 1);
    }

    [TestMethod]
    public async Task RegressedProducerUtcPreservesDurableCompletionAndProjectsHit()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: RuleDeployment(), ruleAuditManagedApplication: "managed-a");
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA,
            secondEditor: false, deployment: true, executionHistory: true, counters: true));

        string application = fixture.ApplicationA;
        HttpClient actor = fixture.Client("operator-a");
        HttpClient workload = fixture.Client("workload-a");
        await Apply(actor, await Catalog(actor), "clock-regression-catalog");
        string registration = await Register(workload);
        string version = await SaveCountry(actor, application, "clock-regression-save", "MLT");
        await DeployAccepted(actor, application, "clock-regression-deploy", (await Assignment(actor, application)).ETag, version);
        string definition = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + version), "definitionJson");
        PreparedRuleDefinition prepared = RuleDefinitionValidator.Prepare(RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(definition)));
        ManagementConfigurationDesiredContext desired = await ReadWorkloadDesired(workload, registration);
        var target = new ConfigurationSnapshotTarget(desired.AuthorityId, desired.ApplicationId, desired.Environment,
            desired.RegistrationId, desired.HostApplicationName, desired.CohortId);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset finishedAt = startedAt.AddMinutes(-2);
        var clock = new ManualTimeProvider(startedAt);
        const string correlation = "clock-regression";

        await RuleEngineAuditIntegrationTests.WithNativeAudit(async (source, capture, writer, limits) =>
        {
            var probe = new RuleEngineAuditIntegrationTests.AuditProbe(writer, afterSubmit: (record, result) =>
            {
                Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted, result.Outcome);
                Assert.IsNotNull(result.Receipt);
                if (record.Record.EventType == RuleEngineAuditEvents.ExecutionStarted)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(5));
                    clock.SetUtcNow(finishedAt);
                }
            });
            var evaluator = new RuleEvaluator((_, _) => Task.FromResult(new RuleEngineSelection(prepared, target)),
                capture, probe, limits, clock);
            var inputs = new RuleInputSnapshot([new("country", RuleValue.FromString("MLT"))]);
            RuleEvaluationResult execution = await evaluator.EvaluateAsync(new(RulesetId, inputs, version, correlation));
            Assert.AreEqual(RuleExecutionStatus.Completed, execution.Status);
            Assert.HasCount(1, execution.Matches);
            Assert.AreEqual(TimeSpan.FromMilliseconds(5), clock.GetElapsedTime(0));
            Assert.HasCount(2, probe.Attempts);
            Assert.AreEqual(2L, await source.CountAsync("outbox"));
            RuleEngineAuditIntegrationTests.AuditAttempt start = probe.Attempts[0];
            RuleEngineAuditIntegrationTests.AuditAttempt completion = probe.Attempts[1];
            Assert.AreEqual(RuleEngineAuditEvents.ExecutionStarted, start.Record.EventType);
            Assert.AreEqual(RuleEngineAuditEvents.ExecutionCompleted, completion.Record.EventType);
            Assert.AreEqual(start.Record.AuditId, completion.Record.CausationAuditId);
            Assert.AreEqual(startedAt, start.Record.OccurredAtUtc);
            Assert.AreEqual(finishedAt, completion.Record.OccurredAtUtc);

            foreach (RuleEngineAuditIntegrationTests.AuditAttempt attempt in probe.Attempts)
            {
                MessageStageReceipt receipt = attempt.Submission.Receipt!;
                byte[] wire = await AuditNativeTestSupport.ReadStoredWireAsync(source, receipt.RecordId);
                Assert.IsTrue(MessageWire.TryDecode(wire, out EncodedMessage? message, out _));
                Assert.IsNotNull(message);
                Assert.AreEqual(receipt.MessageId, message.Envelope.MessageId);
                CollectionAssert.AreEqual(attempt.Record.OriginalBytes.ToArray(), message.Envelope.CopyPayload());
                await fixture.PublishRuleAuditAsync(message);
                await AwaitExecutionReceipt(fixture, receipt.MessageId, trusted: true);
            }

            JsonElement detail = await Get(actor, ExecutionDetailsRoute(application, execution.ExecutionId));
            var facts = new RuleExecutionAuditFacts(execution.ExecutionId, target, LocalApplicationIdentity.Capture(), RulesetId,
                SchemaRevision, version, correlation, startedAt);
            JsonElement summary = detail.GetProperty("execution");
            AssertExecution(summary, facts, "Completed", true, true, returnedMatchCount: 1);
            Assert.AreEqual(finishedAt, summary.GetProperty("finishedAtUtc").GetDateTimeOffset());
            Assert.AreEqual(Encoding.UTF8.GetString(start.Record.OriginalBytes.Span), ReadText(detail, "startAuditJson"));
            Assert.AreEqual(Encoding.UTF8.GetString(completion.Record.OriginalBytes.Span), ReadText(detail, "completionAuditJson"));
            await RabbitMqMessageFixture.UntilAsync(async () =>
            {
                JsonElement counters = await Get(actor, RuleRoute(application, "counters"));
                return CounterEquals(CounterRows(counters), "stable", "1");
            }, TimeSpan.FromSeconds(20));
        }, clock: clock);
    }

    private static string ExecutionDetailsRoute(string application, Guid executionId)
        => RuleRoute(application, $"executions/{executionId:D}");

    private static CapturedAuditRecord CaptureExecutionStart(IAuditCapture capture, RuleExecutionAuditFacts facts, RuleInputSnapshot inputs)
        => capture.CaptureLocalSystem(RuleExecutionAudit.Start, new RuleExecutionAuditStart(facts, inputs),
            RuleExecutionAudit.CaptureOptions(facts, AuditOutcome.Accepted, facts.StartedAtUtc));

    private static string ExactAuditJson(CapturedAuditRecord record) => Encoding.UTF8.GetString(record.OriginalBytes.Span);

    private static EncodedMessage EncodeExecutionAudit(CapturedAuditRecord record)
        => MessageWire.Encode(new MessageEnvelope(Guid.NewGuid(), MessageKind.Event, new AuditRecordCodec().Contract,
            record.Record.Producer.Application, record.Record.Producer.Environment, DateTimeOffset.UtcNow, record.OriginalBytes));

    private static async Task<(EncodedMessage Message, string InstanceId)> InterruptAfterAuditStart(RuleExecutionAuditFacts facts)
    {
        await using PostgresMessageFixture outbox = await PostgresMessageFixture.CreateAsync();
        using var directory = new PrivateContainerDirectory();
        string descriptor = Path.Combine(directory.Path, "start.json");
        string markerPath = Path.Combine(directory.Path, "start-committed.json");
        var input = new RuleEngineAuditCrashInput(RuleEngineAuditEvents.Version, outbox.Schema, facts.ExecutionId,
            facts.Target.AuthorityId, facts.Target.ApplicationId, facts.Target.RegistrationId, facts.RulesetId,
            facts.SchemaRevision, facts.Version, facts.Identity.Application, facts.Identity.Environment, facts.StartedAtUtc);
        await PrivateContainerDirectory.WriteNewAsync(descriptor, JsonSerializer.SerializeToUtf8Bytes(input));
        await PrivateContainerDirectory.WriteNewAsync(Path.Combine(directory.Path, "postgres.txt"), Encoding.UTF8.GetBytes(outbox.ConnectionString));
        var childEnvironment = new Dictionary<string, string?>
        {
            ["CORE_SERVICE_NAME"] = input.ProducerApplication,
            ["CORE_ENVIRONMENT"] = input.ProducerEnvironment
        };
        using var terminate = new CancellationTokenSource();
        Task<ProcessResult> child = ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            MessageRpcProcessFixture.Binary, "rule-engine-audit-start", descriptor], directory.Path, TimeSpan.FromSeconds(50),
            childEnvironment, maximumCapturedBytesPerStream: 65_536, cancellationToken: terminate.Token);
        RuleEngineAuditCrashMarker? marker = null;
        try
        {
            await RabbitMqMessageFixture.UntilAsync(async () =>
            {
                if (child.IsCompleted)
                {
                    ProcessResult ended = await child;
                    Assert.Fail("The owned producer exited before the durable start marker. " + ended.StandardError);
                }
                if (!File.Exists(markerPath))
                {
                    return false;
                }
                try
                {
                    marker = JsonSerializer.Deserialize<RuleEngineAuditCrashMarker>(await File.ReadAllBytesAsync(markerPath));
                    return marker is not null;
                }
                catch (JsonException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(25));
            Assert.IsNotNull(marker);
            Assert.AreNotEqual(facts.Identity.InstanceId, marker.InstanceId);
            Assert.AreEqual(1L, await outbox.CountAsync("outbox"));
            await terminate.CancelAsync();
            ProcessTimeoutException killed = await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(async () => { _ = await child; });
            Assert.IsTrue(killed.CleanupCompleted && killed.Result.OutputDrained);
            Assert.AreNotEqual(0, killed.Result.ExitCode);
        }
        finally
        {
            if (!child.IsCompleted)
            {
                await terminate.CancelAsync();
                try
                {
                    _ = await child;
                }
                catch (ProcessTimeoutException error) when (error.CleanupCompleted) { }
            }
        }

        byte[] survivingWire = await AuditNativeTestSupport.ReadStoredWireAsync(outbox, marker!.RecordId);
        Assert.IsTrue(MessageWire.TryDecode(survivingWire, out EncodedMessage? message, out _));
        Assert.AreEqual(marker.MessageId, message!.Envelope.MessageId);
        Assert.AreEqual(input.ProducerApplication, message.Envelope.ProducerApplication);
        Assert.AreEqual(input.ProducerEnvironment, message.Envelope.ProducerEnvironment);
        Assert.AreEqual(1L, await outbox.CountAsync("outbox"));
        return (message, marker.InstanceId);
    }

    private static async Task PublishAndAwaitExecutionReceipt(ManagementHostFixture fixture, CapturedAuditRecord record,
        bool trusted = true, string? refusal = null)
    {
        EncodedMessage message = EncodeExecutionAudit(record);
        await fixture.PublishRuleAuditAsync(message, trusted);
        await AwaitExecutionReceipt(fixture, message.Envelope.MessageId, trusted);
        if (refusal is not null)
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"""
                SELECT safe_code FROM "{fixture.Options.Persistence.Schema}".rule_engine_execution_rejection
                WHERE audit_id=@audit AND payload_digest=@digest
                """);
            command.Parameters.AddWithValue("audit", record.AuditId);
            command.Parameters.AddWithValue("digest", SHA256.HashData(record.OriginalBytes.Span));
            Assert.AreEqual(refusal, await command.ExecuteScalarAsync());
        }
    }

    private static async Task AwaitExecutionReceipt(ManagementHostFixture fixture, Guid id, bool trusted)
    {
        string schema = fixture.Options.MessagingSchema;
        string query = trusted
            ? $"SELECT EXISTS(SELECT 1 FROM \"{schema}\".processing WHERE message_id=@id AND state=1)"
            : $"SELECT EXISTS(SELECT 1 FROM \"{schema}\".rejections WHERE message_id=@id AND safe_code='messaging.producer_untrusted')";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            while (true)
            {
                await using NpgsqlCommand command = fixture.Source.CreateCommand(query);
                command.Parameters.AddWithValue("id", id);
                if (await command.ExecuteScalarAsync(deadline.Token) is true)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Assert.Fail($"Native receipt {id:D} was missing after 25 seconds (trusted={trusted}).");
        }
    }

    private static void AssertExecution(JsonElement execution, RuleExecutionAuditFacts expected, string status, bool start, bool completion,
        string? producerInstanceId = null, int? returnedMatchCount = null)
    {
        Assert.AreEqual(expected.ExecutionId.ToString("D"), ReadText(execution, "executionId"));
        Assert.AreEqual(expected.Target.ApplicationId, ReadText(execution, "applicationId"));
        Assert.AreEqual(expected.Target.Environment, ReadText(execution, "environment"));
        Assert.AreEqual(expected.Target.RegistrationId, ReadText(execution, "registrationId"));
        Assert.AreEqual(expected.Identity.Application, ReadText(execution, "producerApplication"));
        Assert.AreEqual(producerInstanceId ?? expected.Identity.InstanceId, ReadText(execution, "instanceId"));
        Assert.AreEqual(expected.RulesetId, ReadText(execution, "rulesetId"));
        Assert.AreEqual(expected.Version, ReadText(execution, "version"));
        Assert.AreEqual(expected.SchemaRevision, ReadText(execution, "schemaRevision"));
        Assert.AreEqual(expected.StartedAtUtc, execution.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.AreEqual(status, ReadText(execution, "status"));
        Assert.AreEqual(start, execution.GetProperty("startReceived").GetBoolean());
        Assert.AreEqual(completion, execution.GetProperty("completionReceived").GetBoolean());
        if (returnedMatchCount is { } expectedCount)
        {
            Assert.AreEqual(expectedCount, execution.GetProperty("returnedMatchCount").GetInt32());
        }
        else
        {
            Assert.IsFalse(execution.TryGetProperty("returnedMatchCount", out _));
        }
    }

    private static async Task AssertExecutionHttpStatus(HttpClient client, string route, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.GetAsync(route);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
