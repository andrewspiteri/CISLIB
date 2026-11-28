using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Configuration;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using CIS.Messaging;
using CIS.RuleEngine;
using CIS.Security;
using CIS.Tests.IntegrationTests.Audit;
using CIS.Tests.IntegrationTests.Messaging;
using Npgsql;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementRuleEngineVersionTests
{
    [TestMethod]
    public async Task NativeCountersOrderFirstAcceptanceAcrossResetAndRecoverFromProjectionFailure()
    {
        const string primary = "counter-enabled";
        const string test = "counter-test";
        const string retained = "counter-retained";
        const string unknown = "counter-unknown";
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 2, start: false,
            configuration: RuleDeployment(), ruleAuditManagedApplication: "managed-a");
        for (int replica = 0; replica < 2; replica++)
        {
            await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA,
                secondEditor: false, deployment: true, executionHistory: true, counters: true));
        }

        string application = fixture.ApplicationA;
        HttpClient actor = fixture.Client("operator-a");
        HttpClient secondActor = fixture.Client("operator-a", 1);
        HttpClient reader = fixture.Client("operator-b");
        HttpClient workload = fixture.Client("workload-a");
        await Apply(actor, await Catalog(actor), "counter-catalog");
        string firstRegistration = await Register(workload);
        string secondRegistration = await Register(workload);
        RuleDefinition template = RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(DefinitionJson([])));
        var any = new RuleCondition("country", RulePresetKind.Any);
        RuleRow[] initialRows = [new(primary, 10, RuleState.Enabled, [any]), new(test, 20, RuleState.Test, [any]), Row(retained, 30, "MLT")];
        async Task<string> StoreVersion(string key, IEnumerable<RuleRow> rows, string? basis = null)
        {
            var definition = new RuleDefinition(RulesetId, SchemaRevision, "unsaved", RuleExecutionMode.AllMatching,
                template.Columns, rows, template.DisplayName);
            string encoded = Encoding.UTF8.GetString(RuleDefinitionCodec.Encode(definition));
            string revision = ReadText(await OnlyRuleset(actor, application), "editETag");
            return ReadText(await SaveAccepted(actor, application, key, revision, encoded, basis), "version");
        }

        string first = await StoreVersion("counter-save-first", initialRows);
        JsonElement current = await DeployAccepted(actor, application, "counter-deploy-first", (await Assignment(actor, application)).ETag, first);
        async Task<ConfigurationSnapshotTarget> Target(string registration)
        {
            ManagementConfigurationDesiredContext desired = await ReadWorkloadDesired(workload, registration);
            return new(desired.AuthorityId, desired.ApplicationId, desired.Environment, desired.RegistrationId,
                desired.HostApplicationName, desired.CohortId);
        }

        ConfigurationSnapshotTarget firstTarget = await Target(firstRegistration);
        ConfigurationSnapshotTarget secondTarget = await Target(secondRegistration);
        PreparedRuleDefinition prepared = RuleDefinitionValidator.Prepare(RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(
            ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + first), "definitionJson"))));
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        var limits = new AuditLimits();
        await using var resources = new MessagingResourceBudget(AuditNativeTestSupport.Snapshot(limits).Settings);
        await using var captureEngine = new AuditCaptureEngine(identity, limits, TimeProvider.System, resources);
        IAuditCapture capture = new AuditCapture(captureEngine, null);
        var goodInput = new RuleInputSnapshot([new("country", RuleValue.FromString("MLT"))]);
        var failingInput = new RuleInputSnapshot([new("country", RuleValue.FromBoolean(false))]);

        (RuleExecutionAuditFacts Facts, CapturedAuditRecord Start, CapturedAuditRecord Completion) Observe(
            ConfigurationSnapshotTarget target, RuleInputSnapshot values, RuleExecutionStatus expectedStatus)
        {
            var facts = new RuleExecutionAuditFacts(Guid.NewGuid(), target, identity, RulesetId, SchemaRevision, first,
                "counter-ordering", DateTimeOffset.UtcNow);
            CapturedAuditRecord start = CaptureExecutionStart(capture, facts, values);
            RuleEvaluationTrace trace = RuleMatcher.Evaluate(prepared, values);
            Assert.AreEqual(expectedStatus, trace.Status);
            if (expectedStatus == RuleExecutionStatus.Failed)
            {
                Assert.AreEqual(RuleRowOutcome.Matched, trace.Rows[0].Outcome);
                Assert.AreEqual(RuleRowOutcome.Matched, trace.Rows[1].Outcome);
                Assert.IsEmpty(trace.SelectedRowIndices);
            }
            DateTimeOffset finished = DateTimeOffset.UtcNow;
            AuditOutcome outcome = trace.Status == RuleExecutionStatus.Completed ? AuditOutcome.Succeeded : AuditOutcome.Failed;
            return (facts, start, capture.CaptureLocalSystem(RuleExecutionAudit.Complete,
                new RuleExecutionAuditCompletion(facts, trace, finished, null),
                RuleExecutionAudit.CaptureOptions(facts, outcome, finished, start.AuditId)));
        }

        async Task Relay(CapturedAuditRecord start, CapturedAuditRecord completion)
        {
            await PublishAndAwaitExecutionReceipt(fixture, start);
            await PublishAndAwaitExecutionReceipt(fixture, completion);
        }

        PostgreSqlManagementStore CreateProjector() => new(fixture.Transactions, fixture.Options.Persistence,
            new PostgreSqlManagementAuthority(fixture.Transactions, fixture.Options));
        PostgreSqlManagementStore projector = CreateProjector();
        async Task ProjectConcurrently()
        {
            bool[] settled = await Task.WhenAll(projector.ProjectRuleEngineCountersAsync(8, CancellationToken.None),
                CreateProjector().ProjectRuleEngineCountersAsync(8, CancellationToken.None));
            Assert.IsTrue(settled.All(result => result));
        }

        string counters = RuleRoute(application, "counters");
        async Task<JsonElement> State(string enabled, string trial, string historical)
        {
            JsonElement settled = default;
            await RabbitMqMessageFixture.UntilAsync(async () =>
            {
                settled = await Get(actor, counters);
                Dictionary<string, JsonElement> rows = CounterRows(settled);
                return CounterEquals(rows, primary, enabled) && CounterEquals(rows, test, trial) && CounterEquals(rows, retained, historical);
            }, TimeSpan.FromSeconds(20));
            return settled;
        }

        var original = Observe(firstTarget, goodInput, RuleExecutionStatus.Completed);
        await Relay(original.Start, original.Completion);
        await ProjectConcurrently();
        await State("1", "1", "1");
        JsonElement readOnly = await Get(reader, counters);
        Assert.AreEqual("fresh", ReadText(readOnly, "availability"));

        string newer = await StoreVersion("counter-save-second",
            [new(primary, 10, RuleState.Disabled, [any]), new(test, 20, RuleState.Test, [any])], first);
        current = await DeployAccepted(actor, application, "counter-deploy-second", ReadText(current, "assignmentETag"), newer);
        await State("1", "1", "1");
        var earlier = Observe(secondTarget, goodInput, RuleExecutionStatus.Completed);
        string before = (await Assignment(actor, application)).ETag;

        using (HttpResponseMessage denied = await CounterReset(reader, application, "counter-denied", "one", [primary]))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode, await denied.Content.ReadAsStringAsync());
        }
        const string one = "counter-reset-one";
        JsonElement oneResult = await ReadCounterReset(actor, application, one, "one", [primary], applied: "1", rejected: "0");
        string firstReset = ReadText(oneResult, "resetAtUtc");
        Assert.IsTrue(earlier.Facts.StartedAtUtc < DateTimeOffset.Parse(firstReset, System.Globalization.CultureInfo.InvariantCulture));
        var zero = CounterRows(await State("0", "1", "1"));
        Assert.AreEqual(firstReset, ReadText(zero[primary], "lastResetAtUtc"));
        await Relay(original.Start, original.Completion);
        await ProjectConcurrently();
        await State("0", "1", "1");
        await Relay(earlier.Start, earlier.Completion);
        await ProjectConcurrently();
        await State("1", "2", "2");

        const string selected = "counter-reset-selected";
        await ReadCounterReset(secondActor, application, selected, "selected", [test, unknown], applied: "1", rejected: "1");
        await State("1", "0", "2");
        JsonElement partial = (await Get(actor, RuleRoute(application, "counter-reset-operations/" + selected + "/outcomes"))).GetProperty("items");
        CollectionAssert.AreEquivalent(new[] { test + "=reset", unknown + "=notFound" }, partial.EnumerateArray()
            .Select(row => ReadText(row, "ruleId") + "=" + ReadText(row, "disposition")).ToArray());

        string tableSchema = fixture.Options.Persistence.Schema;
        await using (NpgsqlCommand block = fixture.Source.CreateCommand($"""
            CREATE FUNCTION "{tableSchema}".counter_projection_fault() RETURNS trigger LANGUAGE plpgsql AS
            $body$ BEGIN RAISE EXCEPTION 'Owned counter projection fault'; END; $body$;
            CREATE TRIGGER counter_projection_fault BEFORE INSERT OR UPDATE ON "{tableSchema}".rule_engine_counter
                FOR EACH ROW EXECUTE FUNCTION "{tableSchema}".counter_projection_fault();
            """))
        {
            await block.ExecuteNonQueryAsync();
        }
        var failed = Observe(secondTarget, failingInput, RuleExecutionStatus.Failed);
        try
        {
            await Relay(failed.Start, failed.Completion);
            JsonElement independent = await Get(actor, ExecutionDetailsRoute(application, failed.Facts.ExecutionId));
            AssertExecution(independent.GetProperty("execution"), failed.Facts, "Failed", true, true);
            Assert.IsFalse(await projector.ProjectRuleEngineCountersAsync(8, CancellationToken.None));
            JsonElement stale = await Get(actor, counters);
            Assert.AreEqual("stale", ReadText(stale, "availability"));
            Assert.IsTrue(CounterEquals(CounterRows(stale), primary, "1"));
        }
        finally
        {
            await using NpgsqlCommand restore = fixture.Source.CreateCommand($"""
                DROP TRIGGER counter_projection_fault ON "{tableSchema}".rule_engine_counter;
                DROP FUNCTION "{tableSchema}".counter_projection_fault()
                """);
            await restore.ExecuteNonQueryAsync();
        }
        await ProjectConcurrently();
        await State("2", "1", "2");

        const string all = "counter-reset-all";
        JsonElement allResult = await ReadCounterReset(actor, application, all, "all", [], applied: "3", rejected: "0");
        string finalReset = ReadText(allResult, "resetAtUtc");
        var allCleared = CounterRows(await State("0", "0", "0"));
        foreach (string ruleId in new[] { primary, test, retained })
        {
            Assert.AreEqual(finalReset, ReadText(allCleared[ruleId], "lastResetAtUtc"));
        }
        string outcomeRoute = RuleRoute(application, "counter-reset-operations/" + all + "/outcomes") + "&pageSize=1";
        string outcomePage = outcomeRoute;
        var resetRules = new List<string>();
        for (int index = 0; index < 3; index++)
        {
            JsonElement page = await Get(actor, outcomePage);
            JsonElement items = page.GetProperty("items");
            Assert.AreEqual(1, items.GetArrayLength());
            Assert.AreEqual("reset", ReadText(items[0], "disposition"));
            resetRules.Add(ReadText(items[0], "ruleId"));
            if (index < 2)
            {
                outcomePage = outcomeRoute + "&cursor=" + Uri.EscapeDataString(ReadText(page, "nextCursor"));
            }
            else
            {
                Assert.IsFalse(page.TryGetProperty("nextCursor", out _));
            }
        }
        CollectionAssert.AreEquivalent(new[] { primary, test, retained }, resetRules.ToArray());
        Assert.AreEqual(before, (await Assignment(actor, application)).ETag);

        await DeployAccepted(actor, application, "counter-rollback", ReadText(current, "assignmentETag"), first, intent: "rollback");
        await State("0", "0", "0");
        var after = Observe(firstTarget, goodInput, RuleExecutionStatus.Completed);
        await Relay(after.Start, after.Completion);
        await ProjectConcurrently();
        await State("1", "1", "1");
        JsonElement recovered = await Get(secondActor, RuleRoute(application, "counter-reset-operations/" + all));
        Assert.AreEqual(finalReset, ReadText(recovered.GetProperty("result"), "resetAtUtc"));
        await ReadCounterReset(actor, application, all, "all", [], applied: "3", rejected: "0");
        await State("1", "1", "1");
        await AssertNativeCounterResetAudit(fixture, actor, application, [one, selected, all]);
    }

    private static async Task AssertNativeCounterResetAudit(ManagementHostFixture fixture, HttpClient actor, string application,
        string[] originalKeys)
    {
        string resource;
        await using (NpgsqlCommand key = fixture.Source.CreateCommand($"""
            SELECT ruleset_key::text FROM "{fixture.Options.Persistence.Schema}".rule_engine_ruleset
            WHERE application_id=@app AND ruleset_id=@ruleset
            """))
        {
            key.Parameters.AddWithValue("app", Guid.Parse(application));
            key.Parameters.AddWithValue("ruleset", RulesetId);
            resource = (string)(await key.ExecuteScalarAsync())!;
        }
        var audits = new List<AuditRecord>();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT envelope FROM "{fixture.Options.MessagingSchema}".outbox WHERE route_alias='management-audit' AND purpose=0
            ORDER BY created_at,record_id LIMIT 65
            """))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            var codec = new AuditRecordCodec();
            int inspected = 0;
            while (await reader.ReadAsync())
            {
                Assert.IsLessThan(65, ++inspected);
                Assert.IsTrue(MessageWire.TryDecode(reader.GetFieldValue<byte[]>(0), out EncodedMessage? envelope, out _));
                Assert.IsTrue(codec.TryDecode(envelope!.Envelope.Contract, envelope.Envelope.CopyPayload(), out AuditRecord? record));
                if (record!.EventType == "cis.management.rule-engine.counter.reset")
                {
                    audits.Add(record);
                }
            }
        }
        CollectionAssert.AreEquivalent(originalKeys, audits.Select(audit => audit.OperationReference!.Key).ToArray());
        foreach (AuditRecord audit in audits)
        {
            AuditOperationReference operation = audit.OperationReference!;
            JsonElement original = await Get(actor, RuleRoute(application, "counter-reset-operations/" + operation.Key));
            JsonElement originalOperation = original.GetProperty("operation");
            JsonElement decision = original.GetProperty("result");
            Assert.AreEqual(1, audit.EventVersion);
            Assert.AreEqual(AuditCategory.Administration, audit.Category);
            Assert.AreEqual(AuditOutcome.Succeeded, audit.Outcome);
            Assert.AreEqual(AuditActorKind.User, audit.Actor.Kind);
            Assert.AreEqual(ManagementHostFixture.Issuer, audit.Actor.Subject!.Issuer);
            Assert.AreEqual("operator-a", audit.Actor.Subject.Identifier);
            Assert.AreEqual(ReadText(originalOperation, "name"), operation.OperationName);
            Assert.AreEqual(ReadText(originalOperation, "scope"), operation.Scope);
            Assert.AreEqual(ReadText(originalOperation, "key"), operation.Key);
            Assert.AreEqual(EnvironmentOptions.ApplicationName, audit.Target!.Application);
            Assert.AreEqual(EnvironmentOptions.EnvironmentName, audit.Target.Environment);
            Assert.AreEqual("management.rule-engine.ruleset", audit.Target.ResourceType);
            Assert.AreEqual(resource, audit.Target.ResourceIdentifier);
            using JsonDocument details = JsonDocument.Parse(audit.DetailsJson);
            Assert.AreEqual(resource, ReadText(details.RootElement, "resourceId"));
            Assert.AreEqual(RulesetId, ReadText(details.RootElement, "rulesetId"));
            Assert.AreEqual(ReadText(decision, "selection"), ReadText(details.RootElement, "selection"));
            Assert.AreEqual(ReadText(decision, "resetAtUtc"), ReadText(details.RootElement.GetProperty("decision"), "resetAtUtc"));
        }
    }

    private static Dictionary<string, JsonElement> CounterRows(JsonElement page)
        => page.GetProperty("items").EnumerateArray().ToDictionary(row => ReadText(row, "ruleId"), StringComparer.Ordinal);

    private static bool CounterEquals(Dictionary<string, JsonElement> rows, string ruleId, string count)
        => rows.TryGetValue(ruleId, out JsonElement row) && row.TryGetProperty("count", out JsonElement observed) && observed.GetString() == count;

    private static async Task<HttpResponseMessage> CounterReset(HttpClient actor, string application, string key, string selection, string[] ruleIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RuleRoute(application, "counter-resets"))
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { selection, ruleIds, reason = "Native counter verification" }))
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Content.Headers.ContentType = new("application/json");
        return await actor.SendAsync(request);
    }

    private static async Task<JsonElement> ReadCounterReset(HttpClient actor, string application, string key, string selection,
        string[] ruleIds, string applied, string rejected)
    {
        using HttpResponseMessage response = await CounterReset(actor, application, key, selection, ruleIds);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using JsonDocument captured = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(key, ReadText(captured.RootElement.GetProperty("operation"), "key"));
        JsonElement result = captured.RootElement.GetProperty("result").Clone();
        Assert.AreEqual(selection, ReadText(result, "selection"));
        Assert.AreEqual(applied, ReadText(result, "appliedCount"));
        Assert.AreEqual(rejected, ReadText(result, "rejectedCount"));
        return result;
    }
}
