using System.Diagnostics;
using System.Net;
using System.Text;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationEditorTests
{
    [TestMethod]
    public async Task ExactQualifiedBindingSourcesAndSelectedDeclaredDefaultsRemainReadOnlyAndIndependentlyAuthorized()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "editor-context-catalog");
        ManagementConfigurationDefinition application = (await Definitions(actor)).Items.Single(item => item.HostApplicationName == "FirstHost");
        ManagementConfigurationEditorContext context = await Read(actor, Path(fixture.ApplicationA));
        Assert.AreEqual(application.BindingId, context.BindingId);
        Assert.AreEqual(application.SourceId, context.ApplicationSource.SourceId);
        Assert.AreEqual("managed-a", context.Application);
        Assert.AreEqual("Development", context.Environment);
        Assert.AreEqual("FirstHost", context.HostApplicationName);
        Assert.AreEqual("available", context.CommonSource.State);
        Assert.AreEqual("available", context.ApplicationSource.State);
        Assert.AreNotEqual(context.CommonSource.SourceId, context.ApplicationSource.SourceId);
        Assert.AreEqual(application.DefaultsJson, context.Defaults.DefaultsJson);
        ManagementConfigurationEditorContext common = await Read(actor, Path(fixture.ApplicationA, "common"));
        Assert.AreEqual(context.CommonSource.SourceId, common.CommonSource.SourceId);
        Assert.AreEqual(application.DefaultsJson, common.Defaults.DefaultsJson);
        ManagementConfigurationEditorContext readOnly = await Read(fixture.Client("operator-b"), Path(fixture.ApplicationA));
        Assert.AreEqual("available", readOnly.ApplicationSource.State);
        Assert.AreEqual("redacted", readOnly.Defaults.State);
        Assert.IsNull(readOnly.Defaults.DefaultsJson);

        string commonId = context.CommonSource.SourceId!;
        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options,
            options.Grants.Where(grant => !(grant.Subject?.Identifier == "operator-a"
                && grant.Capability == ManagementConfigurationCapabilities.DefinitionsRead
                && grant.ResourceType == ManagementConfigurationCapabilities.SourceResource))
                .Append(new(options.Profile, ManagementConfigurationCapabilities.DefinitionsRead, EnvironmentOptions.ApplicationName,
                    EnvironmentOptions.EnvironmentName, subject: new(ManagementHostFixture.Issuer, "operator-a"),
                    resourceSelection: CapabilityResourceSelection.ExactResource,
                    resourceType: ManagementConfigurationCapabilities.SourceResource, resourceIdentifier: application.SourceId)).ToArray()));
        HttpClient selectedOnly = fixture.Client("operator-a", 1);
        ManagementConfigurationEditorContext hidden = await Read(selectedOnly, Path(fixture.ApplicationA, "common"));
        Assert.AreEqual("redacted", hidden.CommonSource.State);
        Assert.IsNull(hidden.CommonSource.SourceId);
        Assert.AreEqual("redacted", hidden.Defaults.State);
        Assert.IsNull(hidden.Defaults.DefaultsJson, "Other source ContentRead cannot disclose selected hidden-source defaults.");
        Assert.AreEqual("available", (await Read(selectedOnly, Path(fixture.ApplicationA))).Defaults.State);
        Assert.IsFalse(Encoding.UTF8.GetString(ManagementConfigurationWire.EncodeResponse(hidden)).Contains(commonId, StringComparison.Ordinal));

        using HttpResponseMessage info = await actor.GetAsync("service-info");
        ManagementServiceInfo service = ManagementWire.DecodeResponse<ManagementServiceInfo>(await info.Content.ReadAsByteArrayAsync())!;
        Assert.AreEqual(46, service.Features.Count);
        foreach (string operation in new[] { "getConfigurationCreationContext", "createConfiguration", "getConfigurationCreationOperation", "getConfigurationSummaries" })
        {
            Assert.AreEqual(1, service.Features.Count(item => item.OperationId == operation));
        }
        Assert.AreEqual(1, service.Features.Count(item => item.OperationId == "getConfigurationEditorContext"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task DisabledRetainedStreamsAndActualAbsenceDifferWhileHistoricalReadsAndLegacyBackendsStayCompatible()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "editor-disabled-catalog");
        string common = (await Read(actor, Path(fixture.ApplicationA))).CommonSource.SourceId!;
        await using (var disable = fixture.Source.CreateCommand(
            $"UPDATE \"{fixture.Options.Persistence.Schema}\".configuration_source SET enabled=false WHERE source_id=@source"))
        {
            disable.Parameters.AddWithValue("source", Guid.Parse(common));
            Assert.AreEqual(1, await disable.ExecuteNonQueryAsync());
        }
        ManagementConfigurationEditorContext disabled = await Read(actor, Path(fixture.ApplicationA, "common"));
        Assert.AreEqual("unavailable", disabled.CommonSource.State);
        Assert.AreEqual(common, disabled.CommonSource.SourceId);
        Assert.AreEqual("unavailable", disabled.Defaults.State);
        Assert.IsNull(disabled.Defaults.DefaultsJson);
        using (HttpResponseMessage historical = await actor.GetAsync("configuration/sources/" + common))
        {
            Assert.AreEqual(HttpStatusCode.OK, historical.StatusCode);
        }
        await using (var remove = fixture.Source.CreateCommand(
            $"DELETE FROM \"{fixture.Options.Persistence.Schema}\".configuration_source WHERE source_id=@source"))
        {
            remove.Parameters.AddWithValue("source", Guid.Parse(common));
            Assert.AreEqual(1, await remove.ExecuteNonQueryAsync());
        }
        ManagementConfigurationEditorContext absent = await Read(actor, Path(fixture.ApplicationA, "common"));
        Assert.AreEqual("absent", absent.CommonSource.State);
        Assert.IsNull(absent.CommonSource.SourceId);
        Assert.AreEqual("unavailable", absent.Defaults.State);

        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new ManagementConfigurationCohortLegacyBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>())));
        using (HttpResponseMessage unsupported = await fixture.Client("operator-a", 1).GetAsync(Path(fixture.ApplicationA)))
        {
            Assert.AreEqual(HttpStatusCode.NotImplemented, unsupported.StatusCode);
            Assert.AreEqual("management.configuration.editor.unsupported", (await Decode<ManagementConfigurationProblem>(unsupported)).Code);
        }
        using HttpResponseMessage hidden = await fixture.Client("reader", 1).GetAsync(Path(fixture.ApplicationA));
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [TestMethod]
    public async Task HiddenForgedAndMalformedOptionalSnapshotsFailWithoutDefaultsOrPartialContext()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "editor-malformed-catalog");
        foreach (string path in new[] { Path(fixture.ApplicationA).Replace("schemaVersion=1", "schemaVersion=2", StringComparison.Ordinal),
            Path(fixture.ApplicationB) })
        {
            using HttpResponseMessage hidden = await actor.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        foreach (string path in new[] { Path(fixture.ApplicationA) + "&unknown=1", Path(fixture.ApplicationA) + "&source=common",
            Path(fixture.ApplicationA).Replace("source=application", "Source=application", StringComparison.Ordinal),
            Path(fixture.ApplicationA).Replace("schemaVersion=1", "SchemaVersion=1", StringComparison.Ordinal),
            Path(fixture.ApplicationA).Replace("source=application", "source=%FF", StringComparison.Ordinal) })
        {
            using HttpResponseMessage invalid = await actor.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode, path);
        }
        using (var body = new HttpRequestMessage(HttpMethod.Get, Path(fixture.ApplicationA))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })
        using (HttpResponseMessage invalid = await actor.SendAsync(body))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        var scenario = new EditorScenario();
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new EditorBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>(), scenario)));
        foreach (string fault in new[] { "gate", "application", "environment", "schema", "host", "source", "source-id", "duplicate", "defaults", "shape", "cleanup", "no-completion", "rollback", "more",
            "not-found-no-completion", "not-found-cleanup", "not-found-uncertain" })
        {
            scenario.Fault = fault;
            scenario.EditorCalls = 0;
            using HttpResponseMessage refused = await fixture.Client("operator-a", 1).GetAsync(Path(fixture.ApplicationA));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode, fault);
            string bytes = await refused.Content.ReadAsStringAsync();
            Assert.IsFalse(bytes.Contains("defaultsJson", StringComparison.Ordinal), fault);
            Assert.IsFalse(bytes.Contains("commonSource", StringComparison.Ordinal), fault);
            if (fault == "gate")
            {
                Assert.AreEqual(0, scenario.EditorCalls, "A malformed resolved base binding must be refused before the optional read.");
            }
        }
        scenario.Fault = "not-found-clean";
        using HttpResponseMessage cleanMissing = await fixture.Client("operator-a", 1).GetAsync(Path(fixture.ApplicationA));
        Assert.AreEqual(HttpStatusCode.NotFound, cleanMissing.StatusCode);
        Assert.AreEqual("management.configuration.not_found", (await Decode<ManagementConfigurationProblem>(cleanMissing)).Code);
    }

    [TestMethod]
    public async Task CallerCancellationDrainsTheActualNativeBindingReadBeforeItsOwnedDatabaseLockIsReleased()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "editor-cancellation-catalog");
        await using var connection = await fixture.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = connection.CreateCommand())
        {
            hold.Transaction = transaction;
            hold.CommandText = $"LOCK TABLE \"{fixture.Options.Persistence.Schema}\".configuration_binding IN ACCESS EXCLUSIVE MODE";
            await hold.ExecuteNonQueryAsync();
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<HttpResponseMessage> pending = actor.GetAsync(Path(fixture.ApplicationA), cancellation.Token);
        try
        {
            Assert.IsTrue(await Blocked(fixture, true), "Actual product SQL must reach the held binding table.");
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                using HttpResponseMessage response = await pending;
            });
            Assert.IsTrue(await Blocked(fixture, false), "Native cancellation must drain before releasing the lock.");
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                using HttpResponseMessage response = await pending;
            }
            catch (OperationCanceledException)
            {
            }
            await transaction.RollbackAsync();
        }
    }

    private static string Path(string application, string source = "application")
        => "applications/" + application + "/configuration/shared.settings/editor-context?schemaVersion=1&source=" + source;

    private static async Task<ManagementConfigurationEditorContext> Read(HttpClient actor, string path)
    {
        using HttpResponseMessage response = await actor.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        return await Decode<ManagementConfigurationEditorContext>(response);
    }

    private static async Task<bool> Blocked(ManagementHostFixture fixture, bool expected)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var query = fixture.Source.CreateCommand("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid<>pg_backend_pid()
                    AND state='active' AND wait_event_type='Lock' AND strpos(query,@table)>0
                    AND strpos(query,'b.binding_id,a.application_id')>0)
                """);
            query.Parameters.AddWithValue("table", "\"" + fixture.Options.Persistence.Schema + "\".\"configuration_binding\"");
            if ((bool)(await query.ExecuteScalarAsync())! == expected)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        return false;
    }

    private static ManagementSecurityOptions Grants(ManagementSecurityOptions original, IReadOnlyList<CapabilityGrant> grants)
        => new()
        {
            Mode = original.Mode,
            Profile = original.Profile,
            AuthenticationScheme = original.AuthenticationScheme,
            BusinessAddress = original.BusinessAddress,
            BusinessPort = original.BusinessPort,
            OperationalPort = original.OperationalPort,
            ServerCertificateFile = original.ServerCertificateFile,
            ServerPrivateKeyFile = original.ServerPrivateKeyFile,
            ServerTrustBundleFile = original.ServerTrustBundleFile,
            CertificateRevocation = original.CertificateRevocation,
            BorrowedNativeKeyScope = original.BorrowedNativeKeyScope,
            PolicyVersion = original.PolicyVersion,
            IdentityMappings = original.IdentityMappings,
            NativeProvider = original.NativeProvider,
            Grants = grants
        };

    private sealed class EditorScenario
    {
        internal string Fault { get; set; } = "";
        internal int EditorCalls
        {
            get; set;
        }
    }

    private sealed class EditorBackend : ManagementConfigurationCohortLegacyBackend, IManagementConfigurationEditorBackend,
        IManagementConfigurationBackend
    {
        private readonly IManagementConfigurationEditorBackend editor;
        private readonly EditorScenario scenario;

        internal EditorBackend(IManagementConfigurationBackend native, EditorScenario scenario) : base(native)
        {
            editor = (IManagementConfigurationEditorBackend)native;
            this.scenario = scenario;
        }

        async Task<ManagementStoreResult<ManagementConfigurationBindingResource>> IManagementConfigurationBackend.ReadBindingAsync(
            string applicationId, string configurationId, string schemaVersion, CancellationToken token)
        {
            ManagementStoreResult<ManagementConfigurationBindingResource> result = await base.ReadBindingAsync(
                applicationId, configurationId, schemaVersion, token);
            return scenario.Fault == "gate" ? result with
            {
                Value = result.Value! with
                {
                    Environment = "foreign"
                }
            } : result;
        }

        public async Task<ManagementStoreResult<ManagementConfigurationEditorBindingResource>> ReadEditorBindingAsync(
            string applicationId, string configurationId, string schemaVersion, CancellationToken token = default)
        {
            scenario.EditorCalls++;
            ManagementStoreResult<ManagementConfigurationEditorBindingResource> result = await editor
                .ReadEditorBindingAsync(applicationId, configurationId, schemaVersion, token);
            ManagementConfigurationEditorBindingResource snapshot = result.Value!;
            return scenario.Fault switch
            {
                "application" => result with { Value = snapshot with { Binding = snapshot.Binding with { ApplicationId = Guid.NewGuid().ToString("D") } } },
                "environment" => result with { Value = snapshot with { Binding = snapshot.Binding with { Environment = "foreign" } } },
                "schema" => result with { Value = snapshot with { Binding = snapshot.Binding with { SchemaVersion = "2" } } },
                "source-id" => result with
                {
                    Value = snapshot with
                    {
                        Binding = snapshot.Binding with
                        {
                            CommonSourceId = "77777777-7777-7777-7777-777777777777"
                        },
                        CommonSource = snapshot.CommonSource! with
                        {
                            Definition = snapshot.CommonSource!.Definition with
                            {
                                SourceId = "77777777-7777-7777-7777-777777777777"
                            }
                        }
                    }
                },
                "host" => result with
                {
                    Value = snapshot with
                    {
                        Binding = snapshot.Binding with
                        {
                            HostApplicationName = "ForeignHost"
                        },
                        ApplicationSource = snapshot.ApplicationSource! with
                        {
                            Definition = snapshot.ApplicationSource!.Definition with
                            {
                                HostApplicationName = "ForeignHost"
                            }
                        }
                    }
                },
                "source" => result with
                {
                    Value = snapshot with
                    {
                        ApplicationSource = snapshot.ApplicationSource! with
                        {
                            Definition = snapshot.ApplicationSource!.Definition with
                            {
                                Environment = "foreign"
                            }
                        }
                    }
                },
                "duplicate" => result with
                {
                    Value = snapshot with
                    {
                        Binding = snapshot.Binding with
                        {
                            ApplicationSourceId = snapshot.Binding.CommonSourceId
                        },
                        ApplicationSource = snapshot.ApplicationSource! with
                        {
                            Definition = snapshot.ApplicationSource!.Definition with
                            {
                                SourceId = snapshot.Binding.CommonSourceId!
                            }
                        }
                    }
                },
                "defaults" => result with { Value = snapshot with { Binding = snapshot.Binding with { DefaultsJson = """{"Enabled":"not Boolean"}""" } } },
                "shape" => result with { Value = snapshot with { Binding = snapshot.Binding with { Shape = new("future", false) } } },
                "cleanup" => result with
                {
                    AttemptCompletion = new(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested,
                    commitAttempted: false, rollbackAttempted: true, rollbackConfirmed: true,
                    cleanupErrors: [new(TransactionErrorPhase.ConnectionDispose, "test.cleanup", "Owned cleanup failed.")])
                },
                "no-completion" => result with { AttemptCompletion = null },
                "rollback" => result with
                {
                    AttemptCompletion = new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
                    commitAttempted: true, rollbackAttempted: false, rollbackConfirmed: false)
                },
                "more" => result with { HasMore = true },
                "not-found-clean" => result with { Value = null, Code = "management.configuration.not_found", PreviouslyCommitted = false },
                "not-found-no-completion" => result with
                {
                    Value = null,
                    Code = "management.configuration.not_found",
                    PreviouslyCommitted = false,
                    AttemptCompletion = null
                },
                "not-found-cleanup" => result with
                {
                    Value = null,
                    Code = "management.configuration.not_found",
                    PreviouslyCommitted = false,
                    AttemptCompletion = new(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested,
                        commitAttempted: false, rollbackAttempted: true, rollbackConfirmed: true,
                        cleanupErrors: [new(TransactionErrorPhase.ConnectionDispose, "test.cleanup", "Owned cleanup failed.")])
                },
                "not-found-uncertain" => result with
                {
                    Value = null,
                    Code = "management.configuration.not_found",
                    PreviouslyCommitted = false,
                    AttemptCompletion = new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
                        commitAttempted: true, rollbackAttempted: false, rollbackConfirmed: false)
                },
                _ => result
            };
        }
    }
}

