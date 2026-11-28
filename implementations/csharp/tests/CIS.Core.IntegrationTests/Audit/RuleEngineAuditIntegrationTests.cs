using System.Text.Json;
using CIS.Audit;
using CIS.Configuration;
using CIS.Messaging;
using CIS.RuleEngine;
using CIS.Security;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[DoNotParallelize]
public sealed class RuleEngineAuditIntegrationTests
{
    private const string Ruleset = "fixture-éligibilité";
    private const string Version = "914da956-187d-4b75-8d0d-4701ed3c52f4";
    private const string Revision = "fixture-schema";
    private const string Correlation = "durable-invocation-fixture";
    private const string ExactDecimal = "9007199254740993.000000000001";
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task StartWithoutConfirmedAcceptanceWithholdsEvaluationAndNeverSubmitsCompletion(bool acknowledgementLost)
        => WithNativeAudit(async (database, capture, native, limits) =>
        {
            int before = database.Boundary.ConfirmedNativeCommits;
            if (acknowledgementLost)
            {
                database.Boundary.FailAfterNextCommit();
            }
            var probe = new AuditProbe(native);
            RuleEvaluationResult result = await Evaluator(capture, probe, limits, Definition(failAfterMatch: true), Target())
                .EvaluateAsync(Request(withInputs: true));

            Assert.AreEqual(RuleExecutionStatus.Failed, result.Status);
            Assert.AreEqual("AuditUnavailable", result.Error?.Code);
            Assert.AreEqual(acknowledgementLost ? AuditSubmissionOutcome.Unknown : AuditSubmissionOutcome.NotSubmitted, result.AuditFailure);
            Assert.IsNull(result.PriorError);
            Assert.IsEmpty(result.Matches);
            Assert.IsEmpty(result.Trace);
            Assert.HasCount(1, probe.Attempts);
            AuditAttempt attempt = probe.Attempts[0];
            Assert.AreEqual(RuleEngineAuditEvents.ExecutionStarted, attempt.Record.EventType);
            Assert.AreEqual(result.AuditFailure, attempt.Submission.Outcome);
            Assert.AreEqual(before + (acknowledgementLost ? 1 : 0), database.Boundary.ConfirmedNativeCommits);
            Assert.AreEqual(acknowledgementLost ? 1L : 0L, await database.CountAsync("outbox"));
            if (acknowledgementLost)
            {
                AuditRecord retained = await ReadDurable(database, attempt, limits);
                Assert.AreEqual(result.ExecutionId.ToString("D"), Text(Details(retained), "executionId"));
            }
            else
            {
                Assert.AreEqual("messaging.storage_capacity", attempt.Submission.Error?.Code);
            }
        }, acknowledgementLost ? null : 1L);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task EnabledFailureKeepsEarlierObservedMatchAndBothNativeAuditEvents(bool completionAcknowledgementLost)
        => WithNativeAudit(async (database, capture, native, limits) =>
        {
            int before = database.Boundary.ConfirmedNativeCommits;
            var probe = new AuditProbe(native, beforeSubmit: (record, _) =>
            {
                if (completionAcknowledgementLost && record.Record.EventType == RuleEngineAuditEvents.ExecutionCompleted)
                {
                    database.Boundary.FailAfterNextCommit();
                }
                return Task.CompletedTask;
            });
            ConfigurationSnapshotTarget target = Target();
            RuleEvaluationResult result = await Evaluator(capture, probe, limits, Definition(failAfterMatch: true), target)
                .EvaluateAsync(Request(withInputs: true));

            Assert.AreEqual(RuleExecutionStatus.Failed, result.Status);
            Assert.IsEmpty(result.Matches);
            Assert.AreEqual(completionAcknowledgementLost ? "AuditUnavailable" : "WrongInputType", result.Error?.Code);
            if (completionAcknowledgementLost)
            {
                Assert.AreEqual(AuditSubmissionOutcome.Unknown, result.AuditFailure);
                Assert.AreEqual("WrongInputType", result.PriorError?.Code);
            }
            else
            {
                Assert.IsNull(result.AuditFailure);
                Assert.IsNull(result.PriorError);
            }
            CollectionAssert.AreEqual(new[] { RuleRowOutcome.Matched, RuleRowOutcome.Error, RuleRowOutcome.NotReached },
                result.Trace.Select(row => row.Outcome).ToArray());

            Assert.AreEqual(before + 2, database.Boundary.ConfirmedNativeCommits);
            Assert.AreEqual(2L, await database.CountAsync("outbox"));
            (AuditRecord start, AuditRecord completion) = await ReadPair(database, probe, limits);
            Assert.AreEqual(completionAcknowledgementLost ? AuditSubmissionOutcome.Unknown : AuditSubmissionOutcome.OutboxCommitted,
                probe.Attempts[1].Submission.Outcome);
            JsonElement startDetails = Details(start);
            JsonElement completionDetails = Details(completion);
            AssertProvenance(start, startDetails, result, target);
            AssertProvenance(completion, completionDetails, result, target);
            Assert.AreEqual(Text(startDetails, "startedAtUtc"), Text(completionDetails, "startedAtUtc"));
            Assert.IsTrue(completion.OccurredAtUtc >= start.OccurredAtUtc);
            Assert.IsFalse(completionDetails.TryGetProperty("inputs", out _));
            AssertInputs(startDetails);

            Assert.AreEqual("Failed", Text(completionDetails, "status"));
            Assert.AreEqual("WrongInputType", Text(completionDetails, "errorCode"));
            Assert.AreEqual(0, completionDetails.GetProperty("selectedRowIndices").GetArrayLength());
            JsonElement rows = completionDetails.GetProperty("rows");
            Assert.AreEqual(3, rows.GetArrayLength());
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.EnumerateArray().Select(row => row.GetProperty("rowIndex").GetInt32()).ToArray());
            CollectionAssert.AreEqual(new[] { "Matched", "Error", "NotReached" }, rows.EnumerateArray().Select(row => Text(row, "outcome")).ToArray());
            CollectionAssert.AreEqual(new[] { "mm", "eu", "uu" }, rows.EnumerateArray().Select(row => Text(row, "cellOutcomes")).ToArray());
            Assert.AreEqual("WrongInputType", Text(rows[1], "errorCode"));
            Assert.AreEqual(0, rows[1].GetProperty("errorColumn").GetInt32());
        });

    [TestMethod]
    public Task CancellationImmediatelyAfterCommittedStartFinalizesCancelledOnIndependentToken()
        => WithNativeAudit(async (database, capture, native, limits) =>
        {
            using var caller = new CancellationTokenSource();
            var probe = new AuditProbe(native, afterSubmit: (record, submission) =>
            {
                if (record.Record.EventType == RuleEngineAuditEvents.ExecutionStarted)
                {
                    Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted, submission.Outcome);
                    caller.Cancel();
                }
            });
            RuleEvaluationResult result = await Evaluator(capture, probe, limits, Definition(), Target())
                .EvaluateAsync(Request(), caller.Token);

            Assert.AreEqual(RuleExecutionStatus.Cancelled, result.Status);
            Assert.AreEqual("Cancelled", result.Error?.Code);
            Assert.IsNull(result.AuditFailure);
            Assert.IsEmpty(result.Matches);
            Assert.IsTrue(result.Trace.All(row => row.Outcome == RuleRowOutcome.NotReached));
            Assert.AreEqual(2L, await database.CountAsync("outbox"));
            AuditRecord completion = (await ReadPair(database, probe, limits)).Completion;
            JsonElement details = Details(completion);
            Assert.AreEqual("Cancelled", Text(details, "status"));
            Assert.AreEqual("Cancelled", Text(details, "errorCode"));
            Assert.AreEqual(0, details.GetProperty("selectedRowIndices").GetArrayLength());
        });

    [TestMethod]
    public Task LateCallerCancellationDoesNotRewriteTerminalSelectionOrReleaseItBeforeSourceCommit()
        => WithNativeAudit(async (database, capture, native, limits) =>
        {
            using var caller = new CancellationTokenSource();
            var completionEntered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var probe = new AuditProbe(native, beforeSubmit: async (record, finalization) =>
            {
                if (record.Record.EventType == RuleEngineAuditEvents.ExecutionCompleted)
                {
                    completionEntered.TrySetResult(finalization);
                    await release.Task.WaitAsync(finalization);
                }
            });
            Task<RuleEvaluationResult> pending = Evaluator(capture, probe, limits, Definition(), Target()).EvaluateAsync(Request(), caller.Token);
            RuleEvaluationResult result;
            try
            {
                CancellationToken finalization = await completionEntered.Task.WaitAsync(TestDeadline);
                Assert.AreEqual(1L, await database.CountAsync("outbox"));
                caller.Cancel();
                Assert.IsTrue(finalization.CanBeCanceled);
                Assert.IsFalse(finalization.IsCancellationRequested);
                Assert.IsFalse(pending.IsCompleted);
            }
            finally
            {
                release.TrySetResult();
                result = await pending.WaitAsync(TestDeadline);
            }

            Assert.AreEqual(RuleExecutionStatus.Completed, result.Status);
            Assert.IsNull(result.Error);
            Assert.IsNull(result.AuditFailure);
            Assert.AreEqual("first", result.Matches.Single().RuleId);
            Assert.AreEqual(2L, await database.CountAsync("outbox"));
            Assert.AreEqual(0L, await database.CountAsync("processing"), "No reporting consumer is running.");
            AuditRecord completion = (await ReadPair(database, probe, limits)).Completion;
            JsonElement details = Details(completion);
            Assert.AreEqual("Completed", Text(details, "status"));
            Assert.AreEqual(0, details.GetProperty("selectedRowIndices").EnumerateArray().Single().GetInt32());
        });

    [TestMethod]
    public Task UnknownCompletionOfSuccessfulMatchRetainsAuditButWithholdsBusinessResult()
        => WithNativeAudit(async (database, capture, native, limits) =>
        {
            var probe = new AuditProbe(native, beforeSubmit: (record, _) =>
            {
                if (record.Record.EventType == RuleEngineAuditEvents.ExecutionCompleted)
                {
                    database.Boundary.FailAfterNextCommit();
                }
                return Task.CompletedTask;
            });
            RuleEvaluationResult result = await Evaluator(capture, probe, limits, Definition(), Target()).EvaluateAsync(Request());

            Assert.AreEqual(RuleExecutionStatus.Failed, result.Status);
            Assert.AreEqual("AuditUnavailable", result.Error?.Code);
            Assert.AreEqual(AuditSubmissionOutcome.Unknown, result.AuditFailure);
            Assert.IsEmpty(result.Matches);
            Assert.HasCount(1, result.Trace);
            Assert.AreEqual(RuleRowOutcome.Matched, result.Trace[0].Outcome);
            Assert.AreEqual(2L, await database.CountAsync("outbox"));
            AuditRecord completion = (await ReadPair(database, probe, limits)).Completion;
            JsonElement details = Details(completion);
            Assert.AreEqual("Completed", Text(details, "status"));
            Assert.AreEqual(0, details.GetProperty("selectedRowIndices").EnumerateArray().Single().GetInt32());
        });

    internal static async Task WithNativeAudit(Func<PostgresMessageFixture, IAuditCapture, IAuditWriter, AuditLimits, Task> test,
        long? capacity = null, TimeProvider? clock = null)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync(capacity: capacity);
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var limits = new AuditLimits();
        MessagingSnapshot snapshot = AuditNativeTestSupport.Snapshot(limits);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        await using var engine = new AuditCaptureEngine(local, limits, clock ?? TimeProvider.System, resources);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, local);
        await using var writer = new AuditWriter(new(AuditNativeTestSupport.Route, limits), local, outbox,
            new MessageSubmissionClient(outbox, database.Factory));
        writer.BeginAccepting();
        try
        {
            await test(database, new AuditCapture(engine, null), writer, limits);
        }
        finally
        {
            await outbox.StopAsync();
        }
    }

    private static RuleEvaluator Evaluator(IAuditCapture capture, IAuditWriter writer, AuditLimits limits,
        PreparedRuleDefinition definition, ConfigurationSnapshotTarget target)
        => new((_, _) => Task.FromResult(new RuleEngineSelection(definition, target)), capture, writer, limits, TimeProvider.System,
            finalizationDeadline: TestDeadline);

    private static PreparedRuleDefinition Definition(bool failAfterMatch = false)
    {
        RuleColumn[] columns =
        [
            new("country", "Country", RuleValueKind.String, [RulePresetKind.String, RulePresetKind.Any]),
            new("flag", "Flag", RuleValueKind.Boolean, [RulePresetKind.Any])
        ];
        RuleCondition countryAny = new("country", RulePresetKind.Any);
        RuleCondition flagAny = new("flag", RulePresetKind.Any);
        RuleRow[] rows = failAfterMatch
            ? [new("first", 1, RuleState.Enabled, [countryAny, flagAny]),
                new("invalid", 2, RuleState.Enabled,
                    [new("country", RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString("MLT")), flagAny]),
                new("unreached", 3, RuleState.Enabled, [countryAny, flagAny])]
            : [new("first", 1, RuleState.Enabled, [countryAny, flagAny])];
        return RuleDefinitionValidator.Prepare(new(Ruleset, Revision, Version, RuleExecutionMode.AllMatching, columns, rows));
    }

    private static RuleEvaluationRequest Request(bool withInputs = false)
    {
        Dictionary<string, RuleValue> values = new(StringComparer.Ordinal);
        if (withInputs)
        {
            using JsonDocument opaque = JsonDocument.Parse("{\"number\":9007199254740993.00}");
            values.Add("country", RuleValue.FromDecimal(ExactDecimal));
            values.Add("explicit-null", RuleValue.Null);
            values.Add("ignored-json", RuleValue.FromJson(opaque.RootElement));
            values.Add("unknown-text", RuleValue.FromString("Café 🌍"));
        }
        var request = new RuleEvaluationRequest(Ruleset, new(values), Version, Correlation);
        values.Clear();
        return request;
    }

    private static ConfigurationSnapshotTarget Target()
        => new("fb9b1c4f-e472-476a-b384-3f63878e0b18", "1112e3e2-48a9-4d79-b331-985a2cd48859", EnvironmentOptions.EnvironmentName,
            "aabf83e8-29de-408e-9c80-d22673cbd460", "NativeAuditFixture");

    private static async Task<(AuditRecord Start, AuditRecord Completion)> ReadPair(PostgresMessageFixture database, AuditProbe probe, AuditLimits limits)
    {
        Assert.HasCount(2, probe.Attempts);
        AuditRecord start = await ReadDurable(database, probe.Attempts[0], limits);
        AuditRecord completion = await ReadDurable(database, probe.Attempts[1], limits);
        Assert.AreEqual(RuleEngineAuditEvents.ExecutionStarted, start.EventType);
        Assert.AreEqual(RuleEngineAuditEvents.ExecutionCompleted, completion.EventType);
        Assert.AreNotEqual(start.AuditId, completion.AuditId);
        Assert.AreEqual(start.AuditId, completion.CausationAuditId);
        return (start, completion);
    }

    private static async Task<AuditRecord> ReadDurable(PostgresMessageFixture database, AuditAttempt attempt, AuditLimits limits)
    {
        MessageStageReceipt receipt = attempt.Submission.Receipt ?? throw new AssertFailedException("No native submission receipt is available.");
        byte[] stored = await AuditNativeTestSupport.ReadStoredWireAsync(database, receipt.RecordId);
        Assert.IsTrue(MessageWire.TryDecode(stored, out EncodedMessage? message, out _));
        Assert.AreEqual(receipt.MessageId, message!.Envelope.MessageId);
        CollectionAssert.AreEqual(attempt.Record.OriginalBytes.ToArray(), message.Envelope.CopyPayload());
        var codec = new AuditRecordCodec(limits);
        Assert.IsTrue(codec.TryDecode(message.Envelope.CopyPayload(), out AuditRecord? record, out _));
        return record!;
    }

    private static void AssertInputs(JsonElement start)
    {
        Dictionary<string, JsonElement> inputs = start.GetProperty("inputs").EnumerateArray().ToDictionary(
            entry => Text(entry, "columnId"), StringComparer.Ordinal);
        Assert.HasCount(4, inputs);
        Assert.IsFalse(inputs.ContainsKey("flag"));
        Assert.AreEqual("decimal", Text(inputs["country"], "kind"));
        Assert.AreEqual(ExactDecimal, Text(inputs["country"], "value"));
        Assert.AreEqual("null", Text(inputs["explicit-null"], "kind"));
        Assert.AreEqual("json", Text(inputs["ignored-json"], "kind"));
        Assert.AreEqual("9007199254740993.00", inputs["ignored-json"].GetProperty("value").GetProperty("number").GetRawText());
        Assert.AreEqual("Café 🌍", Text(inputs["unknown-text"], "value"));
    }

    private static void AssertProvenance(AuditRecord record, JsonElement details, RuleEvaluationResult result, ConfigurationSnapshotTarget target)
    {
        Assert.AreEqual(result.ExecutionId.ToString("D"), Text(details, "executionId"));
        Assert.AreEqual(target.AuthorityId, Text(details, "authorityId"));
        Assert.AreEqual(target.ApplicationId, Text(details, "applicationId"));
        Assert.AreEqual(target.RegistrationId, Text(details, "registrationId"));
        Assert.AreEqual(EnvironmentOptions.ApplicationName, Text(details, "application"));
        Assert.AreEqual(EnvironmentOptions.EnvironmentName, Text(details, "environment"));
        Assert.AreEqual(EnvironmentOptions.InstanceId, Text(details, "instanceId"));
        Assert.AreEqual(EnvironmentOptions.BuildVersion, Text(details, "applicationBuild"));
        Assert.AreEqual(RuleEngineAuditEvents.Version, details.GetProperty("engineContractVersion").GetInt32());
        Assert.AreEqual(Ruleset, Text(details, "rulesetId"));
        Assert.AreEqual(Revision, Text(details, "schemaRevision"));
        Assert.AreEqual(Version, Text(details, "version"));
        Assert.AreEqual(Correlation, Text(details, "correlationId"));
        Assert.AreEqual(Correlation, record.CorrelationId);
    }

    private static JsonElement Details(AuditRecord record)
    {
        using JsonDocument document = JsonDocument.Parse(record.DetailsJson);
        return document.RootElement.Clone();
    }

    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString()!;

    internal sealed record AuditAttempt(AuditRecord Record, AuditSubmissionResult Submission);

    internal sealed class AuditProbe(IAuditWriter actual, Func<CapturedAuditRecord, CancellationToken, Task>? beforeSubmit = null,
        Action<CapturedAuditRecord, AuditSubmissionResult>? afterSubmit = null) : IAuditWriter
    {
        internal List<AuditAttempt> Attempts { get; } = [];

        public Task<AuditStageResult> StageAsync(ILocalTransactionContext context, CapturedAuditRecord record,
            CancellationToken cancellationToken = default)
            => actual.StageAsync(context, record, cancellationToken);

        public async Task<AuditSubmissionResult> SubmitAsync(CapturedAuditRecord record, CancellationToken cancellationToken = default)
        {
            if (beforeSubmit is not null)
            {
                await beforeSubmit(record, cancellationToken);
            }
            AuditSubmissionResult result = await actual.SubmitAsync(record, cancellationToken);
            Attempts.Add(new(record.Record, result));
            afterSubmit?.Invoke(record, result);
            return result;
        }
    }
}
