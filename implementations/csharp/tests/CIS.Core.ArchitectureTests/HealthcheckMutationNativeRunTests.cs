using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wrapper = CIS.Verification.HealthcheckMutationNativeRun;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationNativeRunTests
{
    [TestMethod]
    public void ExplicitCisAttemptResolvesOneSourceWithoutCreatingOrExecutingAnything()
    {
        using var fixture = new Fixture();
        Wrapper.Request request = Wrapper.Read(fixture.Arguments());
        Assert.AreEqual(fixture.Source, request.Source);
        Assert.AreEqual(fixture.Output, request.Output);
        Assert.AreEqual(268435456, request.MaximumChildBytes);
        Assert.IsFalse(Directory.Exists(fixture.ChildRoot));
        Assert.IsTrue(fixture.Tools.All(File.Exists));
    }

    [TestMethod]
    public void NativeArgumentsAndReportLocationRemainUnchangedExceptForTheIdenticalConfigurationCopy()
    {
        using var fixture = new Fixture();
        Wrapper.Request request = Wrapper.Read(fixture.Arguments());
        string[] before = [.. request.Arguments];
        string retained = Path.Combine(fixture.ChildRoot, request.AttemptId, "native-config.json");
        string[] changed = Wrapper.ReplaceConfiguration(request.Arguments, fixture.Config, retained);
        CollectionAssert.AreEqual(before, request.Arguments);
        Assert.AreEqual(retained, changed[1]);
        Assert.AreEqual(fixture.Output, changed[3]);
        Assert.AreEqual(before.Length, changed.Length);
    }

    [TestMethod]
    public void AnExplicitBoundedLargerChildCapRemainsPartOfTheDeclaredRequest()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(1073741824, Wrapper.Read(fixture.Arguments(cap: "1073741824")).MaximumChildBytes);
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("relative-language")]
    [DataRow("foreign-child-root")]
    [DataRow("unsafe-profile")]
    [DataRow("wrong-output")]
    [DataRow("wrong-config")]
    [DataRow("duplicate-native-config")]
    [DataRow("duplicate-native-output")]
    [DataRow("missing-tool")]
    [DataRow("mixed-tool-root")]
    [DataRow("range-source")]
    [DataRow("missing-source")]
    [DataRow("existing-attempt")]
    [DataRow("foreign-entry")]
    [DataRow("unowned-prior-attempt")]
    [DataRow("bad-cap")]
    [DataRow("zero-cap")]
    [DataRow("huge-cap")]
    public void UnsafeUnownedOrAmbiguousRequestsCannotStartNativeMutation(string change)
    {
        using var fixture = new Fixture();
        string[] arguments = fixture.Arguments();
        switch (change)
        {
            case "unknown": arguments[0] = "--accept-existing"; break;
            case "duplicate": arguments[2] = arguments[0]; break;
            case "relative-language": arguments[1] = "implementations/csharp"; break;
            case "foreign-child-root": arguments[11] = Path.Combine(fixture.Root, "outside"); break;
            case "unsafe-profile": arguments[3] = "../outside"; break;
            case "wrong-output": arguments[^1] = Path.Combine(fixture.Root, "outside", "abcdef012345"); break;
            case "wrong-config": arguments[^3] = Path.Combine(fixture.Root, "outside.json"); break;
            case "duplicate-native-config": arguments = [.. arguments, "--config-file", fixture.Config]; break;
            case "duplicate-native-output": arguments = [.. arguments, "--output", fixture.Output]; break;
            case "missing-tool": File.Delete(fixture.Tools[0]); break;
            case "mixed-tool-root": arguments[9] = fixture.Tools[0]; break;
            case "range-source": fixture.Configuration["stryker-config"]!["mutate"] = new JsonArray("Healthcheck/Pipes/Budget.cs{1..2}"); fixture.Save(); break;
            case "missing-source": File.Delete(fixture.Source); break;
            case "existing-attempt": Directory.CreateDirectory(Path.Combine(fixture.ChildRoot, "abcdef012345")); break;
            case "foreign-entry": Directory.CreateDirectory(fixture.ChildRoot); File.WriteAllText(Path.Combine(fixture.ChildRoot, "foreign.txt"), "foreign"); break;
            case "unowned-prior-attempt": Directory.CreateDirectory(Path.Combine(fixture.ChildRoot, "012345abcdef")); break;
            case "bad-cap": arguments = fixture.Arguments(cap: "not-a-number"); break;
            case "zero-cap": arguments = fixture.Arguments(cap: "0"); break;
            case "huge-cap": arguments = fixture.Arguments(cap: "1073741825"); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Wrapper.Read(arguments));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-child-native-contract-" + Guid.NewGuid().ToString("N"));
        internal string Language { get; }
        internal string Output { get; }
        internal string Config { get; }
        internal string ChildRoot { get; }
        internal string Source { get; }
        internal string[] Tools { get; }
        internal JsonObject Configuration { get; } = new()
        {
            ["stryker-config"] = new JsonObject { ["mutate"] = new JsonArray("Healthcheck/Pipes/Budget.cs") },
        };
        internal Fixture()
        {
            Language = Path.Combine(Root, "implementations", "csharp");
            string profile = Path.Combine(Root, ".cis", "local", "testing", "incremental", "native-child-contract");
            Output = Path.Combine(profile, "work", "abcdef012345");
            Config = Path.Combine(Output, "stryker-config.json");
            ChildRoot = Path.Combine(profile, "child-evidence");
            Source = Path.Combine(Language, "src", "CIS.Core", "Healthcheck", "Pipes", "Budget.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            File.WriteAllText(Source, "class Budget { }");
            Directory.CreateDirectory(Output);
            string toolRoot = Path.Combine(Root, "tools");
            Directory.CreateDirectory(toolRoot);
            Tools = [Path.Combine(toolRoot, "dotnet"), Path.Combine(toolRoot, "Stryker.CLI.dll"), Path.Combine(toolRoot, "Stryker.Core.dll")];
            foreach (string tool in Tools) { File.WriteAllText(tool, "Contract fixture bytes; never executed."); }
            Save();
        }
        internal string[] Arguments(string? cap = null)
        {
            string[] options = ["--language-root", Language, "--profile-id", "native-child-contract", "--dotnet", Tools[0],
                "--stryker-cli", Tools[1], "--stryker-core", Tools[2], "--child-root", ChildRoot];
            if (cap is not null) { options = [.. options, "--maximum-child-bytes", cap]; }
            return [.. options, "--", "--config-file", Config, "--output", Output];
        }
        internal void Save() => File.WriteAllText(Config, Configuration.ToJsonString());
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
