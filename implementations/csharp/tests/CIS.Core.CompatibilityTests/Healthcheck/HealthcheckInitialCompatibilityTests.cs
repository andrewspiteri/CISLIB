using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.Healthcheck.HealthcheckCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests.Healthcheck;

// This is the current initial candidate, not replay of an invented released version.
[TestClass]
[DoNotParallelize]
public sealed class HealthcheckInitialCompatibilityTests
{
    private static readonly string[] EnvironmentKeys =
    [
        "CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS", "CIS_HEALTHCHECK_MAX_MONITORS",
        "CIS_HEALTHCHECK_DETAILS_ENABLED", "CIS_HEALTHCHECK_PIPE_ENABLED", "CIS_HEALTHCHECK_PIPE_NAME",
        "CIS_HEALTHCHECK_PIPE_MAX_CLIENTS", "CIS_HEALTHCHECK_PIPE_TIMEOUT_MS", "CIS_HEALTHCHECK_MAX_REPORT_BYTES"
    ];

    public static IEnumerable<object[]> ApiCases => Api()["types"]!.AsArray().Select(item => new object[] { item!["name"]!.GetValue<string>() });
    public static IEnumerable<object[]> ConfigurationCases => HealthcheckCompatibilityInputs.Configuration()["cases"]!.AsArray().Select(item => new object[] { item!["id"]!.GetValue<string>() });

    [ClassInitialize]
    public static void CheckFrozenInputs(TestContext _) => FrozenInputs();

    [ClassCleanup]
    public static void CheckFrozenInputsAfterExecution() => FrozenInputs();

    [TestMethod]
    [DynamicData(nameof(ApiCases))]
    public void FrozenPublicContractIsPreserved(string name)
    {
        JsonNode expected = Api()["types"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == name)!;
        Type type = Core.GetType("CIS.Healthcheck." + name, throwOnError: true)!;
        Assert.IsTrue(type.IsPublic);
        Assert.AreEqual(expected["kind"]!.GetValue<string>(), Kind(type));
        if (expected["base"] is JsonNode baseType) { Assert.AreEqual(baseType.GetValue<string>(), TypeName(type.BaseType!)); }
        if (expected["interfaces"] is JsonArray interfaces)
        {
            RequireMembers(interfaces.Select(item => item!.GetValue<string>()), type.GetInterfaces().Select(item => TypeName(item)));
        }
        RequireMembers(expected["members"]!.AsArray().Select(item => item!.GetValue<string>()), Members(type));
    }

    [TestMethod]
    [DynamicData(nameof(ConfigurationCases))]
    public void FrozenConfigurationBehaviorIsPreserved(string id)
    {
        JsonObject fixture = HealthcheckCompatibilityInputs.Configuration();
        JsonNode test = fixture["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
        HealthcheckOptions? setup = test["setup"]?.Deserialize<HealthcheckOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        WithEnvironment(test["environment"]!.AsObject(), () =>
        {
            if (test["issues"] is JsonArray issues)
            {
                CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => HealthcheckOptions.Resolve(setup));
                string[] expected = issues.Select(item => item!["key"]!.GetValue<string>() + "=" + item["code"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
                string[] actual = error.Issues.Select(issue => issue.Key + "=" + issue.Code).Order(StringComparer.Ordinal).ToArray();
                CollectionAssert.AreEqual(expected, actual, id);
            }
            else
            {
                JsonObject expected = fixture["defaults"]!.DeepClone().AsObject();
                foreach ((string key, JsonNode? value) in test["expectedOverrides"]!.AsObject()) { expected[key] = value?.DeepClone(); }
                HealthcheckOptions result = HealthcheckOptions.Resolve(setup);
                Assert.IsTrue(Same(expected, ObserveOptions(result)), id);
                Assert.AreNotSame(setup, result, "Resolution returns detached settings.");
            }
        });
    }

    [TestMethod]
    public async Task RuntimeConstructionUsesSuppliedOptionsWithoutReadingDeploymentOverrides()
    {
        HealthcheckService? service = null;
        try
        {
            WithEnvironment(new JsonObject { ["CIS_HEALTHCHECK_MAX_MONITORS"] = "invalid" }, () =>
            {
                service = new HealthcheckService(new HealthcheckOptions { MaxMonitors = 1 }, Identity(), new FrozenClock());
                IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
                HealthcheckCapacityException error = Assert.ThrowsExactly<HealthcheckCapacityException>(() => service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true }));
                Assert.AreEqual(1, error.Limit);
                Assert.AreEqual("Healthcheck.MonitorCapacity", error.Code);
                Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
                GC.KeepAlive(monitor);
            });
        }
        finally
        {
            if (service is not null) { await service.DisposeAsync(); }
        }
    }

