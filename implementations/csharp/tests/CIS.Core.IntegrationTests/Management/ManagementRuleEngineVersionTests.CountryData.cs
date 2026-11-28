using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Examples.CountryData;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Messaging;
using CIS.RuleEngine;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Tests.MessagingCrashFixture;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Bootstrap = CIS.Tests.IntegrationTests.Management.ManagementConfigurationBootstrapTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementRuleEngineVersionTests
{
    [TestMethod]
    public async Task CountryDataProcessesKeepTheirCapturedModesInputsAndNativeExecutionHistory()
    {
        const string high = "country-high";
        const string primary = "country-primary";
        const string trial = "country-trial";
        const string metadata = "Expanded";
        await using RestHttpLocalServer countries = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""
                {"error":false,"data":[{"name":"Malta","Iso2":"MT","Iso3":"MLT"},{"name":"Italy","Iso2":"IT","Iso3":"ITA"}]}
                """, context.RequestAborted);
        });
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: CountryRuleDeployment(), ruleAuditManagedApplication: "managed-a");
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(Bootstrap.WorkloadServer(selected),
            fixture.ApplicationA, secondEditor: false, deployment: true, executionHistory: true));
        HttpClient actor = fixture.Client("operator-a");
        string application = fixture.ApplicationA;
        await Apply(actor, await Catalog(actor), "country-rm11-catalog");

        var candidateCountries = new RuleCondition(CountryResidenceRules.ResidenceCountry, RulePresetKind.StringList,
            RuleOperator.IsIn, RuleValue.FromStringList(["MLT", "ITA"]));
        var eitherPensioner = new RuleCondition(CountryResidenceRules.Pensioner, RulePresetKind.Any);
        var notPensioner = new RuleCondition(CountryResidenceRules.Pensioner, RulePresetKind.Boolean,
            RuleOperator.Equals, RuleValue.FromBoolean(false));
        using JsonDocument exact = JsonDocument.Parse(JsonSerializer.Serialize(metadata));
        using JsonDocument local = JsonDocument.Parse("\"Local\"");
        using JsonDocument trialMetadata = JsonDocument.Parse("\"Trial\"");
        RuleRow[] rows =
        [
            new(trial, 5, RuleState.Test, [candidateCountries, eitherPensioner], "0.5000", trialMetadata.RootElement),
            new(primary, 10, RuleState.Enabled, [candidateCountries, eitherPensioner], "0.6500", local.RootElement),
            new(high, 20, RuleState.Enabled, [candidateCountries, notPensioner], "1.2500", exact.RootElement)
        ];
        string first = await SaveCountryExample(actor, application, "country-rm11-first",
            CountryResidenceRules.CreateDraft(RuleExecutionMode.FirstMatch, rows));
        JsonElement firstDeployment = await DeployCountryExample(actor, application, "country-rm11-deploy-first", first);
        string second = await SaveCountryExample(actor, application, "country-rm11-all",
            CountryResidenceRules.CreateDraft(RuleExecutionMode.AllMatching, rows), first);
        string desiredRoute = CountryRuleRoute(application, "deployment");
        Assert.AreEqual(first, ReadText((await Get(actor, desiredRoute)).GetProperty("desired"), "version"));

        await using (CountryRuleChild initial = await CountryRuleChild.StartAsync(fixture, first, ReadText(firstDeployment.GetProperty("desired"), "releaseId"),
            false, "MLT", true, countries.Address, true))
        {
            RuleEngineCountryDataStarted initialStart = await initial.StartedAsync();
            Assert.AreEqual(first, initialStart.Version);
            JsonElement secondDeployment = await DeployCountryExample(actor, application, "country-rm11-deploy-all", second,
                ReadText(firstDeployment, "assignmentETag"));
            await using CountryRuleChild updated = await CountryRuleChild.StartAsync(fixture, second,
                ReadText(secondDeployment.GetProperty("desired"), "releaseId"), false, "ITA", false, countries.Address, true);
            RuleEngineCountryDataStarted laterStart = await updated.StartedAsync();
            Assert.AreEqual(second, laterStart.Version);
            Assert.AreNotEqual(initialStart.ExecutionId, laterStart.ExecutionId);
            await Task.WhenAll(initial.ReleaseAsync(), updated.ReleaseAsync());
            RuleEngineCountryDataResult[] results = await Task.WhenAll(initial.ReadResultAsync(), updated.ReadResultAsync());
            RuleEngineCountryDataResult one = results[0];
            RuleEngineCountryDataResult all = results[1];
            Assert.AreEqual(initialStart.ExecutionId, one.ExecutionId);
            Assert.AreEqual(laterStart.ExecutionId, all.ExecutionId);
            Assert.AreNotEqual(one.ProcessId, all.ProcessId);
            Assert.AreNotEqual(one.InstanceId, all.InstanceId);
            Assert.AreNotEqual(one.RegistrationId, all.RegistrationId);
            CollectionAssert.AreEqual(new[] { primary }, one.Matches.Select(row => row.RuleId).ToArray());
            Assert.AreEqual("NotReached", one.Trace[2].Outcome);
            CollectionAssert.AreEqual(new[] { high, primary }, all.Matches.Select(row => row.RuleId).ToArray());
            Assert.AreEqual("1.2500", all.Matches[0].Weight);
            Assert.AreEqual(metadata, all.Matches[0].Metadata);
            Assert.AreEqual("Matched", all.Trace[0].Outcome);
            Assert.AreEqual("Test", all.Trace[0].State);
            await AssertCountryHistory(actor, application, initial, one, first, "MLT", true, 1);
            await AssertCountryHistory(actor, application, updated, all, second, "ITA", false, 2);
        }

        string testVersion = await SaveCountryExample(actor, application, "country-rm11-test-only",
            CountryResidenceRules.CreateDraft(RuleExecutionMode.AllMatching, [rows[0]]), second);
        JsonElement testDeployment = await DeployCountryExample(actor, application, "country-rm11-deploy-test-only", testVersion);
        await using CountryRuleChild testOnly = await CountryRuleChild.StartAsync(fixture, testVersion,
            ReadText(testDeployment.GetProperty("desired"), "releaseId"), true, "MLT", true, countries.Address, false);
        RuleEngineCountryDataResult empty = await testOnly.ReadResultAsync();
        Assert.IsEmpty(empty.Matches);
        Assert.AreEqual("Matched", empty.Trace.Single().Outcome);
        Assert.AreEqual("Test", empty.Trace.Single().State);
        await AssertCountryHistory(actor, application, testOnly, empty, testVersion, "MLT", true, 0);
    }

    private static ManagementConfigurationOptions CountryRuleDeployment()
    {
        string configuration = CountryResidenceRules.ConfigurationId;
        string schema = CountryResidenceRules.SchemaVersion;
        string draft = Encoding.UTF8.GetString(RuleDefinitionCodec.Encode(
            CountryResidenceRules.CreateSchema()));
        var carrier = new ManagementConfigurationShape("object", false,
            [new("definitionChunks", true, new("array", false, Element: new("string", false)))]);
        var manifest = new ManagementConfigurationManifest(1, [new(configuration, schema, carrier, """{"definitionChunks":[]}""")],
            [new("managed-a", "Development", typeof(CountryApplication).Assembly.GetName().Name!, configuration, schema)], [], [])
        {
            RuleEngine = [new("managed-a", "Development", configuration, draft)]
        };
        return new(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), 16L * 1024 * 1024);
    }

    private static string CountryRuleRoute(string app, string path)
        => $"applications/{app}/rule-engine/{path}?rulesetId={Uri.EscapeDataString(CountryResidenceRules.RulesetId)}";

    private static async Task<string> SaveCountryExample(HttpClient actor, string app, string key, RuleDefinition draft, string? basis = null)
    {
        JsonElement sets = await Get(actor, $"applications/{app}/rule-engine/rulesets");
        string revision = ReadText(sets.GetProperty("items").EnumerateArray().Single(), "editETag");
        var command = new ManagementSaveRuleVersionCommand(CountryResidenceRules.SchemaRevision,
            Encoding.UTF8.GetString(RuleDefinitionCodec.Encode(draft)), "CountryData example operator save", basis);
        using HttpResponseMessage response = await NativeWrite(actor, HttpMethod.Post, CountryRuleRoute(app, "versions"), key, revision,
            ManagementRuleEngineWire.EncodeRequest(command));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return ReadText((await Read(response)).GetProperty("result"), "version");
    }

    private static async Task<JsonElement> DeployCountryExample(HttpClient actor, string app, string key, string version, string? revision = null)
    {
        revision ??= ReadText(await Get(actor, CountryRuleRoute(app, "deployment")), "assignmentETag");
        using HttpResponseMessage response = await NativeWrite(actor, HttpMethod.Post, CountryRuleRoute(app, "deployments"), key, revision,
            ManagementRuleEngineWire.EncodeRequest(new ManagementRequestRuleDeploymentCommand(version, "deploy", "CountryData explicit deployment")));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Read(response)).GetProperty("result");
    }

    private static async Task AssertCountryHistory(HttpClient actor, string application, CountryRuleChild child,
        RuleEngineCountryDataResult result, string version, string country, bool pensioner, int count)
    {
        Assert.AreEqual(CountryResidenceRules.RulesetId, result.RulesetId);
        Assert.AreEqual(version, result.Version);
        Assert.AreEqual("Completed", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreNotEqual(Guid.Empty, result.ExecutionId);
        string route = CountryRuleRoute(application, "executions/" + result.ExecutionId.ToString("D"));
        JsonElement detail = default;
        await RabbitMqMessageFixture.UntilAsync(async () =>
        {
            using HttpResponseMessage response = await actor.GetAsync(route);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            detail = await Read(response);
            return ReadText(detail.GetProperty("execution"), "status") == "Completed";
        }, TimeSpan.FromSeconds(30));
        JsonElement summary = detail.GetProperty("execution");
        Assert.AreEqual(version, ReadText(summary, "version"));
        Assert.AreEqual(result.InstanceId, ReadText(summary, "instanceId"));
        Assert.AreEqual(result.RegistrationId, ReadText(summary, "registrationId"));
        Assert.AreEqual(count, summary.GetProperty("returnedMatchCount").GetInt32());
        (string startJson, string completionJson) = await child.ReadDurableOutputsAsync();
        Assert.AreEqual(startJson, ReadText(detail, "startAuditJson"));
        Assert.AreEqual(completionJson, ReadText(detail, "completionAuditJson"));
        using JsonDocument start = JsonDocument.Parse(ReadText(detail, "startAuditJson"));
        Dictionary<string, JsonElement> inputs = start.RootElement.GetProperty("details").GetProperty("inputs").EnumerateArray()
            .ToDictionary(row => ReadText(row, "columnId"), StringComparer.Ordinal);
        Assert.AreEqual(country, ReadText(inputs[CountryResidenceRules.ResidenceCountry], "value"));
        Assert.AreEqual(pensioner, inputs[CountryResidenceRules.Pensioner].GetProperty("value").GetBoolean());
        RuleDefinition definition = RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(ReadText(detail, "definitionJson")));
        Assert.AreEqual(version, definition.Version);
    }

    private sealed class CountryRuleChild(PrivateContainerDirectory files, PostgresMessageFixture database,
        CancellationTokenSource stop, Task<ProcessResult> process) : IAsyncDisposable
    {
        internal static async Task<CountryRuleChild> StartAsync(ManagementHostFixture fixture, string expectedVersion,
            string expectedRelease, bool exact, string residence, bool pensioner, string countrySource, bool holdAfterStart)
        {
            var directory = new PrivateContainerDirectory();
            var lifetime = new CancellationTokenSource();
            PostgresMessageFixture? outbox = null;
            try
            {
                outbox = await PostgresMessageFixture.CreateAsync();
                string descriptor = Path.Combine(directory.Path, "country.json");
                var input = new RuleEngineCountryDataInput(1, fixture.Origins[0] + fixture.Options.BasePath, outbox.Schema,
                    expectedVersion, exact, residence, pensioner, countrySource, holdAfterStart, expectedRelease);
                await PrivateContainerDirectory.WriteNewAsync(descriptor, JsonSerializer.SerializeToUtf8Bytes(input));
                await PrivateContainerDirectory.WriteNewAsync(Path.Combine(directory.Path, "postgres.txt"),
                    Encoding.UTF8.GetBytes(outbox.ConnectionString));
                await PrivateContainerDirectory.WriteNewAsync(Path.Combine(directory.Path, "workload-token.txt"),
                    Encoding.UTF8.GetBytes(fixture.Token(Bootstrap.ObjectId, issuer: Bootstrap.NativeIssuer)));
                await PrivateContainerDirectory.WriteNewAsync(Path.Combine(directory.Path, "management-ca.pem"),
                    await File.ReadAllBytesAsync(fixture.ServerAuthorityFile));
                OwnedRabbitService server = await IntegrationServices.GetRabbitAsync();
                RabbitFixturePrincipal producer = server.CaptureInformation().Principals["other"];
                var owned = fixture.Options.Delivery!.CreateBrokerOptions();
                var rabbit = new RuleEngineCountryDataBroker(owned.HostName, owned.Port, owned.ServerName, owned.VirtualHost,
                    owned.Namespace, owned.SubjectIssuer, producer.User, producer.Password);
                await PrivateContainerDirectory.WriteNewAsync(Path.Combine(directory.Path, "rabbit.json"), JsonSerializer.SerializeToUtf8Bytes(rabbit));
                await PrivateContainerDirectory.WriteNewAsync(Path.Combine(directory.Path, "rabbit-ca.pem"), Encoding.UTF8.GetBytes(server.RootPem));

                var childEnvironment = new Dictionary<string, string?>
                {
                    ["CORE_SERVICE_NAME"] = EnvironmentOptions.ApplicationName,
                    ["CORE_ENVIRONMENT"] = EnvironmentOptions.EnvironmentName
                };
                Task<ProcessResult> result = ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                    MessageRpcProcessFixture.Binary, "rule-engine-country-data", descriptor], directory.Path, TimeSpan.FromSeconds(90),
                    childEnvironment, maximumCapturedBytesPerStream: 65_536, cancellationToken: lifetime.Token);
                return new(directory, outbox, lifetime, result);
            }
            catch
            {
                lifetime.Dispose();
                directory.Dispose();
                if (outbox is not null)
                {
                    await outbox.DisposeAsync();
                }
                throw;
            }
        }

        internal async Task<RuleEngineCountryDataStarted> StartedAsync()
        {
            string marker = Path.Combine(files.Path, "country-started.json");
            RuleEngineCountryDataStarted? start = null;
            await RabbitMqMessageFixture.UntilAsync(async () =>
            {
                if (process.IsCompleted)
                {
                    ProcessResult ended = await process;
                    Assert.Fail("The owned CountryData process exited before its durable start gate. " + ended.StandardError);
                }
                if (!File.Exists(marker))
                {
                    return false;
                }
                try
                {
                    start = JsonSerializer.Deserialize<RuleEngineCountryDataStarted>(await File.ReadAllBytesAsync(marker));
                    return start is not null;
                }
                catch (JsonException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(40));
            Assert.IsNotNull(start);
            Assert.AreNotEqual(Guid.Empty, start.ExecutionId);
            return start;
        }

        internal Task ReleaseAsync()
            => PrivateContainerDirectory.WriteNewAsync(Path.Combine(files.Path, "country-proceed"), [1]);

        internal async Task<RuleEngineCountryDataResult> ReadResultAsync()
        {
            ProcessResult ended = await process;
            Assert.AreEqual(0, ended.ExitCode, ended.StandardError);
            Assert.IsTrue(ended.OutputDrained);
            RuleEngineCountryDataResult result = JsonSerializer.Deserialize<RuleEngineCountryDataResult>(
                await File.ReadAllBytesAsync(Path.Combine(files.Path, "country-result.json")))!;
            Assert.AreEqual(ended.ProcessId, result.ProcessId);
            return result;
        }

        internal async Task<(string Start, string Completion)> ReadDurableOutputsAsync()
        {
            var audits = new Dictionary<string, string>(StringComparer.Ordinal);
            var codec = new AuditRecordCodec();
            await using var command = database.Source.CreateCommand($"SELECT envelope FROM {database.Table("outbox")} WHERE purpose=0 LIMIT 4");
            await using NpgsqlDataReader stored = await command.ExecuteReaderAsync();
            while (await stored.ReadAsync())
            {
                Assert.IsTrue(MessageWire.TryDecode(stored.GetFieldValue<byte[]>(0), out EncodedMessage? envelope, out _));
                byte[] payload = envelope!.Envelope.CopyPayload();
                Assert.IsTrue(codec.TryDecode(envelope.Envelope.Contract, payload, out AuditRecord? audit));
                Assert.IsTrue(audits.TryAdd(audit!.EventType, Encoding.UTF8.GetString(payload)));
            }
            Assert.HasCount(2, audits);
            return (audits[RuleEngineAuditEvents.ExecutionStarted], audits[RuleEngineAuditEvents.ExecutionCompleted]);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!process.IsCompleted)
                {
                    await stop.CancelAsync();
                }
                try
                {
                    _ = await process;
                }
                catch (ProcessTimeoutException failure) when (failure.CleanupCompleted && failure.Result.OutputDrained) { }
                catch (ProcessOutputLimitException failure) when (failure.Result.OutputDrained) { }
                catch (System.ComponentModel.Win32Exception) { } // The child could not start.
            }
            finally
            {
                stop.Dispose();
            }
            files.Dispose();
            await database.DisposeAsync();
        }
    }

}
