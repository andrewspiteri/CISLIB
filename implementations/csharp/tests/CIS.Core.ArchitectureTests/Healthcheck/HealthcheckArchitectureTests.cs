using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests.Healthcheck;

[TestClass]
public sealed class HealthcheckArchitectureTests
{
    private static readonly Lazy<Task<JsonObject>> Inspection = new(InspectAsync);

    [TestMethod]
    public async Task ActualCoreMetadataPreservesApprovedHealthcheckPublicAndDependencyBoundaries()
    {
        JsonObject facts = await Current();
        HealthcheckArchitecturePolicy.Assert(facts);
        Assert.AreEqual(54, HealthcheckArchitecturePolicy.PublicNames.Length);
    }

    [TestMethod]
    [DataRow("assembly", "HC-ARCH-ASSEMBLY")]
    [DataRow("missing-public-type", "HC-ARCH-PUBLIC-SURFACE")]
    [DataRow("additional-public-type", "HC-ARCH-PUBLIC-SURFACE")]
    [DataRow("exposed-internal-type", "HC-ARCH-PUBLIC-SURFACE")]
    [DataRow("no-body-facts", "HC-ARCH-INSPECTION")]
    [DataRow("service-http-signature", "HC-ARCH-HTTP-DIRECTION")]
    [DataRow("collector-http-body", "HC-ARCH-HTTP-DIRECTION")]
    [DataRow("state-di-field", "HC-ARCH-INFRASTRUCTURE")]
    [DataRow("binder-heartbeat-body", "HC-ARCH-INFRASTRUCTURE")]
    [DataRow("collector-startup-body", "HC-ARCH-INFRASTRUCTURE")]
    [DataRow("http-di-body", "HC-ARCH-INFRASTRUCTURE")]
    [DataRow("policy-pipe-field", "HC-ARCH-POLICY-TRANSPORT")]
    [DataRow("collector-network-body", "HC-ARCH-POLICY-TRANSPORT")]
    [DataRow("binder-pipe-server", "HC-ARCH-POLICY-TRANSPORT")]
    [DataRow("http-constructs-state", "HC-ARCH-TRANSPORT-OWNER")]
    [DataRow("pipe-owns-service", "HC-ARCH-TRANSPORT-OWNER")]
    [DataRow("pipe-owns-collector", "HC-ARCH-TRANSPORT-OWNER")]
    [DataRow("http-factory-capability", "HC-ARCH-TRANSPORT-OWNER")]
    [DataRow("unsealed-snapshot", "HC-ARCH-SNAPSHOT-SHAPE")]
    [DataRow("public-snapshot-constructor", "HC-ARCH-SNAPSHOT-MUTATOR")]
    [DataRow("public-snapshot-setter", "HC-ARCH-SNAPSHOT-MUTATOR")]
    [DataRow("writable-snapshot-field", "HC-ARCH-SNAPSHOT-FIELD")]
    [DataRow("borrowed-snapshot-array", "HC-ARCH-SNAPSHOT-OWNERSHIP")]
    [DataRow("retained-snapshot-exception", "HC-ARCH-SNAPSHOT-OWNERSHIP")]
    [DataRow("retained-snapshot-monitor", "HC-ARCH-SNAPSHOT-OWNERSHIP")]
    public async Task IndependentCompiledFactControlsRejectOneSpecificBoundaryViolation(string change, string diagnostic)
    {
        JsonObject facts = await Current();
        HealthcheckArchitecturePolicy.Assert(facts);
        JsonObject snapshot = Type(facts, "HealthSnapshot");
        switch (change)
        {
            case "assembly": facts["name"] = "CIS.Healthcheck.Adapter"; break;
            case "missing-public-type": facts["types"]!.AsArray().Remove(snapshot); break;
            case "additional-public-type":
                var additional = snapshot.DeepClone().AsObject();
                additional["name"] = "CIS.Healthcheck.AccidentalPublicService";
                facts["types"]!.AsArray().Add(additional);
                break;
            case "exposed-internal-type": Type(facts, "Internal.HealthStateCoordinator")["publicApi"] = true; break;
            case "no-body-facts": facts["typeUses"] = new JsonArray(); break;
            case "service-http-signature": AddUse(facts, "HealthcheckService:method:.ctor", "Microsoft.AspNetCore.Http", "HttpContext"); break;
            case "collector-http-body": AddUse(facts, "HealthCollectorRunner+<Run>d__1:method:MoveNext", "Microsoft.AspNetCore.Http", "HttpContext"); break;
            case "state-di-field": AddUse(facts, "Internal.HealthStateCoordinator:field:resolver", "Microsoft.Extensions.DependencyInjection", "IServiceCollection"); break;
            case "binder-heartbeat-body": AddUse(facts, "HealthExecutionBinder+ExecutionBinding:method:Report", "CIS.Heartbeat", "Scheduler"); break;
            case "collector-startup-body": AddUse(facts, "HealthCollectorRunner:method:StartAsync", "CIS.Startup", "IStartable"); break;
            case "http-di-body": AddUse(facts, "AspNetCore.HealthcheckApplicationBuilderExtensions:method:HandleAsync", "Microsoft.Extensions.DependencyInjection", "ServiceProviderServiceExtensions"); break;
            case "policy-pipe-field": AddUse(facts, "Internal.HealthMonitorEntry:field:endpoint", "System.IO.Pipes", "NamedPipeServerStream"); break;
            case "collector-network-body": AddUse(facts, "HealthCollectorRunner:method:StartAsync", "System.Net.Http", "HttpClient"); break;
            case "binder-pipe-server": AddUse(facts, "HealthExecutionBinder:method:Bind", "CIS.Healthcheck", "HealthPipeServer"); break;
            case "http-constructs-state": AddUse(facts, "AspNetCore.HealthcheckApplicationBuilderExtensions:method:HandleAsync", "CIS.Healthcheck.Internal", "HealthStateCoordinator"); break;
            case "pipe-owns-service": AddUse(facts, "HealthPipeServer:field:service", "CIS.Healthcheck", "HealthcheckService"); break;
            case "pipe-owns-collector": AddUse(facts, "Pipes.HealthProbeClient:method:RunAsync", "CIS.Healthcheck", "HealthCollectorRunner"); break;
            case "http-factory-capability": AddUse(facts, "AspNetCore.HealthcheckApplicationBuilderExtensions:field:factory", "CIS.Healthcheck", "IHealthMonitorFactory"); break;
            case "unsealed-snapshot": snapshot["attributes"] = "Public, BeforeFieldInit"; break;
            case "public-snapshot-constructor":
                JsonObject constructor = HealthcheckArchitecturePolicy.Objects(snapshot["members"]).Single(member => Text(member, "name") == ".ctor");
                constructor["publicApi"] = true;
                break;
            case "public-snapshot-setter":
                snapshot["members"]!.AsArray().Add(new JsonObject { ["kind"] = "method", ["name"] = "set_Ready", ["signature"] = "Default generic=0 (Boolean) -> Void", ["attributes"] = "Public", ["publicApi"] = true });
                break;
            case "writable-snapshot-field": Field(snapshot, "<Ready>k__BackingField")["attributes"] = "Private"; break;
            case "borrowed-snapshot-array": Field(snapshot, "<Monitors>k__BackingField")["signature"] = "[CIS.Core]CIS.Healthcheck.HealthMonitorSnapshot[]"; break;
            case "retained-snapshot-exception": Field(snapshot, "<Application>k__BackingField")["signature"] = "[System.Runtime]System.Exception"; break;
            case "retained-snapshot-monitor": Field(snapshot, "<Application>k__BackingField")["signature"] = "[CIS.Core]CIS.Healthcheck.IHealthMonitor"; break;
            default: Assert.Fail("Unknown independent control."); break;
        }
        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() => HealthcheckArchitecturePolicy.Assert(facts));
        StringAssert.StartsWith(exception.Message, diagnostic + ":");
    }

    private static void AddUse(JsonObject facts, string source, string space, string name)
    {
        facts["typeUses"]!.AsArray().Add(new JsonObject
        {
            ["Site"] = HealthcheckArchitecturePolicy.Prefix + source,
            ["Assembly"] = space.StartsWith("CIS.", StringComparison.Ordinal) ? "CIS.Core" : space,
            ["Namespace"] = space,
            ["Type"] = space + "." + name
        });
    }

    private static string Text(JsonNode node, string key) => HealthcheckArchitecturePolicy.Text(node, key);
    private static JsonObject Type(JsonObject facts, string name) => HealthcheckArchitecturePolicy.Objects(facts["types"])
        .Single(type => Text(type, "name") == HealthcheckArchitecturePolicy.Prefix + name);
    private static JsonObject Field(JsonObject type, string name) => HealthcheckArchitecturePolicy.Objects(type["members"])
        .Single(member => Text(member, "kind") == "field" && Text(member, "name") == name);
    private static async Task<JsonObject> Current() => (await Inspection.Value).DeepClone().AsObject();

    private static async Task<JsonObject> InspectAsync()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CisLib.slnx"))) { directory = directory.Parent; }
        if (directory is null) { throw new DirectoryNotFoundException("Run from the C# checkout."); }
        using var temporary = new TemporaryDirectory();
        string output = Path.Combine(temporary.Path, "healthcheck-inspection.json");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            [Path.Combine(directory.FullName, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"),
                "inspect", typeof(HealthcheckService).Assembly.Location, output], directory.FullName, TimeSpan.FromSeconds(30));
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        return JsonNode.Parse(await File.ReadAllTextAsync(output))!.AsObject();
    }
}
