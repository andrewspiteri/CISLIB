using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ArchitectureAllowlistTests
{
    private static readonly string Root = FindRoot();
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, path)))!.AsObject();
    private static JsonObject Current()
    {
        string repository = Directory.GetParent(Directory.GetParent(Root)!.FullName)!.FullName;
        string reports = Path.Combine(repository, ".artifacts/csharp/verification/Developer");
        JsonObject summary = JsonNode.Parse(File.ReadAllText(Path.Combine(reports, "summary.json")))!.AsObject();
        return JsonNode.Parse(File.ReadAllText(Path.Combine(reports, summary["architectureInspection"]!["path"]!.GetValue<string>())))!.AsObject();
    }
    private static JsonObject Project(JsonObject report, string path) => report["projects"]!.AsArray().Single(item => item!["project"]!.GetValue<string>() == path)!.AsObject();
    private static JsonObject Core(JsonObject report) => Project(report, "src/CIS.Core/CIS.Core.csproj");
    private static JsonObject Target(JsonObject project) => project["targets"]![0]!.AsObject();

    [TestMethod]
    public void CurrentNativeInspectionPassesTheAmendedDependencyPolicy()
    {
        JsonObject report = Current();
        JsonObject result = ArchitecturePolicy.Assert(Root, report);
        Assert.AreEqual(0, result["violations"]!.GetValue<int>());
        Assert.AreEqual(2, result["runtimePackages"]!.GetValue<int>());
        Assert.AreEqual(2, result["packagedAssemblies"]!.GetValue<int>());
        Assert.AreEqual(report["projects"]!.AsArray().Count, result["projects"]!.GetValue<int>());
        Assert.IsTrue(JsonNode.DeepEquals(report["allowlists"], result));
    }

    [TestMethod]
    [DataRow("shipping-identity", "ARCH-SHIPPING-IDENTITY")]
    [DataRow("private-package", "ARCH-PACKAGE-REFERENCE")]
    [DataRow("framework-pruned-closure", "ARCH-PACKAGE-CLOSURE")]
    [DataRow("rid-closure", "ARCH-PACKAGE-CLOSURE")]
    [DataRow("missing-runtime", "ARCH-PACKAGE-CLOSURE")]
    [DataRow("version", "ARCH-PACKAGE-VERSION")]
    [DataRow("framework", "ARCH-FRAMEWORK")]
    [DataRow("project", "ARCH-PROJECT-REFERENCE")]
    [DataRow("build-only", "ARCH-BUILD-ONLY")]
    [DataRow("restored-project", "ARCH-RESTORED-PROJECT")]
    [DataRow("compiler-origin", "ARCH-COMPILER-ORIGIN")]
    [DataRow("development-tool-origin", "ARCH-COMPILER-ORIGIN")]
    [DataRow("wrong-target-asset", "ARCH-COMPILER-ORIGIN")]
    [DataRow("compiled-token", "ARCH-COMPILED-REFERENCE")]
    [DataRow("compiled-host", "ARCH-COMPILED-REFERENCE")]
    [DataRow("type-use", "ARCH-TYPE-DEPENDENCY")]
    [DataRow("packed-origin", "ARCH-PACKED-ORIGIN")]
    [DataRow("package-group", "ARCH-PACKAGE-GROUP")]
    [DataRow("framework-download", "ARCH-FRAMEWORK-DOWNLOAD")]
    [DataRow("manual-reference", "ARCH-MANUAL-REFERENCE")]
    public void PolicyRejectsIsolatedObservedFactViolations(string kind, string diagnostic)
    {
        JsonObject report = Current();
        _ = ArchitecturePolicy.Assert(Root, report); // Each negative starts with the real positive observation.
        JsonObject core = Core(report), target = Target(core);
        JsonNode items = target["items"]!;
        JsonNode assembly = report["assemblies"]![0]!;
        switch (kind)
        {
            case "shipping-identity": target["properties"]!["AssemblyName"] = "Changed.Core"; break;
            case "private-package": items["PackageReference"]!.AsArray().Add(new JsonObject { ["Identity"] = "NLog", ["PrivateAssets"] = "all" }); break;
            case "framework-pruned-closure": Target(Project(report, "examples/type-manager/Web/Web.csproj"))["packages"]!.AsArray().Add(new JsonObject { ["id"] = "Microsoft.Extensions.Logging.Abstractions", ["version"] = "10.0.12" }); break;
            case "rid-closure":
                JsonObject host = Project(report, "tests/fixtures/TypeManager/Host/Host.csproj");
                JsonNode rid = host["targets"]!.AsArray().Single(item => item!["target"]!.GetValue<string>().EndsWith("/linux-x64", StringComparison.Ordinal))!;
                rid["packages"]!.AsArray().Add(new JsonObject { ["id"] = "NLog", ["version"] = "5.0.0" }); break;
            case "missing-runtime": target["packages"]!.AsArray().RemoveAt(0); break;
            case "version": target["packages"]![0]!["version"] = "10.0.13"; break;
            case "framework": items["FrameworkReference"]![0]!["Identity"] = "Microsoft.AspNetCore.App"; break;
            case "project": items["ProjectReference"]!.AsArray().Add(new JsonObject { ["Identity"] = "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj" }); break;
            case "build-only": items["ProjectReference"]![0]!["ReferenceOutputAssembly"] = "true"; break;
            case "restored-project":
                JsonObject unit = Target(Project(report, "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"));
                string key = unit["graph"]!.AsObject().First(item => item.Value!["type"]!.GetValue<string>() == "project").Key;
                unit["graph"]!.AsObject().Remove(key); break;
            case "compiler-origin": items["ReferencePath"]![0]!["Identity"] = typeof(ArchitectureAllowlistTests).Assembly.Location; break;
            case "development-tool-origin": Target(Project(report, "eng/CIS.Discovery.TestLogger/CIS.Discovery.TestLogger.csproj"))["items"]!["ReferencePath"]![0]!["Identity"] = typeof(ArchitectureAllowlistTests).Assembly.Location; break;
            case "wrong-target-asset":
                JsonObject packageReference = items["ReferencePath"]!.AsArray().OfType<JsonObject>().First(item => item["NuGetPackageId"]?.GetValue<string>() == "Microsoft.Extensions.Logging.Abstractions");
                string original = packageReference["Identity"]!.GetValue<string>();
                packageReference["Identity"] = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(original))!, "netstandard2.0", Path.GetFileName(original));
                packageReference["PathInPackage"] = "lib/netstandard2.0/Microsoft.Extensions.Logging.Abstractions.dll"; break;
            case "compiled-token": assembly["facts"]!["references"]![0]!["publicKeyOrToken"] = "0000000000000000"; break;
            case "compiled-host": assembly["facts"]!["references"]!.AsArray().Add(new JsonObject { ["name"] = "Microsoft.Extensions.Hosting", ["version"] = "10.0.0.0", ["culture"] = "", ["publicKeyOrToken"] = "adb9793829ddae60", ["flags"] = "0" }); break;
            case "type-use": assembly["facts"]!["typeUses"]!.AsArray().Add(new JsonObject { ["Assembly"] = "NLog", ["Site"] = "controlled fixture", ["Type"] = "NLog.Logger" }); break;
            case "packed-origin": assembly["project"] = "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"; break;
            case "package-group": report["packageDependencyGroups"]![0]!["dependencies"]!.AsArray().Add(new JsonObject { ["id"] = "Microsoft.Extensions.Logging", ["version"] = "10.0.12" }); break;
            case "framework-download": core["restoreFrameworks"]!["net10.0"]!["downloadDependencies"] = new JsonArray(new JsonObject { ["name"] = "unapproved.runtime", ["version"] = "[1.0.0, 1.0.0]" }); break;
            case "manual-reference": items["Reference"]!.AsArray().Add(new JsonObject { ["Identity"] = "NLog", ["HintPath"] = typeof(ArchitectureAllowlistTests).Assembly.Location }); break;
            default: Assert.Fail("Unknown control."); break;
        }
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ArchitecturePolicy.Assert(Root, report));
        StringAssert.StartsWith(error.Message, diagnostic + ":");
    }

    [TestMethod]
    [DataRow("version", "ARCH-TOOL-VERSION")]
    [DataRow("command", "ARCH-TOOL-COMMAND")]
    [DataRow("extra", "ARCH-TOOL-MANIFEST")]
    public void ToolManifestRequiresTheExactApprovedPinsAndCommands(string kind, string diagnostic)
    {
        JsonObject toolchain = Read("eng/toolchain.json"), manifest = Read(".config/dotnet-tools.json");
        ArchitecturePolicy.AssertTools(toolchain, manifest);
        JsonNode tool = manifest["tools"]!["dotnet-stryker"]!;
        if (kind == "version") { tool["version"] = "0.0.0"; }
        else if (kind == "command") { tool["commands"] = new JsonArray("unapproved"); }
        else { manifest["tools"]!["unapproved"] = new JsonObject { ["version"] = "1.0.0", ["commands"] = new JsonArray("unapproved") }; }
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ArchitecturePolicy.AssertTools(toolchain, manifest));
        StringAssert.StartsWith(error.Message, diagnostic + ":");
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "implementations/csharp");
            if (File.Exists(Path.Combine(candidate, "CisLib.slnx"))) { return candidate; }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Run inside the CisLib checkout.");
    }
}
