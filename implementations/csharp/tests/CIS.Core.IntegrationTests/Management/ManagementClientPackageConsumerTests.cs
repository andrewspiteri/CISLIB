using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.CommonTesting;
using CIS.Management;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[DoNotParallelize]
public sealed class ManagementClientPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PublicCoreAndCommonTestingPackagesComposeTypedManagementWithoutNativeProviders()
    {
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "Management.Client.Package",
            typeof(IManagementClient).Assembly, typeof(ScriptedManagementClient).Assembly);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "management-consumer")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        const string assemblyName = "CIS.Management.PublicPackageConsumer";
        File.WriteAllText(Path.Combine(app, "Program.cs"), ConsumerProgram);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.CommonTesting" }.Select(id => new XElement("PackageReference",
                    new XAttribute("Include", id), new XAttribute("Version", "[" + package.Version + "]")))))).Save(project);
        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        string[] restored = RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json"))).ToArray();
        string[] required = ["CIS.Core/" + package.Version, "CIS.CommonTesting/" + package.Version];
        string[] allowed = [.. required, "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        CollectionAssert.AreEquivalent(required, restored.Where(item => item.StartsWith("CIS.", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(restored.All(item => allowed.Contains(item, StringComparer.Ordinal)));
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        package.AssertDeployed(output, assemblyName);
        var run = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(output, assemblyName + ".dll")], package.Root);
        Assert.AreEqual("", run.StandardError);
        JsonObject report = JsonNode.Parse(run.StandardOutput)!.AsObject();
        foreach (string fact in new[] { "outsideFriend", "typedRead", "prefix", "nativeResultRetained", "scriptedUnknown",
            "originalKey", "noImplicitRecovery", "boundedSafeRecords", "noNativeProvider", "passiveProfile", "originalRejection",
            "snapshotPorts", "boundedSnapshotRecords", "passiveRegistration", "controlPort", "admissionPort", "reconciliationPort" })
        {
            Assert.IsTrue(report[fact]!.GetValue<bool>(), fact);
        }
        foreach (string fact in new[] { "configurationPort", "configurationOperationResults", "configurationOriginalKey",
            "noImplicitConfigurationRecovery", "boundedConfigurationRecords", "centralPairedRead", "oldConfigurationRouting",
            "configurationWireRoundtrip", "passiveConfigurationProfile" })
        {
            Assert.IsTrue(report[fact]!.GetValue<bool>(), fact);
        }
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CommonTesting.dll"), report["testingPath"]!.GetValue<string>());
    }

    private const string ConsumerProgram = """"
        using System.Reflection;
        using System.Text.Json;
        using CIS.CommonTesting;
        using CIS.Configuration;
        using CIS.Management;
        using CIS.Management.Hosting;
        using CIS.Management.Wire;
        using CIS.RestClient;
        using CIS.RestClient.Hosting;
        using CIS.Startup;
        using Microsoft.Extensions.DependencyInjection;

        const string id = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
        var options = new ManagementClientOptions("management") { BaseUrl = "https://management.invalid/tenant/control" };
        var nativeOptions = new RestClientOptions("management").ConfigureCisManagementRestClient(options);
        var rest = new ScriptedRestClient();
        rest.EnqueueJsonValue("""{"applicationId":"6d2222a6-2ffd-4010-bf15-bd22a8b70f4b","application":"orders","environment":"Production","metadata":{"displayName":"Orders"},"metadataRevision":"9007199254740993","metadataETag":"\"m1-9007199254740993\""}""");
        IManagementClient client = new ManagementClient(rest, options);
        ManagementCallResult<ManagementApplication> read = await client.GetApplicationAsync(id);
        var failure = RestResult<ManagementMetadataResult>.Failed("client.transport_failed", "Completion unknown",
            new RestResultContext(failureKind: RestFailureKind.Transport, attemptCount: 1));
        var unknown = new ManagementCallResult<ManagementMetadataResult>(failure, operationKey: "original-key");
        var scripted = new ScriptedManagementClient(1);
        scripted.EnqueueResult(ScriptedManagementCallKind.MetadataReplace, unknown);
        ManagementCallResult<ManagementMetadataResult> decision = await scripted.ReplaceMetadataAsync(id, "original-key", "\"m1-1\"", new(1, "Orders"));
        bool noImplicitRecovery = scripted.TotalCalls == 1;
        var rejected = new ManagementMetadataResult(new("metadata.replace", "mgmt-meta.v1:" + new string('a', 64), "original-key"),
            "found", null, "committed", Rejection: new("management.precondition_failed", "Original rejection"));
        scripted.EnqueueResult(ScriptedManagementCallKind.MetadataOperation, new ManagementCallResult<ManagementMetadataResult>(
            RestResult<ManagementMetadataResult>.FromValue(rejected, new RestResultContext(200, hasValue: true)), operationKey: "original-key"));
        ManagementCallResult<ManagementMetadataResult> recovered = await scripted.GetMetadataOperationAsync(id, "original-key");
        const string attemptId = "22222222-2222-4222-8222-222222222222";
        var controlScript = new ScriptedManagementClient(1);
        ManagementCallResult<ManagementControlConfirmation>? selectedControl = null;
        controlScript.EnqueueControl(call =>
        {
            var selected = new ManagementCallResult<ManagementControlConfirmation>(RestResult<ManagementControlConfirmation>.FromValue(
                new(id, call.RegistrationId!, "opaque-public-revision", true, 15000, call.AttemptId!),
                new RestResultContext(200, hasValue: true)));
            selectedControl = selected;
            return selected;
        });
        IManagementClient controlPort = controlScript;
        ManagementCallResult<ManagementControlConfirmation> control = await controlPort.GetControlAsync(id, attemptId);
        var manualAdmission = new ManualManagedWorkAdmission();
        IManagedWorkAdmission admission = manualAdmission;
        bool initiallyClosed = !admission.TryAdmit();
        int changes = 0;
        admission.Changed += (_, args) => { changes++; };
        manualAdmission.Set(new(ManagedWorkAdmissionState.Ready, true, 1, AuthorityId: id,
            RegistrationId: id, ControlRevision: "opaque-public-revision"));
        bool readyAdmission = admission.TryAdmit();
        ManagedWorkAdmissionSnapshot retainedAdmission = admission.CaptureSnapshot();
        manualAdmission.Set(new(ManagedWorkAdmissionState.ContinuingOnly, false, 2, "selected-test-outage",
            id, id, "opaque-public-revision"));
        IManagedStateReconciler reconciler = new ConsumerReconciler();
        bool reconciled = await reconciler.ReconcileAsync(control.Exchange.Value!);
        var observationScript = new ScriptedManagementObservationSource(1);
        var inventoryScript = new ScriptedManagementInventorySource(1);
        observationScript.EnqueueValue(new("ready", "healthy"));
        inventoryScript.EnqueueValue(new([new("jobs", "1")], [], []));
        IManagementObservationSource observationPort = observationScript;
        IManagementInventorySource inventoryPort = inventoryScript;
        ManagementObservationContent observation = observationPort.CaptureObservation();
        ManagementInventoryContent inventory = inventoryPort.CaptureInventory();
        var registration = new ManagementRegistrationOptions("management") { WireLimits = options.WireLimits };
        var services = new ServiceCollection();
        services.AddSingleton(observationPort);
        services.AddSingleton(inventoryPort);
        nativeOptions.LogCalls = false;
        services.AddCisRestClient(nativeOptions);
        services.AddCisManagementClient(options);
        services.AddCisManagementRegistration(registration);
        const string configurationKey = "configuration-original-key";
        const string configurationPayload = " {\"Greeting\":\"Café — 日本語\",\"Enabled\":false} ";
        const string publicationId = "33333333-3333-4333-8333-333333333333";
        const string releaseId = "44444444-4444-4444-8444-444444444444";
        var configurationTarget = new ManagementConfigurationSnapshotTarget(id, id, "Production", attemptId,
            "public.settings", "1", "PublicHost");
        string digest = ManagementConfigurationWire.ComputePayloadSha256(configurationPayload);
        var configurationSnapshot = new ManagementConfigurationSnapshot(
            new(id, id, "Production", attemptId, "public.settings", "1", "PublicHost", releaseId, "9007199254740993"),
            "\"public-snapshot\"", new("present", PublicationId: publicationId, Sha256: digest, PayloadJson: configurationPayload,
                SourceId: id, ConfigurationId: "public.settings", SchemaVersion: "1", Environment: "Production", Kind: "common"), new("absent"));
        var configurationResult = new ManagementConfigurationCallResult<ManagementConfigurationSnapshot>(
            RestResult<ManagementConfigurationSnapshot>.FromValue(configurationSnapshot, new RestResultContext(200, hasValue: true)));
        var configurationScript = new ScriptedManagementConfigurationClient(2);
        configurationScript.EnqueueSnapshot(expected => expected == configurationTarget ? configurationResult
            : throw new InvalidOperationException("The independent expected target must remain exact."));
        IManagementConfigurationClient configurationPort = configurationScript;
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> configurationRead =
            await configurationPort.GetRegistrationConfigurationAsync(configurationTarget);
        configurationScript.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot, configurationResult);
        await using var legacyStore = new LegacyConfigurationStore();
        var centralStore = new ManagementConfigurationStore(configurationPort, legacyStore);
        var enrollment = new ConfigurationEnrollment(typeof(PublicSettings), "public.settings", "1", ConfigurationUpdateMode.HotReload);
        var localTarget = new ConfigurationSnapshotTarget(id, id, "Production", attemptId, "PublicHost");
        IConfigurationSnapshotStore pairedPort = centralStore;
        ConfigurationSnapshotReadResult pair = await pairedPort.ReadSnapshotAsync(new(
            new("CommonConfiguration", enrollment.PersistedDefinition), new("PublicHost", enrollment.PersistedDefinition),
            enrollment: enrollment, expectedTarget: localTarget), CancellationToken.None);
        IConfigurationStore oldPort = centralStore;
        var oldKey = new ConfigurationRecordKey("PublicHost", "old.definition");
        ConfigurationReadResult oldRead = await oldPort.ReadAsync(oldKey, null, CancellationToken.None);
        await using (IAsyncDisposable subscription = await oldPort.WatchAsync([oldKey], _ => { }, CancellationToken.None))
        {
            // The subscription is owned while its supplied legacy store stays borrowed.
        }
        var configurationUnknown = new ManagementConfigurationCallResult<ManagementConfigurationOperationResult>(
            RestResult<ManagementConfigurationOperationResult>.Failed("client.transport_failed", "Original completion unknown",
                new RestResultContext(failureKind: RestFailureKind.Transport)), operationKey: configurationKey);
        configurationScript.EnqueueResult(ScriptedManagementConfigurationCallKind.ApplyCatalog, configurationUnknown);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> configurationDecision = await configurationPort.ApplyCatalogAsync(
            configurationKey, "\"catalog-1\"", new(1, digest, "Reviewed manifest"));
        bool noImplicitConfigurationRecovery = configurationScript.TotalCalls == 3;
        var originalConfigurationResult = new ManagementConfigurationOperationResult(
            new("management.configuration.catalog.apply", "actual-public-catalog-scope", configurationKey), "found",
            new(1, "catalogApplied", "applied", "configuration.catalog.applied", "2026-11-24T00:00:00.0000000Z",
                CatalogId: id, ManifestDigest: digest, ETag: "\"catalog-2\""), "committed");
        configurationScript.EnqueueResult(ScriptedManagementConfigurationCallKind.CatalogOperation,
            new ManagementConfigurationCallResult<ManagementConfigurationOperationResult>(RestResult<ManagementConfigurationOperationResult>.FromValue(
                originalConfigurationResult, new RestResultContext(200, hasValue: true)), operationKey: configurationKey));
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> configurationRecovery =
            await configurationPort.GetCatalogOperationAsync(configurationKey);
        var configurationOptions = new ManagementConfigurationClientOptions("configuration") { BaseUrl = "https://management.invalid/tenant/control" };
        var configurationNative = new RestClientOptions("configuration") { LogCalls = false };
        configurationNative.ConfigureCisManagementConfigurationRestClient(configurationOptions);
        services.AddCisRestClient(configurationNative);
        services.AddCisManagementConfigurationClient(configurationOptions);
        ManagementConfigurationSnapshotRead decodedSnapshot = ManagementConfigurationWire.DecodeSnapshot(
            ManagementConfigurationWire.EncodeResponse(configurationSnapshot), configurationTarget);
        string[] assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? "").ToArray();
        bool nativeProvider = assemblies.Any(name => name == "Npgsql" || name.StartsWith("RabbitMQ", StringComparison.Ordinal)
            || name.StartsWith("CIS.Security.Entra", StringComparison.Ordinal) || name.StartsWith("CIS.Security.Certificates", StringComparison.Ordinal));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outsideFriend = Assembly.GetExecutingAssembly().GetName().Name == "CIS.Management.PublicPackageConsumer",
            typedRead = read.Exchange.Success && read.Exchange.Value!.MetadataRevision == "9007199254740993",
            prefix = rest.Calls.Single().Url == "https://management.invalid/tenant/control/management/v1/applications/" + id,
            nativeResultRetained = ReferenceEquals(failure, decision.Exchange),
            scriptedUnknown = !decision.Exchange.Success && decision.Exchange.Context.FailureKind == RestFailureKind.Transport,
            originalKey = decision.OperationKey == "original-key" && scripted.Calls.Single().OperationKey == "original-key",
            noImplicitRecovery,
            originalRejection = recovered.Exchange.Value!.Result is null && recovered.Exchange.Value!.Rejection?.Message == "Original rejection",
            controlPort = ReferenceEquals(selectedControl, control) && control.Exchange.Value!.AttemptId == attemptId
                && controlScript.Calls.Single().AttemptId == attemptId && controlScript.Calls.Single().OperationKey is null,
            admissionPort = initiallyClosed && readyAdmission && !admission.TryAdmit()
                && retainedAdmission.AdmissionAllowed && changes == 2,
            reconciliationPort = reconciled && reconciler.IsReady,
            boundedSafeRecords = scripted.Calls.Count == 1 && typeof(ScriptedManagementCall).GetProperty("Body") is null,
            noNativeProvider = !nativeProvider,
            passiveProfile = nativeOptions.Codec is ManagementContentCodec && nativeOptions.RetryPolicy.PolicyName is null,
            snapshotPorts = observation.Lifecycle == "ready" && observation.Health == "healthy"
                && inventory.Modules.Single().Kind == "jobs" && inventory.Modules.Single().Version == "1",
            boundedSnapshotRecords = observationScript.Captures.Single().Kind == "observation"
                && inventoryScript.Captures.Single().Kind == "inventory"
                && typeof(ScriptedManagementCapture).GetProperty("Body") is null
                && typeof(ScriptedManagementCapture).GetProperty("Payload") is null,
            passiveRegistration = services.Count(item => item.ServiceType == typeof(ManagementRegistration)) == 1
                && services.Count(item => item.ServiceType == typeof(IStartable)) == 1
                && observationScript.TotalCaptures == 1 && inventoryScript.TotalCaptures == 1
                && !registration.RequireInitialRegistration && registration.ReportingInterval == TimeSpan.FromSeconds(30),
            configurationPort = ReferenceEquals(configurationResult, configurationRead)
                && configurationRead.Exchange.Value!.Desired.Generation == "9007199254740993",
            configurationOperationResults = ReferenceEquals(originalConfigurationResult, configurationRecovery.Exchange.Value)
                && configurationRecovery.Exchange.Value!.KnownBusinessDecision.IsKnown
                && configurationRecovery.Exchange.Value.KnownBusinessDecision.Value == "applied",
            configurationOriginalKey = ReferenceEquals(configurationUnknown, configurationDecision)
                && configurationDecision.OperationKey == configurationKey
                && configurationScript.Calls.Last().OperationKey == configurationKey,
            noImplicitConfigurationRecovery,
            boundedConfigurationRecords = configurationScript.Calls.Count == 2 && configurationScript.TotalCalls == 4
                && configurationScript.DroppedCalls == 2
                && typeof(ScriptedManagementConfigurationCall).GetProperty("Payload") is null
                && typeof(ScriptedManagementConfigurationCall).GetProperty("ETag") is null,
            centralPairedRead = pair.Kind == ConfigurationSnapshotReadKind.Complete
                && pair.Common!.Payload!.Value.GetProperty("Greeting").GetString() == "Café — 日本語"
                && pair.Application!.Kind == ConfigurationReadKind.Missing
                && pair.Provenance!.Desired.AssignmentGeneration == "9007199254740993"
                && pair.Provenance.CommonSource!.Sha256 == digest,
            oldConfigurationRouting = oldRead.Kind == ConfigurationReadKind.Missing && legacyStore.Reads == 1
                && legacyStore.Watches == 1 && legacyStore.SubscriptionDisposals == 1 && legacyStore.Disposals == 0
                && oldPort.NotificationMode == ConfigurationNotificationMode.RequiresReconciliation,
            configurationWireRoundtrip = decodedSnapshot.Snapshot?.CommonSource.PayloadJson == configurationPayload
                && decodedSnapshot.Desired?.RegistrationId == attemptId,
            passiveConfigurationProfile = configurationNative.Codec is ManagementConfigurationContentCodec
                && configurationNative.Operations.SequenceEqual(new[] { "configuration" })
                && configurationNative.RetryPolicy.PolicyName is null
                && services.Count(item => item.ServiceType == typeof(IStartable)) == 1,
            corePath = typeof(IManagementClient).Assembly.Location,
            testingPath = typeof(ScriptedManagementClient).Assembly.Location
        }));

        sealed class ConsumerReconciler : IManagedStateReconciler
        {
            public bool IsReady => true;
            public Task<bool> ReconcileAsync(ManagementControlConfirmation confirmation, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(confirmation.AdmissionAllowed && confirmation.ControlRevision == "opaque-public-revision");
            }
        }

        sealed class PublicSettings : ConfigurationObject
        {
            public string Greeting { get; set; } = "default";
            public bool Enabled { get; set; } = true;
        }

        sealed class LegacyConfigurationStore : IConfigurationStore, IAsyncDisposable
        {
            public int Reads { get; private set; }
            public int Watches { get; private set; }
            public int SubscriptionDisposals { get; private set; }
            public int Disposals { get; private set; }
            public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.Reliable;
            public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? token, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Reads++;
                return ValueTask.FromResult(ConfigurationReadResult.Missing());
            }
            public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
                Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Watches++;
                return ValueTask.FromResult<IAsyncDisposable>(new Subscription(this));
            }
            public ValueTask DisposeAsync()
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
            private sealed class Subscription(LegacyConfigurationStore owner) : IAsyncDisposable
            {
                public ValueTask DisposeAsync()
                {
                    owner.SubscriptionDisposals++;
                    return ValueTask.CompletedTask;
                }
            }
        }
        """";
}
