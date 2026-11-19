using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Entra;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Security;

[TestClass]
[DoNotParallelize]
public sealed class SecurityPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [DataRow("CoreOnly")]
    [DataRow("Certificates")]
    [DataRow("Entra")]
    [TestMethod]
    public async Task SelectedSecurityPackageRunsWithItsOwnDependencyClosure(string profile)
    {
        Assembly[] current = profile switch
        {
            "Certificates" => [typeof(SecurityOptions).Assembly, typeof(CertificateTrustPolicy).Assembly],
            "Entra" => [typeof(SecurityOptions).Assembly, typeof(EntraSecurityOptions).Assembly],
            _ => [typeof(SecurityOptions).Assembly]
        };
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "Security.Package." + profile, current);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "app")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        string assemblyName = "CIS.Security.PackageConsumer." + profile;
        File.WriteAllText(Path.Combine(app, "Program.cs"), SecurityPackagePrograms.For(profile));
        var consumer = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                current.Select(assembly => new XElement("PackageReference", new XAttribute("Include", assembly.GetName().Name!),
                    new XAttribute("Version", "[" + package.Version + "]"))))));
        Assert.IsFalse(consumer.Descendants("ProjectReference").Any());
        consumer.Save(project);

        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        JsonObject restored = RestPackage.Assets(Path.Combine(app, "obj/project.assets.json"));
        string[] actualPackages = RestPackage.Packages(restored).Order(StringComparer.Ordinal).ToArray();
        AssertApprovedClosure(profile, package.Version, current, actualPackages);
        TestContext.WriteLine(profile + " restored closure: " + string.Join(", ", actualPackages));

        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        JsonObject deployed = package.AssertDeployed(output, assemblyName);
        string[] deployedPackages = deployed["libraries"]!.AsObject()
            .Where(pair => pair.Value!["type"]!.GetValue<string>() == "package")
            .Select(pair => pair.Key).ToArray();
        AssertApprovedClosure(profile, package.Version, current, deployedPackages);
        foreach (string absent in profile switch
        {
            "Certificates" => new[] { "CIS.Security.Entra.dll", "Azure.Identity.dll", "Microsoft.Identity.Client.dll" },
            "Entra" => new[] { "CIS.Security.Certificates.dll", "Microsoft.AspNetCore.Authentication.Certificate.dll" },
            _ => new[] { "CIS.Security.Entra.dll", "CIS.Security.Certificates.dll", "Azure.Identity.dll",
                "Microsoft.Identity.Client.dll", "Microsoft.AspNetCore.Authentication.Certificate.dll",
                "Microsoft.AspNetCore.Authentication.JwtBearer.dll" }
        })
        {
            Assert.IsFalse(File.Exists(Path.Combine(output, absent)), "Unselected package payload: " + absent);
        }

        string working = Directory.CreateDirectory(Path.Combine(package.Root, "execute")).FullName;
        var execution = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(output, assemblyName + ".dll")], working, TimeSpan.FromSeconds(45));
        Assert.AreEqual("", execution.StandardError);
        JsonObject report = JsonNode.Parse(execution.StandardOutput)!.AsObject();
        Assert.AreEqual(profile, report["profile"]!.GetValue<string>());
        Assert.IsTrue(report["registrationWasPure"]!.GetValue<bool>());
        Assert.IsTrue(report["denied"]!.GetValue<bool>());
        Assert.IsTrue(report["crossTargetDenied"]!.GetValue<bool>());
        Assert.AreEqual(profile != "CoreOnly", report["allowed"]!.GetValue<bool>());
        Assert.AreEqual(profile != "CoreOnly", report["callerEstablished"]!.GetValue<bool>());
        Assert.AreEqual(profile == "CoreOnly" ? 0 : 1, report["statusCount"]!.GetValue<int>());
        Assert.IsTrue(report["selectedRolesAvailable"]!.GetValue<bool>());
        Assert.AreEqual(0, report["outgoingCredentialFactories"]!.GetValue<int>());
        Assert.IsFalse(report["cacheRegistered"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", report["framework"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
        if (profile != "CoreOnly")
        {
            RestPackage.SamePath(Path.Combine(output, "CIS.Security." + profile + ".dll"), report["adapterPath"]!.GetValue<string>());
        }
        string[] loaded = report["loaded"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.IsFalse(loaded.Any(IsUnrelatedPackage));
        Assert.IsFalse(loaded.Contains(profile == "Entra" ? "CIS.Security.Certificates" : "CIS.Security.Entra", StringComparer.Ordinal));
        if (profile != "Entra")
        {
            Assert.IsFalse(loaded.Any(name => name.StartsWith("Azure.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.Identity.Client", StringComparison.Ordinal)));
        }
        package.AssertOriginalAssembliesUnchanged();
        TestContext.WriteLine(profile + " executed current packaged public APIs: " + execution.StandardOutput);
    }

    private static void AssertApprovedClosure(string profile, string version, IReadOnlyList<Assembly> assemblies,
        IReadOnlyList<string> actual)
    {
        string language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (Assembly assembly in assemblies)
        {
            string id = assembly.GetName().Name!;
            expected.Add(id + "/" + version);
            JsonObject source = RestPackage.Assets(Path.Combine(language, "src", id, "obj/project.assets.json"));
            expected.UnionWith(RestPackage.Packages(source));
        }
        Assert.IsTrue(actual.All(expected.Contains), "The isolated package consumer must use its selected native source graph and exact versions: "
            + string.Join(", ", actual.Where(item => !expected.Contains(item))));
        Assert.IsTrue(actual.Contains("CIS.Core/" + version, StringComparer.Ordinal));
        Assert.IsFalse(actual.Any(item => IsUnrelatedPackage(item.Split('/')[0])));
        if (profile == "Certificates")
        {
            Assert.IsTrue(actual.Contains("CIS.Security.Certificates/" + version, StringComparer.Ordinal));
            Assert.IsTrue(actual.Contains("Microsoft.AspNetCore.Authentication.Certificate/10.0.12", StringComparer.Ordinal));
        }
        else if (profile == "Entra")
        {
            Assert.IsTrue(actual.Contains("CIS.Security.Entra/" + version, StringComparer.Ordinal));
            Assert.IsTrue(actual.Contains("Azure.Identity/1.21.0", StringComparer.Ordinal));
            Assert.IsTrue(actual.Contains("Microsoft.AspNetCore.Authentication.JwtBearer/10.0.12", StringComparer.Ordinal));
        }
        if (profile != "Entra")
        {
            Assert.IsFalse(actual.Any(item => item.StartsWith("CIS.Security.Entra/", StringComparison.Ordinal)
                || item.StartsWith("Azure.", StringComparison.Ordinal) || item.StartsWith("Microsoft.Identity.Client", StringComparison.Ordinal)));
        }
        if (profile != "Certificates")
        {
            Assert.IsFalse(actual.Any(item => item.StartsWith("CIS.Security.Certificates/", StringComparison.Ordinal)
                || item.StartsWith("Microsoft.AspNetCore.Authentication.Certificate/", StringComparison.Ordinal)));
        }
    }

    private static bool IsUnrelatedPackage(string name) => name.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.VisualStudio.Test", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.NET.Test", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("CIS.CommonTesting", StringComparison.Ordinal)
        || name.StartsWith("CIS.Metrics.OpenTelemetry", StringComparison.Ordinal)
        || name.StartsWith("CIS.RestClient.Resilience", StringComparison.Ordinal);
}
