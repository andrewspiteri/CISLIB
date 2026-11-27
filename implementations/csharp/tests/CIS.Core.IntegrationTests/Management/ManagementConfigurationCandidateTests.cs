using System.Diagnostics;
using System.Net;
using System.Text;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationCandidateTests
{
    [TestMethod]
    public async Task UnpublishedExactCandidateMatchesNativePublishedCompositionWithoutAnyPreviewEffects()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "candidate-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        string commonId = definitions.Items.Single(item => item.Kind == "common").SourceId;
        string applicationId = definitions.Items.Single(item => item.HostApplicationName == "FirstHost").SourceId;
        string common = await PublishValue(actor, commonId, "candidate-common", """{"Greeting":"Common value"}""");
        string previous = await PublishValue(actor, applicationId, "candidate-previous", """{"Enabled":true}""");
        ManagementConfigurationAssignmentMap beforeMap = await Map(actor, fixture.ApplicationA);
        string assigned = (await CreateRelease(actor, fixture.ApplicationA, beforeMap.ETag, "candidate-current-default",
            new(1, new("absent"), new("present", previous), "Current default differs from the explicit candidate baseline"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, beforeMap.ETag, "candidate-current-assignment",
            new(1, "replace", assigned, [], "Use the current default as the comparison"));
        string payload = """{"Enabled":false,"Unrelated":9007199254740993,"UnrelatedText":"Café — 日本語","Zero":0,"Optional":null}""";
        ManagementConfigurationCandidatePreviewCommand command = await Draft(actor, fixture.ApplicationA, applicationId, payload,
            new("present", common), new("present", previous));
        long operations = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        long publications = await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication");
        ManagementConfigurationCatalog catalog = await Catalog(actor);
        ManagementConfigurationCandidatePreview preview = await Preview(actor, command);
        Assert.IsTrue(preview.StructurallyValid);
        Assert.IsFalse(preview.Redacted);
        Assert.AreEqual(command.ApplicationSource, preview.ApplicationSource, "The response retains the baseline, never a fake candidate publication.");
        Assert.AreEqual(command.CommonSource, preview.CommonSource);
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(payload), preview.DraftSha256);
        CollectionAssert.AreEquivalent(new[] { "/Greeting", "/Enabled" }, preview.Changes.Select(change => change.Path).ToArray());
        Assert.AreEqual(catalog.UsedBytes, (await Catalog(actor)).UsedBytes);
        Assert.AreEqual(operations, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        Assert.AreEqual(publications, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(command.AssignmentETag, (await Map(actor, fixture.ApplicationA)).ETag);

        string publication = await PublishValue(actor, applicationId, "candidate-explicit-publication", payload);
        using (HttpResponseMessage content = await actor.GetAsync("configuration/publications/" + publication))
        {
            Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
            ManagementConfigurationPublication publishedContent = await Decode<ManagementConfigurationPublication>(content);
            Assert.AreEqual(payload, publishedContent.PayloadJson);
            Assert.AreEqual(preview.DraftSha256, publishedContent.Sha256);
        }
        using HttpResponseMessage comparison = await Post(actor, "configuration/preview",
            new ManagementConfigurationPreviewCommand(1, fixture.ApplicationA, "shared.settings", "1",
                command.CommonSource, new("present", publication)));
        Assert.AreEqual(HttpStatusCode.OK, comparison.StatusCode);
        ManagementConfigurationPreview published = await Decode<ManagementConfigurationPreview>(comparison);
        Assert.AreEqual(preview.StructurallyValid, published.StructurallyValid);
        CollectionAssert.AreEqual(preview.Changes.ToArray(), published.Changes.ToArray());
        ManagementConfigurationCandidatePreview invalid = await Preview(actor, command with
        {
            SourceETag = (await Source(actor, applicationId)).ETag,
            PayloadJson = """{"Enabled":"not Boolean"}"""
        });
        Assert.IsFalse(invalid.StructurallyValid);
        Assert.IsTrue(invalid.Issues.Count > 0);
    }

    [TestMethod]
    public async Task ActualPreviewValidateAndContentGrantsStaySeparateAndPreconditionsAndForeignReferencesAreChecked()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "candidate-grants-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        string source = definitions.Items.Single(item => item.HostApplicationName == "FirstHost").SourceId;
        ManagementConfigurationCandidatePreviewCommand command = await Draft(actor, fixture.ApplicationA, source, """{"Enabled":false}""");
        using (HttpResponseMessage noValidate = await Post(fixture.Client("operator-b"), "configuration/candidate-preview", command))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, noValidate.StatusCode);
        }
        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options, options.Grants.Where(grant =>
            !(grant.Subject?.Identifier == "operator-a" && (grant.Capability == "management.applications.read"
                || grant.Capability == ManagementConfigurationCapabilities.DefinitionsRead))).ToArray()));
        ManagementConfigurationCandidatePreview independent = await Preview(fixture.Client("operator-a", 1), command);
        Assert.IsFalse(independent.Redacted, "Existing Preview+Validate does not acquire an unrelated AppRead/DefinitionsRead prerequisite.");

        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options, options.Grants.Where(grant =>
            !(grant.Subject?.Identifier == "operator-a" && grant.Capability == ManagementConfigurationCapabilities.ContentRead)).ToArray()));
        ManagementConfigurationCandidatePreview hidden = await Preview(fixture.Client("operator-a", 2), command);
        Assert.IsTrue(hidden.Redacted);
        Assert.AreEqual(0, hidden.Changes.Count);
        Assert.IsTrue(hidden.Issues.All(issue => issue.Path is null));
        foreach (ManagementConfigurationCandidatePreviewCommand stale in new[]
        {
            command with { SourceETag = "\"stale\"" }, command with { AssignmentETag = "\"stale\"" }
        })
        {
            using HttpResponseMessage response = await Post(actor, "configuration/candidate-preview", stale);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        }
        string common = definitions.Items.Single(item => item.Kind == "common").SourceId;
        using (HttpResponseMessage wrongSlot = await Post(actor, "configuration/candidate-preview", command with
        {
            SourceId = common
        }))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, wrongSlot.StatusCode);
        }
        string foreign = definitions.Items.Single(item => item.HostApplicationName == "SecondHost").SourceId;
        string foreignPublication = await PublishValue(actor, foreign, "candidate-foreign", """{"Enabled":false}""");
        using (HttpResponseMessage refused = await Post(actor, "configuration/candidate-preview", command with
        {
            ApplicationSource = new("present", foreignPublication)
        }))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, refused.StatusCode);
        }
        await using var disable = fixture.Source.CreateCommand(
            $"UPDATE \"{fixture.Options.Persistence.Schema}\".configuration_source SET enabled=false WHERE source_id=@id");
        disable.Parameters.AddWithValue("id", Guid.Parse(source));
        Assert.AreEqual(1, await disable.ExecuteNonQueryAsync());
        using HttpResponseMessage disabled = await Post(actor, "configuration/candidate-preview", command);
        Assert.AreEqual(HttpStatusCode.Conflict, disabled.StatusCode);
    }

    [TestMethod]
    public async Task RecipientPagesBindExactDraftBaselinesPreconditionsAndCurrentActorWithoutAFullHistoryScan()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "candidate-pages-catalog");
        string source = (await Definitions(actor)).Items.Single(item => item.HostApplicationName == "FirstHost").SourceId;
        var registrations = new List<string>();
        for (int index = 0; index < 3; index++)
        {
            string registration = Guid.NewGuid().ToString("D");
            using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration)
            {
                Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("candidate-recipient-" + index)))
            };
            request.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage accepted = await fixture.Client("workload-a").SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
            registrations.Add(registration);
        }
        ManagementConfigurationCandidatePreviewCommand command = await Draft(actor, fixture.ApplicationA, source, """{"Enabled":false}""");
        var actual = new List<string>();
        string? cursor = null;
        string? first = null;
        for (int page = 0; page < registrations.Count; page++)
        {
            ManagementConfigurationCandidatePreview response = await Preview(actor, command, 1, cursor);
            Assert.HasCount(1, response.Recipients.Items);
            Assert.AreEqual("unknown", response.Recipients.Items.Single().Compatibility);
            actual.Add(response.Recipients.Items.Single().RegistrationId);
            cursor = response.Recipients.NextCursor;
            first ??= cursor;
        }
        Assert.IsNull(cursor);
        CollectionAssert.AreEquivalent(registrations, actual);
        Assert.IsNotNull(first);
        foreach ((ManagementConfigurationCandidatePreviewCommand changed, int pageSize) in new[]
        {
            (command with { PayloadJson = """{ "Enabled":false}""" }, 1),
            (command with { SourceETag = "\"other\"" }, 1),
            (command with { AssignmentETag = "\"other\"" }, 1),
            (command with { CommonSource = new("present", Guid.NewGuid().ToString("D")) }, 1),
            (command, 2)
        })
        {
            using HttpResponseMessage refused = await Post(actor, Path(pageSize, first), changed);
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options, options.Grants.Append(new(options.Profile,
            ManagementConfigurationCapabilities.Validate, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
            subject: new(ManagementHostFixture.Issuer, "operator-b"), resourceSelection: CapabilityResourceSelection.AllOfType,
            resourceType: ManagementConfigurationCapabilities.SourceResource)).ToArray()));
        using HttpResponseMessage otherActor = await Post(fixture.Client("operator-b", 1), Path(1, first), command);
        Assert.AreEqual(HttpStatusCode.BadRequest, otherActor.StatusCode);
        Assert.HasCount(3, (await Preview(actor, command, 500)).Recipients.Items);
    }

    [TestMethod]
    public async Task OptionalUnsupportedMalformedAndEscapingHeavyCandidateStatesFailWithoutEffectsOrInventedContent()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "candidate-boundaries-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        string source = definitions.Items.Single(item => item.HostApplicationName == "FirstHost").SourceId;
        string common = definitions.Items.Single(item => item.Kind == "common").SourceId;
        ManagementConfigurationCandidatePreviewCommand command = await Draft(actor, fixture.ApplicationA, source, """{"Enabled":false}""");
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new CandidateLegacyBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>())));
        using (HttpResponseMessage unsupported = await Post(fixture.Client("operator-a", 1), Path(), command))
        {
            Assert.AreEqual(HttpStatusCode.NotImplemented, unsupported.StatusCode);
        }
        using (HttpResponseMessage hidden = await Post(fixture.Client("reader", 1), Path(), command))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        var scenario = new CandidateScenario();
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new CandidateBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>(), scenario)));
        foreach (string fault in new[] { "digest", "source", "baseline", "map", "redacted", "too-many", "not-found-uncertain" })
        {
            scenario.Fault = fault;
            using HttpResponseMessage invalid = await Post(fixture.Client("operator-a", 2), Path(1), command);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, invalid.StatusCode, fault);
            string body = await invalid.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains("draftSha256", StringComparison.Ordinal), fault);
            Assert.IsFalse(body.Contains("currentAssignment", StringComparison.Ordinal), fault);
        }
        string heavy = System.Text.Json.JsonSerializer.Serialize(new
        {
            ExtraA = new string('\\', 43680),
            ExtraB = new string('\\', 43680),
            ExtraC = new string('\\', 43680)
        });
        Assert.IsTrue(Encoding.UTF8.GetByteCount(heavy) <= 256 * 1024);
        string publication = await PublishValue(actor, common, "candidate-heavy-baseline", heavy);
        ManagementConfigurationCandidatePreviewCommand oversized = await Draft(actor, fixture.ApplicationA, source, heavy, new("present", publication));
        using HttpResponseMessage tooLarge = await Post(actor, Path(), oversized);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, tooLarge.StatusCode, "Escaped source strings must fit the eventual native envelope before any publication.");
    }

    [TestMethod]
    public async Task NearEnvelopeCandidateRetainsTheExistingWorstUnicodeEnvironmentEligibilityBound()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "candidate-unicode-envelope-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        string commonSource = definitions.Items.Single(item => item.Kind == "common").SourceId;
        string applicationSource = definitions.Items.Single(item => item.HostApplicationName == "FirstHost").SourceId;
        // Both real publications sit near the envelope threshold: counting scalar characters as ASCII
        // would admit this pair, while the established encoded non-BMP environment allowance refuses it.
        string baselinePayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            ExtraA = new string('\\', 43198),
            ExtraB = new string('\\', 43198),
            ExtraC = new string('\\', 43198)
        });
        string common = await PublishValue(actor, commonSource, "unicode-envelope-common", baselinePayload);
        string application = await PublishValue(actor, applicationSource, "unicode-envelope-application", baselinePayload);
        using (HttpResponseMessage existing = await Post(actor, "configuration/preview",
            new ManagementConfigurationPreviewCommand(1, fixture.ApplicationA, "shared.settings", "1",
                new("present", common), new("present", application))))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, existing.StatusCode,
                "The existing immutable-reference preview is the native envelope-bound oracle.");
        }
        string unpublishedPayload = baselinePayload.Replace("\"ExtraA\"", "\"OtherA\"", StringComparison.Ordinal);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(baselinePayload), Encoding.UTF8.GetByteCount(unpublishedPayload));
        Assert.AreNotEqual(ManagementConfigurationWire.ComputePayloadSha256(baselinePayload),
            ManagementConfigurationWire.ComputePayloadSha256(unpublishedPayload));
        ManagementConfigurationCandidatePreviewCommand command = await Draft(actor, fixture.ApplicationA, applicationSource,
            unpublishedPayload, new("present", common), new("present", application));
        using HttpResponseMessage candidate = await Post(actor, Path(), command);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, candidate.StatusCode,
            "An unpublished layer must not weaken the existing escaped Unicode environment bound.");
        Assert.IsFalse((await candidate.Content.ReadAsStringAsync()).Contains("draftSha256", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancellationDrainsTheActualNativeCandidateEndpointReadBeforeTheOwnedLockIsReleased()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "candidate-cancellation-catalog");
        string source = (await Definitions(actor)).Items.Single(item => item.HostApplicationName == "FirstHost").SourceId;
        ManagementConfigurationCandidatePreviewCommand command = await Draft(actor, fixture.ApplicationA, source, """{"Enabled":false}""");
        await using var connection = await fixture.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = connection.CreateCommand())
        {
            hold.Transaction = transaction;
            hold.CommandText = $"LOCK TABLE \"{fixture.Options.Persistence.Schema}\".configuration_binding IN ACCESS EXCLUSIVE MODE";
            await hold.ExecuteNonQueryAsync();
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<HttpResponseMessage> pending = Post(actor, Path(), command, cancellation.Token);
        try
        {
            Assert.IsTrue(await Blocked(fixture, true));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                using HttpResponseMessage response = await pending;
            });
            Assert.IsTrue(await Blocked(fixture, false), "Actual native SQL must drain while the lock is still held.");
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

    private static string Path(int pageSize = 100, string? cursor = null)
        => "configuration/candidate-preview?pageSize=" + pageSize + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));

    private static async Task<ManagementConfigurationCandidatePreviewCommand> Draft(HttpClient actor, string application, string source, string payload,
        ManagementConfigurationSourceReference? common = null, ManagementConfigurationSourceReference? local = null)
        => new(1, application, "shared.settings", "1", "application", source, (await Source(actor, source)).ETag,
            (await Map(actor, application)).ETag, common ?? new("absent"), local ?? new("absent"), payload);

    private static async Task<ManagementConfigurationCandidatePreview> Preview(HttpClient actor,
        ManagementConfigurationCandidatePreviewCommand command, int pageSize = 100, string? cursor = null)
    {
        using HttpResponseMessage response = await Post(actor, Path(pageSize, cursor), command);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationCandidatePreview>(response);
    }

    private static async Task<HttpResponseMessage> Post<T>(HttpClient actor, string path, T command,
        CancellationToken token = default) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(command)) };
        request.Content.Headers.ContentType = new("application/json");
        return await actor.SendAsync(request, token);
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

    private class CandidateLegacyBackend : ManagementConfigurationCohortLegacyBackend, IManagementConfigurationBackend
    {
        private readonly IManagementConfigurationBackend native;

        internal CandidateLegacyBackend(IManagementConfigurationBackend native) : base(native)
        {
            this.native = native;
        }

        Task<ManagementStoreResult<ManagementConfigurationDefinition>> IManagementConfigurationBackend.ReadDefinitionAsync(
            string sourceId, CancellationToken token) => native.ReadDefinitionAsync(sourceId, token);
    }

    private sealed class CandidateScenario
    {
        internal string Fault { get; set; } = "";
    }

    private sealed class CandidateBackend : CandidateLegacyBackend, IManagementConfigurationCandidatePreviewBackend
    {
        private readonly IManagementConfigurationCandidatePreviewBackend native;
        private readonly CandidateScenario scenario;

        internal CandidateBackend(IManagementConfigurationBackend backend, CandidateScenario scenario) : base(backend)
        {
            native = (IManagementConfigurationCandidatePreviewBackend)backend;
            this.scenario = scenario;
        }

        public async Task<ManagementStoreResult<ManagementConfigurationCandidatePreview>> PreviewCandidateAsync(
            ManagementConfigurationBindingResource binding, ManagementConfigurationCandidatePreviewCommand command, string? after,
            int take, bool includeValues, CancellationToken token = default)
        {
            ManagementStoreResult<ManagementConfigurationCandidatePreview> result = await native.PreviewCandidateAsync(
                binding, command, after, take, includeValues, token);
            ManagementConfigurationCandidatePreview value = result.Value!;
            return scenario.Fault switch
            {
                "digest" => result with { Value = value with { DraftSha256 = new string('0', 64) } },
                "source" => result with { Value = value with { SourceId = Guid.NewGuid().ToString("D") } },
                "baseline" => result with { Value = value with { CommonSource = new("present", Guid.NewGuid().ToString("D")) } },
                "map" => result with { Value = value with { CurrentAssignment = value.CurrentAssignment with { ETag = "\"foreign\"" } } },
                "redacted" => result with { Value = value with { Redacted = !value.Redacted, Changes = [] } },
                "too-many" => result with
                {
                    Value = value with
                    {
                        Recipients = new(Enumerable.Range(1, 3)
                    .Select(index => new ManagementConfigurationPreviewRecipient("00000000-0000-0000-0001-" + index.ToString("D12"), "unknown")).ToArray())
                    }
                },
                "not-found-uncertain" => result with
                {
                    Value = null,
                    Code = "management.configuration.not_found",
                    AttemptCompletion = null,
                    PreviouslyCommitted = false
                },
                _ => result
            };
        }
    }
}

