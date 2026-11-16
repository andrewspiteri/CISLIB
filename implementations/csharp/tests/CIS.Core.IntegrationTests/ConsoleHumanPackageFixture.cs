using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using CIS.Logging;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

// The real example, one package and one disposable feed/cache; no probe application is built.
internal sealed class ConsoleHumanPackageFixture : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly Dictionary<string, string?> environment;
    private readonly string language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
    private string version = "", packageHash = "", coreHash = "", programHash = "", assemblyName = "";
    private int runs;
    private string Root => directory.Path;
    private string Cache => Path.Combine(Root, "packages");
    private string Project => Path.Combine(Root, "example/ConsoleHuman.csproj");
    internal string BuildOutput => Path.Combine(Root, "example/bin/Release/net10.0");

    private ConsoleHumanPackageFixture()
    {
        environment = CandidatePackageFixture.Variables(Root);
        // The old probe deliberately uses 16 KiB and different output defaults. Let this example use its own defaults.
        foreach (string key in environment.Keys.Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase)
            || key is "DD_SERVICE" or "DD_ENV" or "DD_VERSION").ToArray()) { environment[key] = null; }
        foreach (string key in new[] { "CisTypeManagerEnabled", "RuntimeIdentifier", "RuntimeIdentifiers", "UseAppHost", "PublishSingleFile", "PublishTrimmed", "PublishAot" })
        { environment[key] = null; }
        environment["NUGET_PACKAGES"] = Cache;
        environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "http-cache");
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        environment["UseSharedCompilation"] = "false";
    }

    internal static async Task<ConsoleHumanPackageFixture> CreateAsync()
    {
        var fixture = new ConsoleHumanPackageFixture();
        try { await fixture.PrepareAsync(); return fixture; }
        catch { fixture.Dispose(); throw; }
    }

    internal string Summary => JsonSerializer.Serialize(new
    {
        packageVersion = version, packageSha256 = packageHash, coreSha256 = coreHash, programSha256 = programHash,
        runtime = RuntimeInformation.FrameworkDescription, platform = RuntimeInformation.RuntimeIdentifier,
        scope = "Actual ConsoleHuman source; ordinary PackageReference; clean build and folder publish."
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
              <SelfContained>false</SelfContained><NuGetAudit>false</NuGetAudit>
            </PropertyGroup></Project>
            """);
        File.WriteAllText(Path.Combine(Root, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(Root, "Directory.Packages.props"), "<Project />");
        string feed = Directory.CreateDirectory(Path.Combine(Root, "feed")).FullName;
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))))
            .Save(Path.Combine(Root, "NuGet.Config"));
        Assert.AreEqual("10.0.401", (await ToolAsync(["--version"])).StandardOutput.Trim());

        string? supplied = Environment.GetEnvironmentVariable("CIS_LOGGING_EXAMPLE_CANDIDATE");
        string? expected = Environment.GetEnvironmentVariable("CIS_LOGGING_EXAMPLE_CANDIDATE_SHA256");
        string package;
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            Assert.IsTrue(expected is { Length: 64 } && expected.All(Uri.IsHexDigit), "A supplied candidate requires its exact SHA-256.");
            package = Path.GetFullPath(supplied);
            Assert.AreEqual(expected!.ToLowerInvariant(), CandidatePackageFixture.Hash(package), "Use the same retained candidate on both platforms.");
        }
        else
        {
            Assert.IsTrue(string.IsNullOrWhiteSpace(expected), "Supply the candidate path together with its SHA-256.");
            string configuration = typeof(CoreLogRuntime).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            var buildEnvironment = new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1", ["UseSharedCompilation"] = "false" };
            ProcessResult packed = await ProcessRunner.RunAsync("dotnet",
                ["pack", "src/CIS.Core/CIS.Core.csproj", "-c", configuration, "--no-build", "--no-restore", "-o", feed],
                language, TimeSpan.FromSeconds(90), buildEnvironment);
            Assert.AreEqual(0, packed.ExitCode, packed.StandardOutput + packed.StandardError);
            package = Directory.GetFiles(feed, "CIS.Core.*.nupkg").Single();
        }
        packageHash = CandidatePackageFixture.Hash(package);
        using (var zip = ZipFile.OpenRead(package))
        {
            using var nuspec = zip.GetEntry("CIS.Core.nuspec")!.Open();
            version = XDocument.Load(nuspec).Descendants().Single(item => item.Name.LocalName == "version").Value;
            Assert.AreEqual(FileVersionInfo.GetVersionInfo(typeof(CoreLogRuntime).Assembly.Location).ProductVersion!.Split('+')[0], version);
            using var core = zip.GetEntry("lib/net10.0/CIS.Core.dll")!.Open();
            coreHash = Convert.ToHexStringLower(SHA256.HashData(core));
        }
        if (string.IsNullOrWhiteSpace(supplied)) { Assert.AreEqual(CandidatePackageFixture.Hash(typeof(CoreLogRuntime).Assembly.Location), coreHash); }
        string destination = Path.Combine(feed, "CIS.Core." + version + ".nupkg");
        if (!string.Equals(Path.GetFullPath(package), destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { File.Copy(package, destination); }
        var assets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "src/CIS.Core/obj/project.assets.json")))!;
        var policy = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "eng/runtime-dependencies.json")))!;
        foreach (var dependency in policy["packages"]!.AsArray())
        {
            string id = dependency!["id"]!.GetValue<string>().ToLowerInvariant(), selected = dependency["version"]!.GetValue<string>();
            string name = id + "." + selected + ".nupkg";
            string source = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, id, selected, name)).First(File.Exists);
            File.Copy(source, Path.Combine(feed, name));
        }

        string sourceRoot = Path.Combine(language, "examples/logging/ConsoleHuman");
        string app = Directory.CreateDirectory(Path.GetDirectoryName(Project)!).FullName;
        foreach (string source in Directory.GetFiles(sourceRoot, "*.cs")) { File.Copy(source, Path.Combine(app, Path.GetFileName(source))); }
        programHash = CandidatePackageFixture.Hash(Path.Combine(app, "Program.cs"));
        var project = XDocument.Load(Path.Combine(sourceRoot, "ConsoleHuman.csproj"));
        Assert.AreEqual("Microsoft.NET.Sdk", (string?)project.Root!.Attribute("Sdk"));
        XElement reference = project.Descendants("ProjectReference").Single();
        Assert.IsTrue(((string?)reference.Attribute("Include"))?.Replace('\\', '/').EndsWith("src/CIS.Core/CIS.Core.csproj", StringComparison.Ordinal) == true);
        reference.Remove();
        Assert.IsFalse(project.Descendants("Import").Any() || project.Root.Elements("Sdk").Any(), "Use an ordinary package consumer, not Type Manager opt-in.");
        project.Root.Add(new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "CIS.Core"), new XAttribute("Version", "[" + version + "]"))));
        assemblyName = project.Descendants("AssemblyName").Single().Value;
        project.Save(Project);
        Assert.IsFalse(Directory.Exists(Path.Combine(app, "obj")), "The first restore must not use old assets.");
        Assert.IsFalse(Directory.Exists(Cache) && Directory.EnumerateFileSystemEntries(Cache).Any(), "The candidate cache must start empty.");
        await ToolAsync(["restore", Project, "--configfile", Path.Combine(Root, "NuGet.Config"), "--no-cache"]);
        var restored = JsonNode.Parse(File.ReadAllBytes(Path.Combine(app, "obj/project.assets.json")))!;
        string[] expectedPackages = policy["packages"]!.AsArray().Select(item => item!["id"]!.GetValue<string>() + "/" + item["version"]!.GetValue<string>())
            .Append("CIS.Core/" + version).ToArray();
        CollectionAssert.AreEquivalent(expectedPackages, restored["targets"]!["net10.0"]!.AsObject().Select(item => item.Key).ToArray());
        Assert.IsTrue(restored["targets"]!["net10.0"]!.AsObject().All(item => (string?)item.Value!["type"] == "package"));
        Assert.AreEqual(1, restored["packageFolders"]!.AsObject().Count);
        Assert.AreEqual(Path.GetFullPath(Cache), Path.TrimEndingDirectorySeparator(Path.GetFullPath(restored["packageFolders"]!.AsObject().Single().Key)));
        Assert.AreEqual(packageHash, CandidatePackageFixture.Hash(Path.Combine(Cache, "cis.core", version, "cis.core." + version + ".nupkg")));
        await ToolAsync(["build", Project, "-c", "Release", "--no-restore"]);
        AssertRuntime(BuildOutput);
    }

    internal async Task<string> PublishAsync()
    {
        string output = Path.Combine(Root, "published");
        await ToolAsync(["publish", Project, "-c", "Release", "--no-restore", "-o", output]);
        AssertRuntime(output);
        return output;
    }

    private void AssertRuntime(string output)
    {
        Assert.AreEqual(coreHash, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.Core.dll")), "The example must execute the intended packaged Core.");
        Assert.IsTrue(File.Exists(Path.Combine(output, assemblyName + (OperatingSystem.IsWindows() ? ".exe" : ""))), "Keep the ordinary SDK apphost default.");
        Assert.IsFalse(Directory.GetFiles(output, "*.types.json").Any());
        Assert.IsFalse(Directory.GetFiles(output, "CIS.TypeManager.Build*", SearchOption.AllDirectories).Any());
        Assert.IsFalse(File.ReadAllText(Path.Combine(output, assemblyName + ".deps.json")).Contains("CIS.TypeManager.Build", StringComparison.Ordinal));
        var config = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, assemblyName + ".runtimeconfig.json")))!;
        foreach (string name in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        { Assert.AreEqual("10.0.12", config["runtimeOptions"]!["frameworks"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == name)!["version"]!.GetValue<string>()); }
    }

    internal async Task<(ProcessResult Process, string LogPath)> RunAsync(string output, IReadOnlyDictionary<string, string?>? changes = null)
    {
        string working = Directory.CreateDirectory(Path.Combine(Root, "run-" + ++runs)).FullName;
        string path = Path.Combine(working, "events.jsonl");
        var variables = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)
        {
            ["CORE_SERVICE_NAME"] = "ConsoleHuman.Package", ["CORE_ENVIRONMENT"] = "integration", ["CORE_SERVICE_VERSION"] = "1.2.3",
            ["CORE_LOG_CONSOLE_LEVEL"] = "Information", ["CORE_LOG_CONSOLE_FORMAT"] = "human", ["CORE_LOG_CONSOLE_STREAM"] = "stderr",
            ["CORE_LOG_HUMAN_DETAIL"] = "verbose", ["CORE_LOG_FILE_PATH"] = path,
            ["CORE_LOG_FILE_LEVEL"] = "Information", ["CORE_LOG_FILE_FORMAT"] = "jsonl"
        };
        if (changes is not null) { foreach (var item in changes) { variables[item.Key] = item.Value; } }
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(output, assemblyName + ".dll")],
            working, CandidatePackageFixture.Guard, variables, new UTF8Encoding(false, true));
        Assert.IsTrue(result.OutputDrained);
        return (result, path);
    }

    private async Task<ProcessResult> ToolAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, Root, TimeSpan.FromSeconds(180), environment);
        Assert.IsTrue(result.OutputDrained);
        Assert.AreEqual(0, result.ExitCode, string.Join(" ", arguments) + "\n" + result.StandardOutput + result.StandardError);
        return result;
    }

    public void Dispose() => directory.Dispose();
}
