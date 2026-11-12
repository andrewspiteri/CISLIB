using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

// One candidate and isolated feed/cache serve the existing sample sources and four native cases.
internal sealed class TypeManagerPackageConsumerFixture : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly Dictionary<string, string?> environment;
    private readonly Dictionary<string, string> published = new(StringComparer.Ordinal);
    private readonly string language;
    private readonly string configuration;
    private string version = "", packageHash = "", coreHash = "";
    private int commands;
    private string Root => directory.Path;
    private string Cache => Path.Combine(Root, "packages");
    private string Rid => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
    private string Late => Path.Combine(Root, "tests/fixtures/TypeManager/ExampleLate/bin/Release/net10.0/CIS.TypeManager.Examples.Late.dll");

    private TypeManagerPackageConsumerFixture()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "CisLib.slnx"))) { at = at.Parent; }
        language = at?.FullName ?? throw new DirectoryNotFoundException("The maintained C# checkout is required.");
        configuration = typeof(TypeManager).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NUGET_PACKAGES"] = Cache,
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "http-cache"),
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["UseSharedCompilation"] = "false",
            ["DOTNET_ROLL_FORWARD"] = "Disable"
        };
    }

    internal static async Task<TypeManagerPackageConsumerFixture> CreateAsync()
    {
        var fixture = new TypeManagerPackageConsumerFixture();
        try { await fixture.PrepareAsync(); return fixture; }
        catch { fixture.Dispose(); throw; }
    }

    internal string Summary => JsonSerializer.Serialize(new
    {
        packageVersion = version, packageSha256 = packageHash, coreSha256 = coreHash,
        runtime = RuntimeInformation.FrameworkDescription, os = Rid, commands,
        scope = "One package; isolated first SDK restore; Console/Web build and folder publication; ordinary package control."
    });

    private async Task PrepareAsync()
    {
        Assert.IsTrue(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());
        Assert.AreEqual(Architecture.X64, RuntimeInformation.ProcessArchitecture);
        Assert.AreEqual(".NET 10.0.12", RuntimeInformation.FrameworkDescription);
        File.Copy(Path.Combine(language, "global.json"), Path.Combine(Root, "global.json"));
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), """
            <Project><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
              <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion><RollForward>Disable</RollForward>
              <NuGetAudit>false</NuGetAudit>
            </PropertyGroup></Project>
            """);
        File.WriteAllText(Path.Combine(Root, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(Root, "Directory.Packages.props"), "<Project />");
        string feed = Directory.CreateDirectory(Path.Combine(Root, "feed")).FullName;
        new XDocument(new XElement("configuration",
            new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))))
            .Save(Path.Combine(Root, "NuGet.Config"));
        Assert.AreEqual("10.0.401", (await RunAsync(["--version"], Root)).StandardOutput.Trim());

        string? supplied = Environment.GetEnvironmentVariable("CIS_TYPE_MANAGER_CANDIDATE");
        string? expected = Environment.GetEnvironmentVariable("CIS_TYPE_MANAGER_CANDIDATE_SHA256");
        string package;
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            string expectedHash = expected ?? throw new InvalidDataException("An external candidate requires its exact SHA-256.");
            Assert.IsTrue(expectedHash.Length == 64 && expectedHash.All(Uri.IsHexDigit), "An external candidate requires its exact SHA-256.");
            package = Path.GetFullPath(supplied);
            Assert.AreEqual(expectedHash.ToLowerInvariant(), Hash(package), "The external candidate differs from the shared Windows candidate.");
        }
        else
        {
            Assert.IsTrue(string.IsNullOrWhiteSpace(expected), "Supply the candidate path together with its expected hash.");
            // Product dependencies were built by the containing native test build; do not rebuild them per consumer.
            await RunAsync(["pack", "src/CIS.Core/CIS.Core.csproj", "-c", configuration, "--no-build", "--no-restore", "-o", feed], language, isolated: false);
            package = Directory.GetFiles(feed, "CIS.Core.*.nupkg").Single();
        }
        packageHash = Hash(package);
        using (var zip = ZipFile.OpenRead(package))
        {
            using var nuspec = zip.GetEntry("CIS.Core.nuspec")!.Open();
            version = XDocument.Load(nuspec).Descendants().Single(element => element.Name.LocalName == "version").Value;
            Assert.AreEqual(FileVersionInfo.GetVersionInfo(typeof(TypeManager).Assembly.Location).ProductVersion!.Split('+')[0], version);
            using var core = zip.GetEntry("lib/net10.0/CIS.Core.dll")!.Open();
            coreHash = Convert.ToHexStringLower(SHA256.HashData(core));
            foreach (string entry in new[] { "Sdk/Sdk.props", "Sdk/Sdk.targets", "Sdk/BeforeSdk.targets", "Sdk/PackageVersion.props", "buildSupport/CIS.TypeManager.targets", "tools/net10.0/CIS.TypeManager.Build.dll" })
            { Assert.IsNotNull(zip.GetEntry(entry), "Missing package build asset: " + entry); }
            Assert.IsNull(zip.GetEntry("lib/net10.0/CIS.TypeManager.Build.dll"));
        }
        if (string.IsNullOrWhiteSpace(supplied)) { Assert.AreEqual(Hash(typeof(TypeManager).Assembly.Location), coreHash, "Pack the current built Core."); }
        CopyPackage(package, Path.Combine(feed, "CIS.Core." + version + ".nupkg"));
        CopyApprovedDependencies(feed);
        string? retained = Environment.GetEnvironmentVariable("CIS_TYPE_MANAGER_CANDIDATE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(retained))
        {
            Directory.CreateDirectory(retained);
            CopyPackage(package, Path.Combine(retained, "CIS.Core." + version + ".nupkg"));
            File.WriteAllText(Path.Combine(retained, "type-manager-candidate.json"), Summary);
        }

        foreach (string name in new[] { "Console", "Web", "Contracts", "Unused", "Shared" })
        { CopySources(Path.Combine(language, "examples/type-manager", name), Path.Combine(Root, "examples/type-manager", name)); }
        CopySources(Path.Combine(language, "tests/fixtures/TypeManager/ExampleLate"), Path.Combine(Root, "tests/fixtures/TypeManager/ExampleLate"));
        foreach (string host in new[] { "Console", "Web" })
        {
            string project = Project(host);
            var xml = XDocument.Load(project);
            xml.Descendants("ProjectReference").Where(item => ((string?)item.Attribute("Include"))?.Replace('\\', '/').EndsWith("src/CIS.Core/CIS.Core.csproj", StringComparison.Ordinal) == true).Remove();
            xml.Descendants("Import").Where(item => ((string?)item.Attribute("Project"))?.EndsWith("CIS.TypeManager.targets", StringComparison.Ordinal) == true).Remove();
            xml.Descendants("RuntimeIdentifiers").Remove();
            xml.Root!.AddFirst(new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", version)));
            xml.Save(project);
            Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
        }
        Assert.IsFalse(Directory.Exists(Cache) && Directory.EnumerateFileSystemEntries(Cache).Any(), "The candidate cache must begin empty.");
        // Restore both real package SDK hosts before preparing the separate late-load fixture.
        foreach (string host in new[] { "Console", "Web" })
        {
            await RestoreAsync(Project(host));
            AssertRestoredCandidate(Project(host));
            await RunAsync(["build", Project(host), "-c", "Release", "--no-restore"]);
        }
        await RunAsync(["build", Path.Combine(Root, "tests/fixtures/TypeManager/ExampleLate/ExampleLate.csproj"), "-c", "Release"]);
        Assert.IsTrue(File.Exists(Late));
    }

    private void CopyApprovedDependencies(string feed)
    {
        var assets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "src/CIS.Core/obj/project.assets.json")))!;
        var policy = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "eng/runtime-dependencies.json")))!;
        foreach (var dependency in policy["packages"]!.AsArray())
        {
            string id = dependency!["id"]!.GetValue<string>().ToLowerInvariant(), selected = dependency["version"]!.GetValue<string>();
            string name = id + "." + selected + ".nupkg";
            string source = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, id, selected, name)).First(File.Exists);
            File.Copy(source, Path.Combine(feed, name));
        }
    }

    private void AssertRestoredCandidate(string project)
    {
        var assets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(project)!, "obj/project.assets.json")))!;
        Assert.IsTrue(assets["libraries"]!.AsObject().ContainsKey("CIS.Core/" + version));
        Assert.IsTrue(assets["targets"]!.AsObject().ContainsKey("net10.0/" + Rid), "First restore must include the early selected native RID.");
        Assert.AreEqual(1, assets["packageFolders"]!.AsObject().Count);
        Assert.AreEqual(Path.GetFullPath(Cache), Path.TrimEndingDirectorySeparator(Path.GetFullPath(assets["packageFolders"]!.AsObject().Single().Key)));
        Assert.AreEqual(packageHash, Hash(Path.Combine(Cache, "cis.core", version, "cis.core." + version + ".nupkg")));
    }

    internal async Task VerifyHostAsync(string host, TestContext context)
    {
        string output = BuildOutput(host), manifest = Path.Combine(output, AssemblyName(host) + ".types.json");
        await VerifyPreparedAsync(host, output);
        byte[] before = File.ReadAllBytes(manifest);
        var stamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(manifest, stamp);
        await RunAsync(["build", Project(host), "-c", "Release", "--no-restore"]);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(manifest));
        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(manifest), "Unchanged inventory must not be rewritten.");
        await VerifyPreparedAsync(host, await PublishAsync(host));
        string noBuild = Path.Combine(Root, "published", host, "no-build");
        await RunAsync(["publish", Project(host), "-c", "Release", "--no-build", "--no-restore", "-o", noBuild]);
        await VerifyPreparedAsync(host, noBuild);
        context.WriteLine("CIS-TYPE-MANAGER-CASE " + JsonSerializer.Serialize(new { host, modes = new[] { "build", "publish", "publish-no-build" }, packageHash, runtime = RuntimeInformation.FrameworkDescription, os = Rid }));
    }

    private async Task VerifyPreparedAsync(string host, string output)
    {
        AssertRuntimeBoundary(host, output);
        var inventory = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, AssemblyName(host) + ".types.json")))!;
        string[] selected = inventory["assemblies"]!.AsArray().Select(item => item!["relativePath"]!.GetValue<string>()).ToArray();
        foreach (string file in new[] { AssemblyName(host) + ".dll", "CIS.Core.dll", "CIS.TypeManager.Examples.Contracts.dll", "CIS.TypeManager.Examples.Unused.dll" })
        { CollectionAssert.Contains(selected, file); }
        var result = await RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(output, AssemblyName(host) + ".dll"), "--verify", Late]);
        AssertBefore(result.StandardOutput, "PREPARING", "PREPARATION_COMPLETE");
        AssertBefore(result.StandardOutput, "PREPARATION_COMPLETE", host == "Web" ? "BUILDER_CREATING" : "APPLICATION_READY");
        if (host == "Web") { AssertBefore(result.StandardOutput, "BUILDER_CREATING", "CONTAINER_BUILDING"); AssertBefore(result.StandardOutput, "CONTAINER_BUILDING", "CONTAINER_BUILT"); }
        string json = result.StandardOutput.Split('\n').Last(line => line.TrimStart().StartsWith('{'));
        var report = JsonNode.Parse(json)!;
        Assert.IsFalse(report["unusedInitiallyLoaded"]!.GetValue<bool>());
        Assert.IsTrue(report["fixedMembershipChecked"]!.GetValue<bool>());
        CollectionAssert.AreEquivalent(new[] { "CIS.TypeManagement.Examples.Unused.DirectExample", "CIS.TypeManagement.Examples.Unused.InheritedExample" },
            report["candidates"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()).ToArray());
        Assert.IsTrue(report["report"]!["LoadedAssemblyIdentities"]!.AsArray().Any(item => item!.GetValue<string>().StartsWith("CIS.TypeManager.Examples.Unused,", StringComparison.Ordinal)));
        if (host == "Web") { Assert.IsTrue(report["rootAndScopesShareManager"]!.GetValue<bool>()); Assert.IsTrue(report["queryIdentityStable"]!.GetValue<bool>()); }
    }

    internal async Task VerifyBrokenDeploymentAsync(TestContext context)
    {
        string original = await PublishAsync("Web"), broken = Path.Combine(Root, "broken-web");
        CopyDirectory(original, broken);
        Assert.IsTrue(File.Exists(Path.Combine(broken, "CIS.TypeManager.Examples.Unused.dll")));
        File.Delete(Path.Combine(broken, "CIS.TypeManager.Examples.Unused.dll"));
        var result = await RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(broken, AssemblyName("Web") + ".dll"), "--verify", Late], requireSuccess: false);
        Assert.AreEqual(1, result.ExitCode, result.StandardOutput + result.StandardError);
        StringAssert.Contains(result.StandardError, "PREPARATION_FAILED: InvalidInventory");
        StringAssert.Contains(result.StandardError, "CIS.TypeManager.Examples.Unused.dll");
        StringAssert.Contains(result.StandardError, "FileNotFoundException");
        foreach (string marker in new[] { "PREPARATION_COMPLETE", "BUILDER_CREATING", "CONTAINER_BUILDING", "CONTAINER_BUILT" })
        { Assert.IsFalse(result.StandardOutput.Contains(marker, StringComparison.Ordinal)); }
        context.WriteLine("CIS-TYPE-MANAGER-CASE missing-selected-assembly: failed before Web construction.");
    }

    internal async Task VerifyOrdinaryConsumerAsync(TestContext context)
    {
        string app = Directory.CreateDirectory(Path.Combine(Root, "ordinary")).FullName;
        string project = Path.Combine(app, "Ordinary.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe")),
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "CIS.Core"), new XAttribute("Version", "[" + version + "]")))))
            .Save(project);
        File.WriteAllText(Path.Combine(app, "Program.cs"), "Console.WriteLine(typeof(CIS.TypeManagement.TypeManager).Assembly.GetName().Name);\n");
        await RestoreAsync(project);
        await RunAsync(["build", project, "-c", "Release", "--no-restore"]);
        string output = Path.Combine(app, "bin/Release/net10.0");
        AssertRuntimeBoundary("Ordinary", output);
        Assert.IsFalse(Directory.GetFiles(output, "*.types.json").Any());
        var result = await RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(output, "Ordinary.dll")]);
        Assert.AreEqual("CIS.Core", result.StandardOutput.Trim());
        context.WriteLine("CIS-TYPE-MANAGER-CASE ordinary-package: usable without inventory.");
    }

    private void AssertRuntimeBoundary(string host, string output)
    {
        Assert.AreEqual(coreHash, Hash(Path.Combine(output, "CIS.Core.dll")), "The child must use this exact package's runtime.");
        Assert.IsFalse(Directory.GetFiles(output, "CIS.TypeManager.Build*", SearchOption.AllDirectories).Any());
        Assert.IsFalse(File.ReadAllText(Path.Combine(output, AssemblyName(host) + ".deps.json")).Contains("CIS.TypeManager.Build", StringComparison.Ordinal));
        var config = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, AssemblyName(host) + ".runtimeconfig.json")))!;
        var frameworks = config["runtimeOptions"]!["frameworks"]!.AsArray();
        foreach (string name in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        { Assert.AreEqual("10.0.12", frameworks.Single(item => item!["name"]!.GetValue<string>() == name)!["version"]!.GetValue<string>()); }
    }

    private async Task<string> PublishAsync(string host)
    {
        if (published.TryGetValue(host, out string? existing)) { return existing; }
        string output = Path.Combine(Root, "published", host, "normal");
        await RunAsync(["publish", Project(host), "-c", "Release", "--no-restore", "-o", output]);
        published.Add(host, output);
        return output;
    }

    private Task<ProcessResult> RestoreAsync(string project)
        => RunAsync(["restore", project, "--configfile", Path.Combine(Root, "NuGet.Config"), "--no-cache"]);

    private async Task<ProcessResult> RunAsync(string[] arguments, string? working = null, bool isolated = true, bool requireSuccess = true)
    {
        commands++;
        var variables = isolated ? environment : new Dictionary<string, string?>
        { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false" };
        var result = await ProcessRunner.RunAsync("dotnet", arguments, working ?? Root, TimeSpan.FromSeconds(180), variables);
        Assert.IsTrue(result.OutputDrained);
        if (requireSuccess) { Assert.AreEqual(0, result.ExitCode, string.Join(" ", arguments) + "\n" + result.StandardOutput + result.StandardError); }
        return result;
    }

    private string Project(string host) => Path.Combine(Root, "examples/type-manager", host, host + ".csproj");
    private string BuildOutput(string host) => Path.Combine(Path.GetDirectoryName(Project(host))!, "bin/Release/net10.0", Rid);
    private static string AssemblyName(string host) => host == "Ordinary" ? host : "CIS.TypeManager.Examples." + host;
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static void AssertBefore(string output, string first, string second)
    { int start = output.IndexOf(first, StringComparison.Ordinal), end = output.IndexOf(second, StringComparison.Ordinal); Assert.IsTrue(start >= 0 && end > start, output); }
    private static void CopyPackage(string source, string destination)
    { if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { File.Copy(source, destination, overwrite: true); } }
    private static void CopySources(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly).Where(file => Path.GetExtension(file) is ".cs" or ".csproj"))
        { File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) { File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
        foreach (string child in Directory.GetDirectories(source)) { CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child))); }
    }
    public void Dispose() => directory.Dispose();
}