    [TestMethod]
    public void OptionalInputDefaultsRetainTheirContractValues()
    {
        var collector = new HealthCollectorOptions();
        Assert.IsNull(collector.Interval);
        Assert.IsTrue(collector.IsRequired);
        var binding = new HealthExecutionBindingOptions { RegistrationId = Guid.Parse("30000000-0000-0000-0000-000000000001") };
        Assert.IsFalse(binding.SuccessPublishesUsableData);
        Assert.IsNull(binding.Interval);
        Assert.IsNull(binding.InitialDelay);
        Assert.IsNull(binding.DisplayName);
        var condition = new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false };
        Assert.IsNull(condition.Name);
        Assert.IsNull(condition.InitialDiagnostic);
        var diagnostic = new HealthDiagnostic();
        Assert.IsNull(diagnostic.Code);
        Assert.IsNull(diagnostic.Message);
        Assert.IsNull(diagnostic.ExceptionType);
        Assert.IsFalse(diagnostic.IsTruncated);
        HealthMeasurement metric = HealthMeasurement.Unavailable("latency", FrozenClock.Instant);
        Assert.IsNull(metric.Unit);
        Assert.IsTrue(metric.IsDiagnostic);
    }

    [TestMethod]
    [DataRow(HealthReportView.Health, "pending-health.json", true, false)]
    [DataRow(HealthReportView.Readiness, "pending-readiness.json", false, false)]
    [DataRow(HealthReportView.Readiness, "pipe-readiness-response.json", false, true)]
    public async Task ActualFormatterPreservesFrozenHc00WireObservations(HealthReportView view, string filename, bool healthy, bool pipe)
    {
        await using var service = new HealthcheckService(new HealthcheckOptions(), Identity(), new FrozenClock());
        IAvailabilityMonitor pending = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        HealthSnapshot snapshot = service.CaptureSnapshot();
        HealthReportBuffer buffer = pipe ? HealthReportFormatter.FormatPipeResponse(snapshot, view, new HealthcheckOptions()) :
            HealthReportFormatter.FormatSnapshot(snapshot, view, new HealthcheckOptions());
        Assert.IsFalse(buffer.IsError);
        Assert.IsNull(buffer.ErrorCode);
        Assert.AreEqual(healthy, buffer.SelectedHealthy);
        Assert.IsFalse(buffer.Utf8Bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.IsTrue(Same(JsonNode.Parse(Wire("valid", filename)), JsonNode.Parse(buffer.Utf8Bytes.Span)), filename);
        Assert.AreEqual(snapshot.Revision, service.CaptureSnapshot().Revision, "Reporting cannot mutate the observed revision.");
        GC.KeepAlive(pending);
    }

    [TestMethod]
    [DataRow("pending-health.json", HealthReportView.Health, true)]
    [DataRow("pending-readiness.json", HealthReportView.Readiness, false)]
    [DataRow("healthy-after-startup-details.json", HealthReportView.Details, true)]
    [DataRow("unhealthy-health.json", HealthReportView.Health, false)]
    public void ActualReaderAcceptsFrozenSnapshotAndPrecisionFixtures(string filename, HealthReportView view, bool healthy)
    {
        byte[] frozen = Wire("valid", filename);
        object response = InvokeProtocol("ParseResponse", (ReadOnlyMemory<byte>)Envelope(frozen, WireView(view)), view, 4_194_304);
        Assert.IsFalse(Property<bool>(response, "IsError"));
        Assert.AreEqual(healthy, Property<bool>(response, "SelectedHealthy"));
        Assert.IsTrue(Same(JsonNode.Parse(frozen), JsonNode.Parse(Property<ReadOnlyMemory<byte>>(response, "SnapshotUtf8").Span)));
        if (view == HealthReportView.Details)
        {
            JsonNode actual = JsonNode.Parse(Property<ReadOnlyMemory<byte>>(response, "SnapshotUtf8").Span)!;
            Assert.AreEqual("9223372036854775807", actual["monitors"]![1]!["measurements"]![0]!["value"]!.GetValue<string>());
        }
    }

    [TestMethod]
    [DataRow("ready-contradiction.json")]
    [DataRow("noncanonical-revision.json")]
    [DataRow("aggregate-contradiction.json")]
    public void ActualReaderRejectsFrozenContradictorySnapshots(string filename) => Assert.ThrowsExactly<FormatException>(() =>
        InvokeProtocol("ParseResponse", (ReadOnlyMemory<byte>)Envelope(Wire("invalid", filename), "readiness"), HealthReportView.Readiness, 4_194_304));

    [TestMethod]
    public void ActualReaderRejectsFrozenResponseViewMismatch() => Assert.ThrowsExactly<FormatException>(() =>
        InvokeProtocol("ParseResponse", (ReadOnlyMemory<byte>)Wire("invalid", "view-mismatch-response.json"), HealthReportView.Readiness, 4_194_304));

    [TestMethod]
    public void ActualReaderAcceptsFrozenInvalidRequestErrorWithoutHealthySelection()
    {
        object response = InvokeProtocol("ParseResponse", (ReadOnlyMemory<byte>)Wire("valid", "pipe-invalid-request-response.json"), HealthReportView.Health, 4_194_304);
        Assert.IsTrue(Property<bool>(response, "IsError"));
        Assert.AreEqual("InvalidRequest", Property<string>(response, "ErrorCode"));
        Assert.IsFalse(Property<bool>(response, "SelectedHealthy"));
        Assert.AreEqual(0, Property<ReadOnlyMemory<byte>>(response, "SnapshotUtf8").Length);
    }

    [TestMethod]
    [DataRow("valid", "pipe-readiness-request.json", true, HealthReportView.Readiness, null)]
    [DataRow("valid", "pipe-additive-request.json", true, HealthReportView.Health, null)]
    [DataRow("invalid", "unsupported-version-request.json", false, HealthReportView.Health, "UnsupportedVersion")]
    [DataRow("invalid", "duplicate-properties-request.txt", false, HealthReportView.Health, "InvalidRequest")]
    public void ActualReaderPreservesFrozenRequestDisposition(string directory, string filename, bool accepted, HealthReportView view, string? code)
    {
        object?[] arguments = [(ReadOnlyMemory<byte>)Wire(directory, filename), default(HealthReportView), null];
        Assert.AreEqual(accepted, (bool)InvokeProtocol("TryParseRequest", arguments));
        Assert.AreEqual(code, arguments[2]);
        if (accepted) { Assert.AreEqual(view, arguments[1]); }
    }

    [TestMethod]
    [DataRow("protocol-version")]
    [DataRow("schema-version")]
    [DataRow("numeric-revision")]
    [DataRow("duplicate-additive")]
    [DataRow("nested-depth")]
    public void ActualReaderRejectsDeliberateAdditiveAndVersionRegressions(string change)
    {
        string payload = Encoding.UTF8.GetString(Wire("valid", "pipe-readiness-response.json"));
        JsonObject parsed = JsonNode.Parse(payload)!.AsObject();
        switch (change)
        {
            case "protocol-version": parsed["protocolVersion"] = 2; payload = parsed.ToJsonString(); break;
            case "schema-version": parsed["schemaVersion"] = 2; payload = parsed.ToJsonString(); break;
            case "numeric-revision": parsed["snapshot"]!["revision"] = 1; payload = parsed.ToJsonString(); break;
            case "duplicate-additive": payload = payload.TrimEnd().TrimEnd('}') + ",\"new\":{\"x\":1,\"x\":2}}"; break;
            case "nested-depth":
                string nested = "0";
                for (int i = 0; i < 16; i++) { nested = "{\"x\":" + nested + "}"; }
                payload = payload.TrimEnd().TrimEnd('}') + ",\"new\":" + nested + "}";
                break;
        }
        Assert.ThrowsExactly<FormatException>(() => InvokeProtocol("ParseResponse", (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(payload), HealthReportView.Readiness, 4_194_304));
    }

    [TestMethod]
    public void ActualReaderAllowsUnknownOptionalFieldsWithinExistingBounds()
    {
        JsonObject payload = JsonNode.Parse(Wire("valid", "pipe-readiness-response.json"))!.AsObject();
        payload["optionalFuture"] = new JsonObject { ["value"] = true };
        payload["snapshot"]!["optionalFuture"] = "retained compatibility";
        object response = InvokeProtocol("ParseResponse", (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(payload.ToJsonString()), HealthReportView.Readiness, 4_194_304);
        Assert.IsFalse(Property<bool>(response, "IsError"));
        Assert.IsFalse(Property<bool>(response, "SelectedHealthy"));
    }

    [TestMethod]
    public async Task FrozenTruncatedFrameCannotBeAcceptedAsACompleteRequest()
    {
        byte[] payload = Wire("invalid", "truncated-request.txt");
        byte[] framed = new byte[4 + payload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(framed, (uint)payload.Length + 1);
        payload.CopyTo(framed, 4);
        using var stream = new MemoryStream(framed);
        Task<byte[]> read = (Task<byte[]>)InvokeProtocol("ReadFrameAsync", stream, 512, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<FormatException>(async () => await read);
    }

    [TestMethod]
    public void EmptyPublicationLedgerRemainsDistinctFromInitialCandidateChecks()
    {
        JsonObject ledger = Read(Path.Combine(Repository(), "compatibility/csharp/publications/ledger.json"));
        Assert.AreEqual("cislib.csharp.publications", ledger["registryId"]!.GetValue<string>());
        Assert.AreEqual(0, ledger["publications"]!.AsArray().Count, "Reconcile a real publication separately; initial fixtures are not released history.");
    }

    private static string WireView(HealthReportView view) => view switch
    {
        HealthReportView.Health => "health",
        HealthReportView.Readiness => "readiness",
        HealthReportView.Details => "details",
        _ => throw new ArgumentOutOfRangeException(nameof(view))
    };

    private static JsonObject ObserveOptions(HealthcheckOptions options) => new()
    {
        ["maxConcurrentCollections"] = options.MaxConcurrentCollections,
        ["maxMonitors"] = options.MaxMonitors,
        ["detailsEnabled"] = options.DetailsEnabled,
        ["pipeEnabled"] = options.PipeEnabled,
        ["pipeName"] = options.PipeName,
        ["pipeMaxClients"] = options.PipeMaxClients,
        ["pipeRequestTimeoutMs"] = options.PipeRequestTimeoutMs,
        ["maxReportBytes"] = options.MaxReportBytes
    };

    private static void WithEnvironment(JsonObject values, Action action)
    {
        Dictionary<string, string?> before = EnvironmentKeys.ToDictionary(key => key, System.Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (string key in EnvironmentKeys) { System.Environment.SetEnvironmentVariable(key, values[key]?.GetValue<string>()); }
            action();
        }
        finally
        {
            foreach ((string key, string? value) in before) { System.Environment.SetEnvironmentVariable(key, value); }
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static HealthApplicationIdentity Identity() => new()
    {
        Name = "healthcheck-foundation-fixture",
        BuildVersion = "0.1.0-dev.1",
        ServiceVersion = "pilot",
        InstanceId = Guid.Parse("10000000-0000-0000-0000-000000000001")
    };

    private sealed class FrozenClock : TimeProvider
    {
        internal static readonly DateTimeOffset Instant = new(2026, 10, 20, 8, 1, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Instant;
    }
}
