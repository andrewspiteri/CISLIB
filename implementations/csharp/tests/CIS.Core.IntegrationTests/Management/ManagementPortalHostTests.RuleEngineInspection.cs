using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Configuration;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Messaging;
using CIS.RuleEngine;
using CIS.Security;
using CIS.Tests.IntegrationTests.Audit;
using CIS.Tests.IntegrationTests.Messaging;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private sealed record RuleInspectionSeed(Guid CompletedId, Guid EmptyId, Guid FailedId, Guid CancelledId,
        Guid IncompleteId, string InstanceId, string HistoricalVersion, string ExpectedDecimal, string ExpectedMetadata,
        string StartedFromUtc, string StartedThroughUtc);

    [TestMethod]
    public async Task ActualRuleGridCounterAndInspectionJourneysKeepAuthorizedNativeEvidence()
        => await RunFixtureAsync(browser: true, ruleEngineWorkflow: true, ruleEngineInspection: true);

    private static string PortalHistoricalInspectionDefinition()
    {
        var input = new RuleCondition("residence", RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString("ITA"));
        var pensioner = new RuleCondition("pensioner", RulePresetKind.Any);
        var anyIncome = new RuleCondition("income", RulePresetKind.Any);
        var income = new RuleCondition("income", RulePresetKind.Decimal, RuleOperator.GreaterThanOrEqual,
            RuleValue.FromDecimal(PortalExactDecimal));
        var missing = new RuleCondition("referral", RulePresetKind.Missing);
        using JsonDocument exactMetadata = JsonDocument.Parse(PortalExactMetadata);
        using JsonDocument basicMetadata = JsonDocument.Parse("""{"action":"Notify"}""");
        return PortalRuleDefinition([
            new("eligibility-primary", 10, RuleState.Enabled, [input, pensioner, anyIncome, missing], "0.6500", basicMetadata.RootElement),
            new("eligibility-secondary", 20, RuleState.Test, [input, pensioner, anyIncome, missing], "0.5000"),
            new("historical-eligibility", 30, RuleState.Enabled, [input, pensioner, income, missing], "1.2500", exactMetadata.RootElement)
        ], RuleExecutionMode.AllMatching);
    }

    private static IEnumerable<CapabilityGrant> PortalRuleInspectionGrants(string issuer, string subject, string app, bool write)
    {
        yield return MetadataGrant(issuer, subject, app, "management.rule-engine.executions.read");
        yield return MetadataGrant(issuer, subject, app, "management.rule-engine.counters.read");
        if (write)
        {
            yield return MetadataGrant(issuer, subject, app, "management.rule-engine.executions.detail.read");
            yield return MetadataGrant(issuer, subject, app, "management.rule-engine.counters.reset");
            // The existing WORK-011 rule grant already gives exact original operation recovery.
        }
    }

    private static async Task<RuleInspectionSeed> SeedPortalRuleInspectionAsync(ManagementHostFixture fixture, HttpClient actor,
        RulePortalSeed baseSeed)
    {
        string application = fixture.ApplicationA;
        string registration = Guid.NewGuid().ToString("D");
        HttpClient workload = fixture.Client("workload-a");
        using (HttpResponseMessage enrolled = await ManagementServiceTests.Put(workload, "registrations/" + registration,
            ManagementHostFixture.Request("rule-inspection-provider")))
        {
            Assert.AreEqual(HttpStatusCode.OK, enrolled.StatusCode, await enrolled.Content.ReadAsStringAsync());
        }
        using HttpResponseMessage loaded = await workload.GetAsync(
            "registrations/" + registration + "/configuration/" + PortalRuleConfiguration + "?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, loaded.StatusCode, await loaded.Content.ReadAsStringAsync());
        ManagementConfigurationDesiredContext desired = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSnapshot>(
            await loaded.Content.ReadAsByteArrayAsync())!.Desired;
        var target = new ConfigurationSnapshotTarget(desired.AuthorityId, desired.ApplicationId, desired.Environment,
            desired.RegistrationId, desired.HostApplicationName, desired.CohortId);
        string historicalJson = (await ReadRuleFixtureJson(actor,
            PortalRulePath(application, "version") + "&version=" + baseSeed.HistoricalVersion)).GetProperty("definitionJson").GetString()!;
        PreparedRuleDefinition prepared = RuleDefinitionValidator.Prepare(RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(historicalJson)));
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        var limits = new AuditLimits();
        await using var resources = new MessagingResourceBudget(AuditNativeTestSupport.Snapshot(limits).Settings);
        await using var captureEngine = new AuditCaptureEngine(identity, limits, TimeProvider.System, resources);
        IAuditCapture capture = new AuditCapture(captureEngine, null);
        var matching = new RuleInputSnapshot([new("residence", RuleValue.FromString("ITA")), new("pensioner", RuleValue.FromBoolean(true)),
            new("income", RuleValue.FromDecimal(PortalExactDecimal)), new("extra", RuleValue.Null)]);
        var empty = new RuleInputSnapshot([new("residence", RuleValue.FromString("MLT"))]);
        var failed = new RuleInputSnapshot([new("residence", RuleValue.FromString("ITA")), new("income", RuleValue.FromString("wrong-kind"))]);
        DateTimeOffset firstStart = DateTimeOffset.UtcNow.AddMinutes(-2);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        async Task<Guid> PublishExecution(RuleInputSnapshot values, int seconds, RuleExecutionStatus? status, int? matches = null)
        {
            var occurrence = new RuleExecutionAuditFacts(Guid.NewGuid(), target, identity, PortalRuleset, PortalRuleSchema,
                baseSeed.HistoricalVersion, "portal-execution-inspection", firstStart.AddSeconds(seconds));
            CapturedAuditRecord start = capture.CaptureLocalSystem(RuleExecutionAudit.Start, new RuleExecutionAuditStart(occurrence, values),
                RuleExecutionAudit.CaptureOptions(occurrence, AuditOutcome.Accepted, occurrence.StartedAtUtc));
            await fixture.PublishRuleAuditAsync(EncodePortalAudit(start));
            if (status is { } expected)
            {
                RuleEvaluationTrace trace = RuleMatcher.Evaluate(prepared, values,
                    expected == RuleExecutionStatus.Cancelled ? cancel.Token : CancellationToken.None);
                Assert.AreEqual(expected, trace.Status);
                Assert.AreEqual(matches ?? 0, trace.SelectedRowIndices.Count);
                DateTimeOffset reportedFinished = occurrence.StartedAtUtc.AddMilliseconds(100);
                AuditOutcome outcome = expected switch
                {
                    RuleExecutionStatus.Completed => AuditOutcome.Succeeded,
                    RuleExecutionStatus.Failed => AuditOutcome.Failed,
                    _ => AuditOutcome.Canceled
                };
                CapturedAuditRecord completion = capture.CaptureLocalSystem(RuleExecutionAudit.Complete,
                    new RuleExecutionAuditCompletion(occurrence, trace, reportedFinished, null),
                    RuleExecutionAudit.CaptureOptions(occurrence, outcome, reportedFinished, start.AuditId));
                await fixture.PublishRuleAuditAsync(EncodePortalAudit(completion));
            }
            return occurrence.ExecutionId;
        }

        Guid completed = await PublishExecution(matching, 1, RuleExecutionStatus.Completed, matches: 2);
        Guid noMatches = await PublishExecution(empty, 2, RuleExecutionStatus.Completed, matches: 0);
        Guid toldFailed = await PublishExecution(failed, 3, RuleExecutionStatus.Failed);
        Guid cancelled = await PublishExecution(matching, 4, RuleExecutionStatus.Cancelled);
        Guid incomplete = await PublishExecution(matching, 5, null);
        await RabbitMqMessageFixture.UntilAsync(async () =>
        {
            JsonElement items = (await ReadRuleFixtureJson(actor, PortalRulePath(application, "executions"))).GetProperty("items");
            return items.EnumerateArray().Count(row => row.GetProperty("startReceived").GetBoolean()) == 5
                && items.EnumerateArray().Count(row => row.GetProperty("completionReceived").GetBoolean()) == 4;
        }, TimeSpan.FromSeconds(20));

        var projector = new PostgreSqlManagementStore(fixture.Transactions, fixture.Options.Persistence,
            new PostgreSqlManagementAuthority(fixture.Transactions, fixture.Options));
        Assert.IsTrue(await projector.ProjectRuleEngineCountersAsync(16, CancellationToken.None));
        JsonElement projected = await ReadRuleFixtureJson(actor, PortalRulePath(application, "counters"));
        Assert.AreEqual("fresh", projected.GetProperty("availability").GetString());
        Dictionary<string, string?> counts = projected.GetProperty("items").EnumerateArray()
            .ToDictionary(row => row.GetProperty("ruleId").GetString()!, row => row.GetProperty("count").GetString(), StringComparer.Ordinal);
        Assert.AreEqual("2", counts["eligibility-primary"]);
        Assert.AreEqual("2", counts["eligibility-secondary"]);
        Assert.AreEqual("1", counts["historical-eligibility"]);
        return new(completed, noMatches, toldFailed, cancelled, incomplete, identity.InstanceId, baseSeed.HistoricalVersion,
            PortalExactDecimal, PortalExactMetadata, BrowserHistoryUtc(firstStart), BrowserHistoryUtc(firstStart.AddSeconds(6)));
    }

    private static async Task VerifyPortalRuleInspectionAsync(ManagementHostFixture fixture, HttpClient actor,
        RuleInspectionSeed seed, JsonElement proof)
    {
        string application = fixture.ApplicationA;
        string[] keyProperties = ["oneResetKey", "selectedResetKey", "allResetKey"];
        string[] selections = ["one", "selected", "all"];
        var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
        JsonElement resetTimes = proof.GetProperty("resetAt");
        Assert.AreEqual(selections.Length, resetTimes.GetArrayLength());
        for (int index = 0; index < selections.Length; index++)
        {
            string key = proof.GetProperty(keyProperties[index]).GetString()!;
            Assert.IsTrue(uniqueKeys.Add(key));
            JsonElement recovered = await ReadRuleFixtureJson(actor,
                PortalRulePath(application, "counter-reset-operations/" + Uri.EscapeDataString(key)));
            Assert.AreEqual(key, recovered.GetProperty("operation").GetProperty("key").GetString());
            JsonElement result = recovered.GetProperty("result");
            Assert.AreEqual(selections[index], result.GetProperty("selection").GetString());
            Assert.AreEqual(resetTimes[index].GetString(), result.GetProperty("resetAtUtc").GetString());
        }

        JsonElement counters = (await ReadRuleFixtureJson(actor, PortalRulePath(application, "counters"))).GetProperty("items");
        Dictionary<string, JsonElement> observed = counters.EnumerateArray()
            .ToDictionary(row => row.GetProperty("ruleId").GetString()!, StringComparer.Ordinal);
        foreach (string ruleId in new[] { "eligibility-primary", "eligibility-secondary", "historical-eligibility" })
        {
            Assert.AreEqual("0", observed[ruleId].GetProperty("count").GetString());
            Assert.AreEqual(resetTimes[2].GetString(), observed[ruleId].GetProperty("lastResetAtUtc").GetString());
        }

        JsonElement completed = await ReadRuleFixtureJson(actor, PortalRulePath(application, "executions/" + seed.CompletedId.ToString("D")));
        Assert.AreEqual(seed.HistoricalVersion, completed.GetProperty("execution").GetProperty("version").GetString());
        Assert.AreEqual(2, completed.GetProperty("execution").GetProperty("returnedMatchCount").GetInt32());
        using JsonDocument start = JsonDocument.Parse(completed.GetProperty("startAuditJson").GetString()!);
        JsonElement inputs = start.RootElement.GetProperty("details").GetProperty("inputs");
        Dictionary<string, JsonElement> supplied = inputs.EnumerateArray()
            .ToDictionary(value => value.GetProperty("columnId").GetString()!, StringComparer.Ordinal);
        Assert.AreEqual(seed.ExpectedDecimal, supplied["income"].GetProperty("value").GetString());
        Assert.AreEqual("null", supplied["extra"].GetProperty("kind").GetString());
        Assert.IsFalse(supplied.ContainsKey("referral"));
        using JsonDocument completion = JsonDocument.Parse(completed.GetProperty("completionAuditJson").GetString()!);
        CollectionAssert.AreEqual(new[] { 2, 0 }, completion.RootElement.GetProperty("details").GetProperty("selectedRowIndices")
            .EnumerateArray().Select(index => index.GetInt32()).ToArray());
        RuleDefinition historical = RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(completed.GetProperty("definitionJson").GetString()!));
        Assert.AreEqual(seed.ExpectedMetadata, historical.Rows.Single(row => row.RuleId == "historical-eligibility").Metadata?.GetRawText());
    }

    private static EncodedMessage EncodePortalAudit(CapturedAuditRecord record)
        => MessageWire.Encode(new MessageEnvelope(Guid.NewGuid(), MessageKind.Event, new AuditRecordCodec().Contract,
            record.Record.Producer.Application, record.Record.Producer.Environment, DateTimeOffset.UtcNow, record.OriginalBytes));

    private static string BrowserHistoryUtc(DateTimeOffset time)
        => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
