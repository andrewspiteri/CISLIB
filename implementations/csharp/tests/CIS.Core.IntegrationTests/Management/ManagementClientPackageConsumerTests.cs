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
            "snapshotPorts", "boundedSnapshotRecords", "passiveRegistration" })
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
        using CIS.Management;
        using CIS.Management.Hosting;
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
            corePath = typeof(IManagementClient).Assembly.Location,
            testingPath = typeof(ScriptedManagementClient).Assembly.Location
        }));
        """";
}
