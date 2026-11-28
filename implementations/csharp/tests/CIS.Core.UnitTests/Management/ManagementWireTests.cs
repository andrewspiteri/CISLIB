using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Management;
using CIS.Management.Wire;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementWireTests
{
    private const string Time = "2026-11-23T12:34:56.1234567Z";
    private const string ApplicationId = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
    private const string RegistrationId = "7d2222a6-2ffd-4010-bf15-bd22a8b70f4b";

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
    public void IndependentlyAuthoredExactWireCasesFollowTheFullRuntimeExpectation(string id, string schema,
        string direction, string json, bool valid)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        object? value = (schema, direction) switch
        {
            ("RegistrationRequest", "request") => ManagementWire.DecodeRequest<ManagementRegistrationRequest>(bytes),
            ("Observation", "request") => ManagementWire.DecodeRequest<ManagementObservation>(bytes),
            ("InventorySnapshot", "request") => ManagementWire.DecodeRequest<ManagementInventorySnapshot>(bytes),
            ("MetadataCommand", "request") => ManagementWire.DecodeRequest<ManagementMetadataCommand>(bytes),
            ("Application", "response") => ManagementWire.DecodeResponse<ManagementApplication>(bytes),
            ("EnvironmentEntry", "response") => ManagementWire.DecodeResponse<ManagementEnvironment>(bytes),
            ("EnvironmentPage", "response") => ManagementWire.DecodeResponse<ManagementPage<ManagementEnvironment>>(bytes),
            ("MetadataResult", "response") => ManagementWire.DecodeResponse<ManagementMetadataResult>(bytes),
            ("Problem", "response") => ManagementWire.DecodeResponse<ManagementProblem>(bytes),
            ("ControlConfirmation", "response") => ManagementWire.DecodeResponse<ManagementControlConfirmation>(bytes),
            ("ServiceInfo", "response") => ManagementWire.DecodeResponse<ManagementServiceInfo>(bytes),
            ("Capabilities", "response") => ManagementWire.DecodeResponse<ManagementCapabilities>(bytes),
            ("ServiceFeaturePage", "response") => ManagementWire.DecodeResponse<ManagementServiceFeaturePage>(bytes),
            ("ApplicationApiCapabilityPage", "response") => ManagementWire.DecodeResponse<ManagementApplicationApiCapabilityPage>(bytes),
            _ => throw new InvalidDataException("The independent catalog has a new shape requiring an explicit consumer.")
        };

        Assert.AreEqual(valid, value is not null, id);
    }

    [TestMethod]
    public void LargeValuesPreciseTimestampsAndUnknownEnumsRemainExactAndNonSuccessfulByDefault()
    {
        ManagementObservation? observation = ManagementWire.DecodeRequest<ManagementObservation>(Bytes(
            """{"sequence":"9007199254740993","observedAtUtc":"2026-11-23T12:34:56.1234567Z","lifecycle":"ready","health":"healthy"}"""));
        Assert.IsNotNull(observation);
        Assert.AreEqual("9007199254740993", observation.Sequence);
        Assert.AreEqual(Time, observation.ObservedAtUtc);

        ManagementMetadataResult original = Result("replayed", "future-completion");
        ManagementMetadataResult? decoded = ManagementWire.DecodeResponse<ManagementMetadataResult>(ManagementWire.EncodeResponse(original));
        Assert.IsNotNull(decoded);
        Assert.AreEqual("9007199254740993", decoded.Result!.MetadataRevision);
        Assert.AreEqual(Time, decoded.Result!.DecidedAtUtc);
        Assert.AreEqual("future-completion", decoded.OriginalCompletion);
        Assert.IsFalse(decoded.KnownOriginalCompletion.IsKnown);

        ManagementInstance instance = Instance() with
        {
            Presence = "未来 status"
        };
        ManagementInstance? future = ManagementWire.DecodeResponse<ManagementInstance>(ManagementWire.EncodeResponse(instance));
        Assert.IsNotNull(future);
        Assert.AreEqual("未来 status", future.Presence);
        Assert.IsFalse(future.KnownPresence.IsKnown);
    }

    [TestMethod]
    public void NativeCategoryTuplesAndResourceObservationsPreserveTheirDistinctFacts()
    {
        var owner = new ManagementOwnerObservation("owner-app", "process-one", "9007199254740993", Time, Time);
        var job = new ManagementJobInventory("orders", "refresh", "maintenance", "Refresh", ["continuous"],
            Description: "", EffectiveConfiguration: new("0", false),
            Observations: [new("resource-a", "continuous", "standby", ObservedOwner: owner),
                new("resource-b", "continuous", "running")], ResourceScopeDescription: "One resource");
        ManagementInventorySnapshot snapshot = Inventory() with
        {
            Categories = [new("orders", "maintenance", "Order maintenance", ""),
                new("billing", "maintenance", "Billing maintenance", "")],
            Jobs = [job]
        };

        ManagementInventorySnapshot? decoded = ManagementWire.DecodeRequest<ManagementInventorySnapshot>(ManagementWire.EncodeRequest(snapshot));

        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded.Categories);
        Assert.AreEqual("Order maintenance", decoded.Categories[0].DisplayName);
        Assert.AreEqual("Billing maintenance", decoded.Categories[1].DisplayName);
        Assert.AreEqual("0", decoded.Jobs[0].EffectiveConfiguration!.Revision);
        Assert.HasCount(2, decoded.Jobs[0].Observations!);
        Assert.AreEqual("9007199254740993", decoded.Jobs[0].Observations![0].ObservedOwner!.Generation);
        Assert.AreEqual("process-one", decoded.Jobs[0].Observations![0].ObservedOwner!.InstanceId);
        Assert.AreEqual(Time, decoded.Jobs[0].Observations![0].ObservedOwner!.ObservedAtUtc);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementCategoryInventory>)decoded.Categories)
            .Add(new("third", "maintenance", "Forbidden mutation")));
    }

    [TestMethod]
    public void RequestClosureAndResponseAdditionsRemainDifferentAtEveryKnownNestingLevel()
    {
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(Bytes(
            """{"commandVersion":1,"displayName":"Orders","future":true}""")));
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(Bytes(
            """{"sequence":"1","observedAtUtc":"2026-11-23T12:34:56.1234567Z","modules":[],"categories":[{"executionScope":"orders","categoryId":"a","displayName":"A","future":true}],"jobs":[]}""")));

        string application = """{"applicationId":"6d2222a6-2ffd-4010-bf15-bd22a8b70f4b","application":"orders","environment":"Production","metadata":{"displayName":"Orders","future":{"supported":true}},"metadataRevision":"1","metadataETag":"\"m1-1\"","future":null}""";
        ManagementApplication? decoded = ManagementWire.DecodeResponse<ManagementApplication>(Bytes(application));
        Assert.IsNotNull(decoded);
        Assert.AreEqual("Orders", decoded.Metadata.DisplayName);
    }

    [TestMethod]
    public void MetadataOmissionClearsWithoutUnicodeNormalizationAndExplicitNullIsRejected()
    {
        string decomposed = "Cafe\u0301";
        byte[] bytes = ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, decomposed));
        using JsonDocument wire = JsonDocument.Parse(bytes);

        Assert.IsFalse(wire.RootElement.TryGetProperty("description", out _));
        Assert.AreEqual(decomposed, wire.RootElement.GetProperty("displayName").GetString());
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(Bytes(
            """{"commandVersion":1,"displayName":"Orders","description":null}""")));
        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(Bytes(
            """{"commandVersion":1,"displayName":"Orders","description":""}""")));
    }

    [TestMethod]
    public void ScalarAndNativeByteBoundsDoNotTruncateOrSubstituteIdentity()
    {
        string scalarDisplay = string.Concat(Enumerable.Repeat("😀", 256));
        ManagementMetadataCommand? metadata = ManagementWire.DecodeRequest<ManagementMetadataCommand>(
            ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, scalarDisplay)));
        Assert.IsNotNull(metadata);
        Assert.AreEqual(scalarDisplay, metadata.DisplayName);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, scalarDisplay + "x")));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, "\ud800")));

        string nativeId = string.Concat(Enumerable.Repeat("é", 64));
        ManagementInventorySnapshot snapshot = Inventory() with
        {
            Categories = [new("orders", nativeId, "Category")]
        };
        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(ManagementWire.EncodeRequest(snapshot)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot with
        {
            Categories = [new("orders", nativeId + "é", "Category")]
        }));
    }

    [TestMethod]
    [DataRow("9007199254740993", true)]
    [DataRow("9223372036854775807", true)]
    [DataRow("9223372036854775808", false)]
    [DataRow("0", false)]
    [DataRow("01", false)]
    [DataRow("+1", false)]
    [DataRow("1.0", false)]
    [DataRow("１", false)]
    public void OrderedIntegersHaveCanonicalExactInt64Semantics(string value, bool valid)
    {
        Assert.AreEqual(valid, ManagementWire.IsOrderedInteger(value));
        Assert.IsTrue(ManagementWire.IsOrderedInteger("0", allowZero: true));
    }

    [TestMethod]
    [DataRow("2026-11-23T12:34:56.1234567Z", true)]
    [DataRow("2026-11-23T12:34:56.123456Z", false)]
    [DataRow("2026-11-23T12:34:56.1234567+00:00", false)]
    [DataRow("2026-02-30T12:34:56.1234567Z", false)]
    [DataRow("2026-11-23T12:34:60.1234567Z", false)]
    public void TimestampsRequirePreciseOriginalUtcTextAndARealCalendar(string value, bool valid)
        => Assert.AreEqual(valid, ManagementWire.IsTimestamp(value));

    [TestMethod]
    public void KeysEtagsAndCursorsHaveTheirFrozenFiniteOpaqueBounds()
    {
        Assert.IsTrue(ManagementWire.IsOperationKey("~._-aZ09"));
        Assert.IsTrue(ManagementWire.IsOperationKey(new string('a', 128)));
        Assert.IsFalse(ManagementWire.IsOperationKey(new string('a', 129)));
        Assert.IsFalse(ManagementWire.IsOperationKey("one:two"));
        Assert.IsFalse(ManagementWire.IsOperationKey("one two"));
        Assert.IsFalse(ManagementWire.IsOperationKey("."));
        Assert.IsFalse(ManagementWire.IsOperationKey(".."));
        Assert.IsTrue(ManagementWire.IsOperationKey("..."));
        Assert.IsTrue(ManagementWire.IsOperationKey(".leading"));
        Assert.IsTrue(ManagementWire.IsOperationKey("trailing.."));
        Assert.IsTrue(ManagementWire.IsStrongETag("\"" + new string('x', 254) + "\""));
        Assert.IsFalse(ManagementWire.IsStrongETag("\"" + new string('x', 255) + "\""));
        Assert.IsFalse(ManagementWire.IsStrongETag("W/\"m1-1\""));
        Assert.IsFalse(ManagementWire.IsStrongETag("*"));
        Assert.IsFalse(ManagementWire.IsStrongETag("\"a\",\"b\""));
        Assert.IsTrue(ManagementWire.IsPageRequest(new(500, new string('x', 2048))));
        Assert.IsFalse(ManagementWire.IsPageRequest(new(500, new string('x', 2049))));
        Assert.IsFalse(ManagementWire.IsPageRequest(new(501)));
    }

    [TestMethod]
    public void BodyByteAndDepthBoundsAreIndependentFromTypedFieldBounds()
    {
        var command = new ManagementMetadataCommand(1, "Orders");
        byte[] body = ManagementWire.EncodeRequest(command);
        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(body,
            new ManagementWireLimits(maximumRegularRequestBytes: body.Length)));
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(body, out ManagementWireFailure failure,
            new ManagementWireLimits(maximumRegularRequestBytes: body.Length - 1)));
        Assert.AreEqual(ManagementWireFailure.TooLarge, failure);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(command,
            new ManagementWireLimits(maximumRegularRequestBytes: body.Length - 1)));

        byte[] inventory = Bytes("""{"sequence":"1","observedAtUtc":"2026-11-23T12:34:56.1234567Z","modules":[{"kind":"future","version":"1","details":{"nested":{"more":{}}}}],"categories":[],"jobs":[]}""");
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(inventory, new ManagementWireLimits(maximumJsonDepth: 3)));
        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(inventory));
    }

    [TestMethod]
    public void UnknownOpaqueModuleDataIsDetachedAndRemainsData()
    {
        ManagementInventorySnapshot? decoded = ManagementWire.DecodeRequest<ManagementInventorySnapshot>(Bytes(
            """{"sequence":"1","observedAtUtc":"2026-11-23T12:34:56.1234567Z","modules":[{"kind":"future.module","version":"19","details":{"identifier":"9007199254740993"}}],"categories":[],"jobs":[]}"""));

        Assert.IsNotNull(decoded);
        Assert.AreEqual("future.module", decoded.Modules[0].Kind);
        Assert.AreEqual("9007199254740993", decoded.Modules[0].Details!.Value.GetProperty("identifier").GetString());
        byte[] encoded = ManagementWire.EncodeRequest(decoded);
        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(encoded));
    }

    [TestMethod]
    public void InventoryLimitsRefuseTheWholeReplacementAndKeepNativeCrossReferences()
    {
        ManagementInventorySnapshot snapshot = Inventory() with
        {
            Categories = [new("orders", "maintenance", "Maintenance")],
            Jobs = Enumerable.Range(0, 1024).Select(index => new ManagementJobInventory("orders", "job-" + index,
                "maintenance", "Job", ["continuous"])).ToArray()
        };
        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(ManagementWire.EncodeRequest(snapshot)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot with
        {
            Jobs = snapshot.Jobs.Append(new("orders", "extra-job", "maintenance", "Extra", ["continuous"])).ToArray()
        }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot with
        {
            Jobs = [new("other-scope", "job", "maintenance", "Unbound", ["continuous"])]
        }));
        ManagementJobInventory observed = snapshot.Jobs[0] with
        {
            Observations = Enumerable.Range(0, 4097).Select(index => new ManagementJobObservation("resource-" + index,
                "continuous", "standby")).ToArray()
        };
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot with { Jobs = [observed] }));
    }

    [TestMethod]
    public void NativeDescriptionsPermitExactlyEmptyButRejectNonemptyWhitespace()
    {
        var category = new ManagementCategoryInventory("orders", "maintenance", "Maintenance", "");
        var job = new ManagementJobInventory("orders", "refresh", "maintenance", "Refresh", ["continuous"], Description: "");
        ManagementInventorySnapshot snapshot = Inventory() with
        {
            Categories = [category],
            Jobs = [job]
        };

        Assert.IsNotNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(ManagementWire.EncodeRequest(snapshot)));
        Assert.IsTrue(ManagementWire.IsUtf8Text("", 2048, allowEmpty: true));
        Assert.IsFalse(ManagementWire.IsUtf8Text("", 2048));
        Assert.IsFalse(ManagementWire.IsUtf8Text(" ", 2048, allowEmpty: true));
        Assert.IsFalse(ManagementWire.IsUtf8Text("\u2003", 2048, allowEmpty: true));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot with
        {
            Categories = [category with { Description = " " }]
        }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot with
        {
            Jobs = [job with { Description = "\u2003" }]
        }));
        Assert.IsTrue(ManagementWire.IsScalarText(" ", 2048, allowEmpty: true));
    }

    [TestMethod]
    [DataRow("{\"commandVersion\":1,\"displayName\":\"A\",\"\\u0064isplayName\":\"B\"}")]
    [DataRow("{\"commandVersion\":1,\"displayName\":\"\\ud800\"}")]
    [DataRow("{/*comment*/\"commandVersion\":1,\"displayName\":\"A\"}")]
    [DataRow("{\"commandVersion\":1,\"displayName\":\"A\"}{}")]
    [DataRow("{\"commandVersion\":\"1\",\"displayName\":\"A\"}")]
    [DataRow("{\"commandVersion\":true,\"displayName\":\"A\"}")]
    public void InvalidLexicalJsonNeverAcquiresAnEquivalentNormalizedMeaning(string json)
        => Assert.IsNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(Bytes(json)));

    [TestMethod]
    public void InvalidUtf8AndUnsupportedVersionsAreExpectedWireFailures()
    {
        byte[] invalid = [.. Bytes("{\"commandVersion\":1,\"displayName\":\""), 0xc3, 0x28, .. Bytes("\"}")];
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(invalid, out ManagementWireFailure malformed));
        Assert.AreEqual(ManagementWireFailure.Invalid, malformed);
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementMetadataCommand>(Bytes(
            """{"commandVersion":2,"displayName":"Orders"}"""), out ManagementWireFailure version));
        Assert.AreEqual(ManagementWireFailure.UnsupportedVersion, version);
        Assert.ThrowsExactly<NotSupportedException>(() => ManagementWire.DecodeRequest<ManagementApplication>(Bytes("{}")));
    }

    [TestMethod]
    public void OriginalCommittedResultSurvivesCurrentReadOwnerCleanupAndCannotBeDowngraded()
    {
        ManagementMetadataResult value = Result("replayed", "committed") with
        {
            AttemptCompletion = new("notCommitted", false, ["transactions.cleanup_failed"])
        };
        ManagementMetadataResult? decoded = ManagementWire.DecodeResponse<ManagementMetadataResult>(ManagementWire.EncodeResponse(value));
        Assert.IsNotNull(decoded);
        Assert.AreEqual("committed", decoded.OriginalCompletion);
        Assert.AreEqual("notCommitted", decoded.AttemptCompletion!.Outcome);
        Assert.IsFalse(decoded.AttemptCompletion.CleanupSucceeded);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(Result("found", "notCommitted")));
    }

    [TestMethod]
    public void NativeRestFactsAndTheOriginalMutationKeyAreRetainedWithoutAnotherSuccessFlag()
    {
        var context = new RestResultContext(failureKind: RestFailureKind.Transport, attemptCount: 1);
        RestResult<ManagementMetadataResult> native = RestResult<ManagementMetadataResult>.Failed("client.transport_failed", "Unavailable", context);
        var result = new ManagementCallResult<ManagementMetadataResult>(native, operationKey: "original-key");

        Assert.AreSame(native, result.Exchange);
        Assert.AreSame(context, result.Exchange.Context);
        Assert.AreEqual("original-key", result.OperationKey);
        Assert.IsNull(result.Problem);
        Assert.IsNull(typeof(ManagementCallResult<ManagementMetadataResult>).GetProperty("Success"));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCallResult<ManagementMetadataResult>(native,
            new("about:blank", "Rejected", 503, "management.unavailable")));
    }

    [TestMethod]
    public void NativeApplicationVersionsPreserveAll512ScalarsAndReject513()
    {
        string version = string.Concat(Enumerable.Repeat("😀", 512));
        byte[] registration = ManagementWire.EncodeRequest(new ManagementRegistrationRequest(1, "reported", Time, version));
        Assert.AreEqual(version, ManagementWire.DecodeRequest<ManagementRegistrationRequest>(registration)!.ApplicationVersion);
        byte[] service = ManagementWire.EncodeResponse(new ManagementServiceInfo(version, [1], []));
        Assert.AreEqual(version, ManagementWire.DecodeResponse<ManagementServiceInfo>(service)!.ServiceVersion);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(new ManagementRegistrationRequest(1, "reported", Time, version + "x")));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(new ManagementServiceInfo(version + "x", [1], [])));
    }

    [TestMethod]
    public void ProblemDetailsPreserveTheir2048ScalarPortableBound()
    {
        string detail = string.Concat(Enumerable.Repeat("😀", 2048));
        var problem = new ManagementProblem("about:blank", "Unavailable", 503, "management.unavailable", detail);
        byte[] bytes = ManagementWire.EncodeResponse(problem);
        Assert.IsTrue(bytes.Length > 16 * 1024);
        Assert.IsTrue(bytes.Length < ManagementWire.MaximumErrorBodyBytes);
        Assert.AreEqual(detail, ManagementWire.DecodeResponse<ManagementProblem>(bytes)!.Detail);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(problem with { Detail = detail + "x" }));
    }

    [TestMethod]
    public void NativeRejectedResultsRetainOnlyTheOriginalErrorWithoutInventedMetadata()
    {
        var rejection = new ManagementOperationRejection("Business rejected / native", "Original\nrejection\tmessage");
        ManagementMetadataResult native = Result("replayed", "committed") with
        {
            Result = null,
            Rejection = rejection
        };
        byte[] bytes = ManagementWire.EncodeResponse(native);
        ManagementMetadataResult? decoded = ManagementWire.DecodeResponse<ManagementMetadataResult>(bytes);

        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded.Result);
        Assert.AreEqual(rejection, decoded.Rejection);
        Assert.AreEqual("rejected", decoded.KnownBusinessDecision.Value);
        Assert.IsTrue(decoded.KnownBusinessDecision.IsKnown);
        Assert.AreEqual("committed", decoded.OriginalCompletion);
        using JsonDocument wire = JsonDocument.Parse(bytes);
        Assert.IsFalse(wire.RootElement.TryGetProperty("result", out _));
        Assert.IsFalse(wire.RootElement.GetProperty("rejection").TryGetProperty("metadata", out _));

        JsonObject explicitNull = JsonNode.Parse(bytes)!.AsObject();
        explicitNull["result"] = null;
        Assert.IsNotNull(ManagementWire.DecodeResponse<ManagementMetadataResult>(Bytes(explicitNull.ToJsonString())));
        JsonObject appliedNull = JsonNode.Parse(ManagementWire.EncodeResponse(Result("found", "committed")))!.AsObject();
        appliedNull["rejection"] = null;
        Assert.IsNotNull(ManagementWire.DecodeResponse<ManagementMetadataResult>(Bytes(appliedNull.ToJsonString())));
    }

    [TestMethod]
    public void AResultCarrierRequiresExactlyOneNonNullOriginalBranch()
    {
        JsonObject carrier = JsonNode.Parse(ManagementWire.EncodeResponse(Result("found", "committed")))!.AsObject();
        carrier["rejection"] = JsonNode.Parse("""{"code":"Rejected / native","message":"Original rejection"}""");
        Assert.IsNull(ManagementWire.DecodeResponse<ManagementMetadataResult>(Bytes(carrier.ToJsonString())));
        carrier["result"] = null;
        carrier["rejection"] = null;
        Assert.IsNull(ManagementWire.DecodeResponse<ManagementMetadataResult>(Bytes(carrier.ToJsonString())));
        carrier.Remove("result");
        carrier.Remove("rejection");
        Assert.IsNull(ManagementWire.DecodeResponse<ManagementMetadataResult>(Bytes(carrier.ToJsonString())));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(Result("found", "committed") with { Result = null }));
    }

    [TestMethod]
    public void RejectionBoundsAreTheExistingNativeErrorBoundsNotDisplayMetadataRules()
    {
        string message = string.Concat(Enumerable.Repeat("😀", 256));
        var valid = new ManagementOperationRejection(new string('A', 127) + " ", message);
        ManagementOperationRejection? decoded = ManagementWire.DecodeResponse<ManagementOperationRejection>(ManagementWire.EncodeResponse(valid));
        Assert.AreEqual(valid, decoded);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(valid with { Message = message + "x" }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(valid with { Message = "original\0message" }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(valid with { Message = "   " }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(valid with { Code = new string('A', 129) }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(valid with { Code = "bad\ncode" }));
    }

    private static ManagementInventorySnapshot Inventory() => new("1", Time, [], [], []);

    private static ManagementMetadataResult Result(string source, string completion) => new(
        new("metadata.replace", "mgmt-meta.v1:" + new string('a', 64), "original-key"), source,
        new(1, ApplicationId, "applied", "management.metadata.applied", new("Orders"), "9007199254740993",
            "\"m1-9007199254740993\"", Time), completion);

    private static ManagementInstance Instance() => new(RegistrationId, ApplicationId, "reported-label",
        new("notProvided"), Time, "1.2.3", 1, Time, "active");

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string CatalogPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "management", "v1", "wire-cases.json");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("The independently authored management wire catalog is required.");
    }
}
