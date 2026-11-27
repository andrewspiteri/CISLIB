using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Management;
using CIS.Management.Wire;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementConfigurationWireTests
{
    private const string ApplicationId = "11111111-1111-1111-1111-111111111111";
    private const string RegistrationId = "22222222-2222-2222-2222-222222222222";
    private const string AuthorityId = "88888888-8888-8888-8888-888888888888";
    private const string ReleaseId = "77777777-7777-7777-7777-777777777777";
    private const string Payload = """{"Greeting":"Café — 日本語","Enabled":false}""";
    private const string Digest = "6b22cc1785a4a64b26ce415be9c4a3cfbe21aa5b7127644f87004d92192429d9";
    private const string Time = "2026-11-24T00:12:34.1234567Z";

    public static IEnumerable<object[]> IndependentCases()
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllBytes(CatalogPath()));
        foreach (JsonElement row in catalog.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [row.GetProperty("id").GetString()!, row.GetProperty("schema").GetString()!,
                row.GetProperty("direction").GetString()!, row.GetProperty("json").GetString()!, row.GetProperty("valid").GetBoolean()];
        }
    }

    [TestMethod]
    [DynamicData(nameof(IndependentCases))]
    public void IndependentlyAuthoredPortableCasesExerciseTheActualCodec(string id, string schema,
        string direction, string json, bool valid)
    {
        byte[] bytes = Bytes(json);
        object? value = (schema, direction) switch
        {
            ("ConfigurationCatalogCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCatalogCommand>(bytes),
            ("ConfigurationCreationCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCreationCommand>(bytes),
            ("ConfigurationCreationContext", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCreationContext>(bytes),
            ("ConfigurationSummary", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSummary>(bytes),
            ("ConfigurationSummaryPage", "response") => ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationSummary>>(bytes),
            ("ConfigurationValidationCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationValidationCommand>(bytes),
            ("ConfigurationPreviewCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationPreviewCommand>(bytes),
            ("ConfigurationPublicationCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationPublicationCommand>(bytes),
            ("ConfigurationReleaseCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationReleaseCommand>(bytes),
            ("ConfigurationAssignmentCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationAssignmentCommand>(bytes),
            ("ConfigurationStatusReport", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationStatusReport>(bytes),
            ("ConfigurationDefinitionPage", "response") => ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationDefinition>>(bytes),
            ("ConfigurationReleasePage", "response") => ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationRelease>>(bytes),
            ("ConfigurationCandidatePreviewCommand", "request") => ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCandidatePreviewCommand>(bytes),
            ("ConfigurationCandidatePreview", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCandidatePreview>(bytes),
            ("ConfigurationEditorContext", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationEditorContext>(bytes),
            ("ConfigurationCatalog", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCatalog>(bytes),
            ("ConfigurationValidationResult", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationValidationResult>(bytes),
            ("ConfigurationPreview", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationPreview>(bytes),
            ("ConfigurationSource", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSource>(bytes),
            ("ConfigurationPublication", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationPublication>(bytes),
            ("ConfigurationRelease", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationRelease>(bytes),
            ("ConfigurationAssignmentMap", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationAssignmentMap>(bytes),
            ("ConfigurationSnapshot", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSnapshot>(bytes),
            ("ConfigurationAdoptionPage", "response") => ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationAdoptionEntry>>(bytes),
            ("ConfigurationOperationResult", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(bytes),
            ("ConfigurationProblem", "response") => ManagementConfigurationWire.DecodeResponse<ManagementConfigurationProblem>(bytes),
            _ => throw new AssertFailedException("Select an explicit declared codec branch for " + schema)
        };

        Assert.AreEqual(valid, value is not null, id);
    }

    [TestMethod]
    public void IdentifiedContentFailuresRetainDesiredButMalformedOrForeignHeadersDoNot()
    {
        foreach (string id in new[] { "configuration-payload-digest-mismatch", "configuration-duplicate-decoded-payload-name", "configuration-payload-bom-refused" })
        {
            ManagementConfigurationSnapshotRead read = ManagementConfigurationWire.DecodeSnapshot(Bytes(Case(id)), Target());
            Assert.IsNull(read.Snapshot, id);
            Assert.IsNotNull(read.Desired, id);
            Assert.AreEqual(ReleaseId, read.Desired.ReleaseId);
            Assert.AreEqual("9007199254740993", read.Desired.Generation);
            Assert.AreNotEqual(ManagementWireFailure.None, read.Failure);
        }
        ManagementConfigurationSnapshotRead wrongTarget = ManagementConfigurationWire.DecodeSnapshot(
            Bytes(Case("configuration-snapshot-valid")), Target() with
            {
                CohortId = "another-cohort"
            });
        Assert.IsNull(wrongTarget.Desired);
        Assert.AreEqual("configuration.target.mismatch", wrongTarget.FailureCode);

        JsonObject invalidHeader = SnapshotNode();
        invalidHeader["desired"]!["generation"] = "01";
        Assert.IsNull(ManagementConfigurationWire.DecodeSnapshot(Bytes(invalidHeader.ToJsonString()), Target()).Desired);
        JsonObject invalidETag = SnapshotNode();
        invalidETag["eTag"] = "W/\"weak\"";
        Assert.IsNull(ManagementConfigurationWire.DecodeSnapshot(Bytes(invalidETag.ToJsonString()), Target()).Desired);
        Assert.IsNull(ManagementConfigurationWire.DecodeSnapshot(Bytes("{broken"), Target()).Desired);
        Assert.IsNull(ManagementConfigurationWire.DecodeSnapshot(new byte[] { 0xff, 0xfe, 0x00 }, Target()).Desired);
    }

    [TestMethod]
    public void SourceSchemaAndScopeAreCheckedUnderAnIndependentlyValidDesiredHeader()
    {
        foreach ((string field, string value) in new[] { ("schemaVersion", "2"), ("environment", "Production"), ("kind", "application") })
        {
            JsonObject snapshot = SnapshotNode();
            snapshot["commonSource"]![field] = value;
            ManagementConfigurationSnapshotRead read = ManagementConfigurationWire.DecodeSnapshot(Bytes(snapshot.ToJsonString()), Target());
            Assert.IsNull(read.Snapshot);
            Assert.IsNotNull(read.Desired);
        }
        JsonObject foreignAuthority = SnapshotNode();
        foreignAuthority["desired"]!["authorityId"] = ApplicationId;
        Assert.IsNull(ManagementConfigurationWire.DecodeSnapshot(Bytes(foreignAuthority.ToJsonString()), Target()).Desired);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.DecodeSnapshot(
            Bytes(Case("configuration-snapshot-valid")), Target() with
            {
                RegistrationId = Guid.Empty.ToString("D")
            }));
    }

    [TestMethod]
    public void ExactUnicodePayloadDigestDoesNotNormalizeOrReserializeAndAllOuterBytesRemainBounded()
    {
        Assert.AreEqual(Digest, ManagementConfigurationWire.ComputePayloadSha256(Payload));
        Assert.AreNotEqual(Digest, ManagementConfigurationWire.ComputePayloadSha256(Payload + " "));
        Assert.AreNotEqual(ManagementConfigurationWire.ComputePayloadSha256("""{"text":"é"}"""),
            ManagementConfigurationWire.ComputePayloadSha256("{\"text\":\"e\u0301\"}"));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.ComputePayloadSha256("\ufeff{}"));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.ComputePayloadSha256("{\"a\":1,\"\\u0061\":2}"));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.ComputePayloadSha256("{\"a\":\"\ud800\"}"));

        var tight = new ManagementConfigurationWireLimits(maximumBodyBytes: 64);
        Assert.IsNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSnapshot>(
            Bytes(Case("configuration-snapshot-valid")), out ManagementWireFailure failure, tight));
        Assert.AreEqual(ManagementWireFailure.TooLarge, failure);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(
            new ManagementConfigurationPublicationCommand(1, Payload, "Publish"), tight));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(
            new ManagementConfigurationPublicationCommand(1, "{\"x\":\"" + new string('é', 150_000) + "\"}", "Publish")));
        // Each decoded source fits 256 KiB. Escaping both complete sources plus
        // the identity envelope exceeds one MiB and must not be truncated.
        string source = "{\"x\":\"" + new string('é', 120_000) + "\"}";
        ManagementConfigurationSnapshot snapshot = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSnapshot>(
            Bytes(Case("configuration-snapshot-valid")))!;
        ManagementConfigurationSourceContent common = snapshot.CommonSource with
        {
            PayloadJson = source,
            Sha256 = ManagementConfigurationWire.ComputePayloadSha256(source)
        };
        ManagementConfigurationSourceContent application = common with
        {
            Kind = "application",
            ApplicationId = ApplicationId
        };
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(snapshot with
        {
            CommonSource = common,
            ApplicationSource = application
        }));
        Assert.AreEqual(64 * 1024, new ManagementWireLimits().MaximumRegularRequestBytes);
    }

    [TestMethod]
    public void UnknownResponseStatesAndAdditionsRemainExplicitWhileCommandsAreClosed()
    {
        var future = ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationAdoptionEntry>>(
            Bytes(Case("configuration-unknown-adoption-is-not-success-default")));
        Assert.IsNotNull(future);
        Assert.IsFalse(future.Items[0].KnownConvergence.IsKnown);
        Assert.IsFalse(future.Items[0].Reported!.KnownState.IsKnown);

        JsonObject response = SnapshotNode();
        response["desired"]!["future"] = JsonNode.Parse("{\"safe\":true}");
        response["future"] = null;
        Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSnapshot>(Bytes(response.ToJsonString())));
        Assert.IsNull(ManagementConfigurationWire.DecodeRequest<ManagementConfigurationReleaseCommand>(Bytes(
            """{"commandVersion":1,"reason":"Test","commonSource":{"state":"absent","future":true},"applicationSource":{"state":"absent"}}""")));
        Assert.IsNull(ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCatalogCommand>(Bytes(
            "{\"commandVersion\":1,\"manifestDigest\":\"" + new string('a', 64) + "\",\"reason\":null}")));
    }

    [TestMethod]
    public void CompleteAssignmentReplayRetainsIntentMapAndOriginalCommitDespiteCurrentCleanupFailure()
    {
        var result = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(
            Bytes(Case("configuration-operationresult-valid")));
        Assert.IsNotNull(result);
        Assert.AreEqual("committed", result.OriginalCompletion);
        Assert.AreEqual("notCommitted", result.AttemptCompletion!.Outcome);
        Assert.IsFalse(result.AttemptCompletion.CleanupSucceeded);
        Assert.AreEqual("promote", result.Result!.Intent);
        Assert.AreEqual("9007199254740993", result.Result.Assignment!.Generation);
        Assert.AreEqual(ReleaseId, result.Result.Assignment.DefaultReleaseId);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationOverride>)result.Result.Assignment.Overrides)
            .Add(new("canary", ReleaseId)));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)result.AttemptCompletion.CleanupCodes!).Add("another.code"));
        using JsonDocument encoded = JsonDocument.Parse(ManagementConfigurationWire.EncodeResponse(result));
        Assert.IsFalse(encoded.RootElement.GetProperty("result").TryGetProperty("payloadJson", out _));

        var rejection = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(
            Bytes(Case("configuration-native-rejection-no-fabricated-value")));
        Assert.IsNotNull(rejection);
        Assert.IsNull(rejection.Result);
        Assert.AreEqual("resource.capacity", rejection.Rejection!.Code);
        Assert.AreEqual("rejected", rejection.KnownBusinessDecision.Value);
    }

    [TestMethod]
    public void UnassignedMapHasNoInventedGenerationAndWholeMapRejectsDuplicatesAndExcessOverrides()
    {
        var map = new ManagementConfigurationAssignmentMap("99999999-9999-9999-9999-999999999999", ApplicationId,
            "sample.settings", "1", "Development", "\"map-empty\"", []);
        var decoded = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationAssignmentMap>(ManagementConfigurationWire.EncodeResponse(map));
        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded.Generation);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(map with { Generation = "0" }));
        var duplicate = new ManagementConfigurationAssignmentCommand(1, "replace", ReleaseId,
            [new("canary", ReleaseId), new("canary", ReleaseId)], "Replace");
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(duplicate));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(duplicate with
        {
            Overrides = Enumerable.Range(0, 17).Select(index => new ManagementConfigurationOverride("cohort-" + index, ReleaseId)).ToArray()
        }));
    }

    [TestMethod]
    public void LocalZeroRevisionAndLargeSequencesRemainStringsAndFaultedSuccessIsHistorical()
    {
        var report = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationStatusReport>(Bytes(Case("configuration-statusreport-valid")));
        Assert.IsNotNull(report);
        Assert.AreEqual("9007199254740993", report.Sequence);
        Assert.AreEqual("0", report.Entries[0].Applied!.LocalRevision);
        var fault = report.Entries[0] with
        {
            State = "Faulted",
            LiveStateValid = false,
            Applied = null
        };
        Assert.IsNotNull(ManagementConfigurationWire.DecodeRequest<ManagementConfigurationStatusReport>(ManagementConfigurationWire.EncodeRequest(
            new ManagementConfigurationStatusReport("9223372036854775807", Time, [fault]))));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(
            new ManagementConfigurationStatusReport("1", Time, [fault with { Applied = fault.LastSuccessful }])));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(
            new ManagementConfigurationStatusReport("1", Time, [fault, fault])));
    }

    [TestMethod]
    public void DefinitionsAndNestedDeclarationsAreDetachedAndReadonly()
    {
        var definitions = ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationDefinition>>(
            Bytes(Case("configuration-definitionpage-valid")));
        Assert.IsNotNull(definitions);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationDefinition>)definitions.Items).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationMember>)definitions.Items[0].Shape.Members!).Clear());
    }

    [TestMethod]
    public void AssignmentPreviewRetainsExactCurrentPreconditionCompleteProposalAndPagedRecipientsWithoutMutation()
    {
        var proposal = new ManagementConfigurationAssignmentCommand(1, "rollback", ReleaseId,
            [new("canary", ReleaseId)], "Restore the previous release and retain a canary pin");
        var command = new ManagementConfigurationPreviewCommand(1, ApplicationId, "sample.settings", "1", Assignment: proposal);
        var decoded = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationPreviewCommand>(ManagementConfigurationWire.EncodeRequest(command));
        Assert.IsNotNull(decoded);
        Assert.AreEqual("rollback", decoded.Assignment!.Intent);
        Assert.IsNull(decoded.CommonSource);
        Assert.IsNull(decoded.ApplicationSource);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationOverride>)decoded.Assignment.Overrides).Clear());

        var current = new ManagementConfigurationAssignmentMap("99999999-9999-9999-9999-999999999999", ApplicationId,
            "sample.settings", "1", "Development", "\"current-map-7\"", [], ReleaseId, "7");
        var preview = new ManagementConfigurationPreview(ApplicationId, "sample.settings", "1", true, true,
            [new("overrides/canary", "retained")], [], current,
            new([new(RegistrationId, "reportedCompatible", "canary")], "opaque-next-recipient-page"), ProposedAssignment: proposal);
        var response = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationPreview>(ManagementConfigurationWire.EncodeResponse(preview));
        Assert.IsNotNull(response);
        Assert.AreEqual("\"current-map-7\"", response.CurrentAssignment.ETag);
        Assert.AreEqual("7", response.CurrentAssignment.Generation);
        Assert.AreEqual("rollback", response.ProposedAssignment!.Intent);
        Assert.AreEqual("canary", response.ProposedAssignment.Overrides.Single().CohortId);
        Assert.AreEqual("opaque-next-recipient-page", response.Recipients.NextCursor);
        Assert.IsTrue(response.Recipients.Items.Single().KnownCompatibility.IsKnown);
        Assert.IsFalse(new ManagementConfigurationPreviewRecipient(RegistrationId, "future-compatibility").KnownCompatibility.IsKnown);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationPreviewRecipient>)response.Recipients.Items).Clear());
        // A preview does not advance the map or fabricate a new generation.
        Assert.AreEqual(current, response.CurrentAssignment with
        {
            Overrides = current.Overrides
        });
    }

    [TestMethod]
    public void PreviewRequiresExactlyOneCompleteSourceOrAssignmentVariant()
    {
        var source = new ManagementConfigurationSourceReference("absent");
        var assignment = new ManagementConfigurationAssignmentCommand(1, "promote", ReleaseId, [], "Promote");
        var pair = new ManagementConfigurationPreviewCommand(1, ApplicationId, "sample.settings", "1", source, source);
        Assert.IsNotNull(ManagementConfigurationWire.DecodeRequest<ManagementConfigurationPreviewCommand>(ManagementConfigurationWire.EncodeRequest(pair)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(pair with { Assignment = assignment }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(pair with { ApplicationSource = null }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(pair with { CommonSource = null, ApplicationSource = null }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeRequest(pair with
        {
            CommonSource = null,
            ApplicationSource = null,
            Assignment = assignment with
            {
                Intent = "future-intent"
            }
        }));
    }

    [TestMethod]
    public void IssueAndProblemBoundsRejectWholeValuesAndFutureDecisionIntentRemainsUnknown()
    {
        ManagementConfigurationIssue[] issues = Enumerable.Range(0, 64)
            .Select(index => new ManagementConfigurationIssue("structural.failed", "member-" + index)).ToArray();
        var accepted = new ManagementConfigurationValidationResult(false, issues);
        Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationValidationResult>(
            ManagementConfigurationWire.EncodeResponse(accepted)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(accepted with
        {
            Issues = [.. issues, new("structural.failed", "member-65")]
        }));
        var problem = new ManagementConfigurationProblem("https://example.test/problems/configuration", "Rejected", 409,
            "configuration.rejected", new string('x', 1024));
        Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationProblem>(
            ManagementConfigurationWire.EncodeResponse(problem)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(problem with { Detail = new string('x', 1025) }));

        JsonObject original = JsonNode.Parse(Case("configuration-operationresult-valid"))!.AsObject();
        original["result"]!["intent"] = "future-intent";
        var decoded = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(Bytes(original.ToJsonString()));
        Assert.IsNotNull(decoded);
        Assert.AreEqual("future-intent", decoded.Result!.Intent);
        Assert.IsFalse(decoded.Result.KnownIntent.IsKnown);
        Assert.AreEqual("committed", decoded.OriginalCompletion);
        original["result"]!["intent"] = new string('x', 65);
        Assert.IsNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(Bytes(original.ToJsonString())));
    }

    [TestMethod]
    public void PublicContentRejectionPreservesFailedHttp200AndCannotBecomeSuccessOrProblemDetails()
    {
        ManagementConfigurationSnapshotRead identified = ManagementConfigurationWire.DecodeSnapshot(
            Bytes(Case("configuration-payload-digest-mismatch")), Target());
        Assert.IsNotNull(identified.Desired);
        var rejection = new ManagementConfigurationContentRejection(identified.Desired, "configuration.integrity");
        RestResult<ManagementConfigurationSnapshot> failed = RestResult<ManagementConfigurationSnapshot>.Failed(
            "configuration.integrity", "Invalid source content", new RestResultContext(200,
                failureKind: RestFailureKind.InvalidResponse, attemptCount: 1, duration: TimeSpan.FromMilliseconds(17)));
        var result = new ManagementConfigurationCallResult<ManagementConfigurationSnapshot>(failed, contentRejection: rejection);
        Assert.AreSame(failed, result.Exchange);
        Assert.AreEqual(200, result.Exchange.Context.HttpStatusCode);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.AreEqual(TimeSpan.FromMilliseconds(17), result.Exchange.Context.Duration);
        Assert.AreSame(rejection, result.ContentRejection);
        Assert.IsNotNull(result.ContentRejection);
        Assert.AreEqual(ReleaseId, result.ContentRejection.Desired.ReleaseId);
        Assert.IsNull(result.Problem);

        ManagementConfigurationSnapshot snapshot = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSnapshot>(
            Bytes(Case("configuration-snapshot-valid")))!;
        RestResult<ManagementConfigurationSnapshot> success = RestResult<ManagementConfigurationSnapshot>.FromValue(snapshot,
            new RestResultContext(200, hasValue: true));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementConfigurationCallResult<ManagementConfigurationSnapshot>(
            success, contentRejection: rejection));
        RestResult<ManagementConfigurationSnapshot> httpFailure = RestResult<ManagementConfigurationSnapshot>.Failed(
            "configuration.refused", "Refused", new RestResultContext(409, failureKind: RestFailureKind.Http));
        var problem = new ManagementConfigurationProblem("about:blank", "Refused", 409, "configuration.refused");
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementConfigurationCallResult<ManagementConfigurationSnapshot>(
            httpFailure, problem, contentRejection: rejection));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ManagementConfigurationContentRejection(null!, "configuration.integrity"));
        foreach (string code in new[] { "", "bad code", "bad\ncode", "é", new string('x', 129) })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new ManagementConfigurationContentRejection(identified.Desired, code));
        }
        Assert.AreEqual(new string('x', 128), new ManagementConfigurationContentRejection(identified.Desired, new string('x', 128)).Code);
    }

    [TestMethod]
    public void DiscoverySupportsCommonOnlyPublishersAndDistinctHostBindingsForOneImmutableSchema()
    {
        var shape = new ManagementConfigurationShape("object", false, []);
        var common = new ManagementConfigurationDefinition("shared.settings", "1", "44444444-4444-4444-4444-444444444444",
            "Development", "common", shape, "{}");
        var firstApplication = common with
        {
            SourceId = "55555555-5555-5555-5555-555555555555",
            Kind = "application",
            ApplicationId = ApplicationId,
            BindingId = "99999999-9999-9999-9999-999999999999",
            HostApplicationName = "FirstHost"
        };
        var secondApplication = firstApplication with
        {
            SourceId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            ApplicationId = AuthorityId,
            BindingId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            HostApplicationName = "AnotherHost"
        };
        var commonOnly = ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationDefinition>>(
            ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationDefinition>([common])));
        Assert.IsNotNull(commonOnly);
        Assert.AreEqual(common.SourceId, commonOnly.Items.Single().SourceId);
        Assert.IsNull(commonOnly.Items.Single().HostApplicationName);
        Assert.IsNull(commonOnly.Items.Single().ApplicationId);
        Assert.IsNull(commonOnly.Items.Single().BindingId);

        var authorizedRows = ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationDefinition>>(
            ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationDefinition>([common, firstApplication, secondApplication])));
        Assert.IsNotNull(authorizedRows);
        Assert.HasCount(3, authorizedRows.Items);
        Assert.AreEqual("FirstHost", authorizedRows.Items[1].HostApplicationName);
        Assert.AreEqual("AnotherHost", authorizedRows.Items[2].HostApplicationName);
        Assert.AreEqual(authorizedRows.Items[1].ConfigurationId, authorizedRows.Items[2].ConfigurationId);
        Assert.AreEqual(authorizedRows.Items[1].SchemaVersion, authorizedRows.Items[2].SchemaVersion);
        Assert.AreEqual(authorizedRows.Items[1].DefaultsJson, authorizedRows.Items[2].DefaultsJson);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(common with { HostApplicationName = "LeakedHost" }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(common with { ApplicationId = ApplicationId }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(firstApplication with { BindingId = null }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(firstApplication with { HostApplicationName = null }));
    }

    [TestMethod]
    public void MetadataOnlyDiscoveryOmitsDefaultsWithoutInventingEmptyDefaultsOrAcceptingExplicitNull()
    {
        var metadata = new ManagementConfigurationDefinition("shared.settings", "1", "44444444-4444-4444-4444-444444444444",
            "Development", "common", new ManagementConfigurationShape("object", false, []));
        byte[] bytes = ManagementConfigurationWire.EncodeResponse(metadata);
        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.IsFalse(document.RootElement.TryGetProperty("defaultsJson", out _));
        var decoded = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDefinition>(bytes);
        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded.DefaultsJson);
        Assert.AreEqual(metadata.SourceId, decoded.SourceId);

        JsonObject explicitNull = JsonNode.Parse(bytes)!.AsObject();
        explicitNull["defaultsJson"] = null;
        Assert.IsNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDefinition>(Bytes(explicitNull.ToJsonString())));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationWire.EncodeResponse(metadata with { DefaultsJson = "{\"a\":1,\"a\":2}" }));
        Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDefinition>(
            ManagementConfigurationWire.EncodeResponse(metadata with
            {
                DefaultsJson = "{}"
            })));
    }

    private static ManagementConfigurationSnapshotTarget Target()
        => new(AuthorityId, ApplicationId, "Development", RegistrationId, "sample.settings", "1", "ExampleHost", "canary");

    private static JsonObject SnapshotNode() => JsonNode.Parse(Case("configuration-snapshot-valid"))!.AsObject();

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Case(string id)
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllBytes(CatalogPath()));
        return catalog.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id)
            .GetProperty("json").GetString()!;
    }

    private static string CatalogPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "management", "v1", "configuration-cases.json");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("The independently authored configuration wire cases are required.");
    }
}
