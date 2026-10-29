using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Verification;
using CIS.Verification.HealthcheckMutationChildren;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Children = CIS.Verification.HealthcheckSharedMutationChildren;
using Shared = CIS.Verification.HealthcheckSharedMutationModel;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckSharedMutationChildrenTests
{
    // These are receiving/protocol controls, not native child or mutation evidence.
    private const string UnitPath = "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/ExampleTests.cs";
    private const string IntegrationPath = "implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/ExampleTests.cs";
    private const string UnitText = "public class ExampleTests { }";
    private static readonly string NativeId = Guid.Parse("15294e7a-147c-4887-bfef-41109e2df371").ToString();

    [TestMethod]
    public void UnitOnlyClassificationUsesCurrentUniqueNativeSourceRatherThanAssertionNames()
    {
        var fixture = new Fixture();
        Assert.IsTrue(Children.AllNativeKillersAreUnit(fixture.Root, [UnitPath], Report(UnitPath), [Definition("7")], fixture.Read));
        JsonObject report = Report(UnitPath);
        report["testFiles"]!.AsObject().Single().Value!["tests"]![0]!["name"] = "An arbitrary completed native test name";
        Assert.IsTrue(Children.AllNativeKillersAreUnit(fixture.Root, [UnitPath], report, [Definition("7")], fixture.Read));
    }

    [TestMethod]
    [DataRow("missing-killer")]
    [DataRow("unknown-guid")]
    [DataRow("integration")]
    [DataRow("stale-source")]
    [DataRow("duplicate-guid")]
    [DataRow("undeclared-test")]
    public void UnboundOrNonUnitKillersCannotMakeAnEmptyCaptureNeutral(string scenario)
    {
        var fixture = new Fixture(); JsonObject report = Report(UnitPath);
        Definition definition = Definition("7"); string[] declared = [UnitPath, IntegrationPath];
        if (scenario == "missing-killer") { definition = definition with { KilledBy = [] }; }
        else if (scenario == "unknown-guid") { definition = definition with { KilledBy = [Guid.NewGuid().ToString()] }; }
        else if (scenario == "integration") { report = Report(IntegrationPath); }
        else if (scenario == "stale-source") { report["testFiles"]!.AsObject().Single().Value!["source"] = UnitText + "changed"; }
        else if (scenario == "duplicate-guid") { report["testFiles"]!.AsObject()["/old/snapshot/" + IntegrationPath] = Report(IntegrationPath)["testFiles"]!.AsObject().Single().Value!.DeepClone(); }
        else { declared = [IntegrationPath]; }

        Assert.IsFalse(Children.AllNativeKillersAreUnit(fixture.Root, declared, report, [definition], fixture.Read));
    }

    [TestMethod]
    public void VersionOneEmptyCaptureIsNeutralOnlyForUnitKillersAndNeverAnActiveChildProof()
    {
        using var fixture = new CaptureFixture();
        Assert.IsTrue(Children.IsEmptyUnitOnlyCapture(fixture.Session, 0, true));
        Assert.IsFalse(Children.IsEmptyUnitOnlyCapture(fixture.Session, 0, false));
        Assert.IsFalse(Children.IsEmptyUnitOnlyCapture(fixture.Session, 1, true));
        Assert.IsFalse(Children.IsEmptyUnitOnlyCapture(fixture.Session with { BoundIntentVersion = 0 }, 0, true));
        foreach (string name in new[] { "intents", "capture-phases", "launches", "stages" })
        {
            string orphan = Path.Combine(fixture.Root, name, "orphan.json");
            File.WriteAllText(orphan, "{}");
            Assert.IsFalse(Children.IsEmptyUnitOnlyCapture(fixture.Session, 0, true), "An orphan in " + name + " is not an empty capture.");
            File.Delete(orphan);
        }
    }

    [TestMethod]
    public void AnActualEmptyCaptureAuditRequiresTheVersionedPhysicalCaptureRoots()
    {
        using var fixture = new CaptureFixture();
        Assert.ThrowsExactly<InvalidOperationException>(() => HealthcheckMutationChildCapture.Audit(fixture.Session, Path.Combine(fixture.Root, "session.json"), []));
        Assert.IsTrue(Children.IsEmptyUnitOnlyCapture(fixture.Session, 0, true));
        Directory.Delete(Path.Combine(fixture.Root, "intents"));
        Assert.ThrowsExactly<InvalidDataException>(() => Children.IsEmptyUnitOnlyCapture(fixture.Session, 0, true));
    }

    [TestMethod]
    public void OneAuditedActiveProbeSatisfiesThePipesAggregateWithoutInventingPerKillChildGuids()
    {
        Shared.Batch[] batches = Enumerable.Range(0, 4).Select(index => Batch("pipes", index.ToString())).ToArray();
        Children.Result evidence = Evidence(batches[0], active: ["probe"]);

        Shared.Batch[] received = Children.ApplyAggregateHolds(batches, [evidence]);

        Assert.IsTrue(received.All(batch => batch.VerifiedKilledIds.SequenceEqual(batch.Definitions.Select(definition => definition.Id))));
        Assert.IsTrue(received.All(batch => batch.Issues.Length == 0));
        Assert.AreNotEqual(received[0].Definitions[0].Id, received[3].Definitions[0].Id,
            "The aggregate witness does not assert a selected child for every native numeric ID.");
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("unit-only")]
    [DataRow("wrong-kind")]
    [DataRow("invalid")]
    [DataRow("foreign-report")]
    [DataRow("foreign-group")]
    public void MissingOrFabricatedAggregateChildProofKeepsNativeKillsHeld(string scenario)
    {
        Shared.Batch pipes = Batch("pipes", "7"), unit = Batch("unit-runtime", "9");
        Children.Result result = Evidence(pipes, ["probe"]);
        result = scenario switch
        {
            "unit-only" => result with { UnitOnly = true },
            "wrong-kind" => result with { ActiveKinds = ["http-host"] },
            "invalid" => result with { Verified = false },
            "foreign-report" => result with { ReportSha256 = Digest("foreign") },
            "foreign-group" => result with { Group = "aspnet" },
            _ => result
        };

        Shared.Batch[] received = Children.ApplyAggregateHolds([pipes, unit], scenario == "none" ? [] : [result]);

        Assert.IsEmpty(received[0].VerifiedKilledIds);
        Assert.IsEmpty(received[0].VerifiedTimeoutKilledIds);
        Assert.AreEqual("MissingSharedActiveChild", received[0].Issues.Single().Code);
        Assert.AreEqual(pipes.Definitions[0].Status, received[0].Definitions[0].Status);
        Assert.AreEqual(pipes.Definitions[0].Identity, received[0].Definitions[0].Identity);
        CollectionAssert.AreEqual(unit.VerifiedKilledIds, received[1].VerifiedKilledIds);
        Assert.IsEmpty(pipes.Issues, "Receiving does not mutate original batches.");
    }

    [TestMethod]
    public void AspNetRequiresItsOwnActiveHttpHostRatherThanAProbeFromAnotherPartition()
    {
        Shared.Batch pipes = Batch("pipes", "1"), asp = Batch("aspnet", "2");
        Shared.Batch[] held = Children.ApplyAggregateHolds([pipes, asp], [Evidence(pipes, ["probe"])]);
        Assert.IsEmpty(held[1].VerifiedKilledIds);
        Shared.Batch[] accepted = Children.ApplyAggregateHolds([pipes, asp], [Evidence(pipes, ["probe"]), Evidence(asp, ["http-host"])]);
        Assert.HasCount(1, accepted[1].VerifiedKilledIds);
    }

    [TestMethod]
    public void RetainedNativeConfigurationMustReproduceTheExactCurrentFullFileRender()
    {
        JsonObject template = JsonNode.Parse("""{"stryker-config":{"mutate":["Healthcheck/*.cs"],"coverage-analysis":"off","mutation-level":"Complete","test-case-filter":"Full"}}""")!.AsObject();
        var rendered = (JsonObject)template.DeepClone();
        rendered["stryker-config"]!["mutate"] = new JsonArray("Healthcheck/Pipes/Example.cs");
        rendered["stryker-config"]!["test-case-filter"] = "Selected";
        string filter = Children.CommandHash("Selected");
        Children.ValidateRenderedConfiguration(template, rendered, "Healthcheck/Pipes/Example.cs", filter);
        var changed = (JsonObject)rendered.DeepClone(); changed["stryker-config"]!["mutation-level"] = "Basic";
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateRenderedConfiguration(template, changed, "Healthcheck/Pipes/Example.cs", filter));
        changed = (JsonObject)rendered.DeepClone(); changed["stryker-config"]!["mutate"] = new JsonArray("Healthcheck/*.cs");
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateRenderedConfiguration(template, changed, "Healthcheck/Pipes/Example.cs", filter));
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateRenderedConfiguration(template, rendered, "Healthcheck/Pipes/Example.cs", Children.CommandHash("Other")));
    }

    [TestMethod]
    public void OuterCommandBindsPinnedRuntimeProducerSourceAndOnlyTheStableConfigReplacement()
    {
        var fixture = new Fixture(); var command = fixture.Commands();
        Assert.AreEqual(268435456L, Children.ValidateCommand(command.Outer, command.Native, fixture.Tools, fixture.Language, "profile", fixture.ChildRoot, fixture.Output, fixture.Config));
        int separator = Array.IndexOf(command.Outer, "--");
        string[] limited = [.. command.Outer[..separator], "--maximum-child-bytes", "67108864", .. command.Outer[separator..]];
        Assert.AreEqual(67108864L, Children.ValidateCommand(limited, command.Native, fixture.Tools, fixture.Language, "profile", fixture.ChildRoot, fixture.Output, fixture.Config));
        limited[separator + 1] = "67108864.0";
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateCommand(limited, command.Native, fixture.Tools, fixture.Language, "profile", fixture.ChildRoot, fixture.Output, fixture.Config));
        string[] changed = [.. command.Outer]; changed[3] = "10.0.13";
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateCommand(changed, command.Native, fixture.Tools, fixture.Language, "profile", fixture.ChildRoot, fixture.Output, fixture.Config));
        changed = [.. command.Native, "--mutate", "Foreign.cs"];
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateCommand(command.Outer, changed, fixture.Tools, fixture.Language, "profile", fixture.ChildRoot, fixture.Output, fixture.Config));
        changed = [.. command.Native]; changed[0] = Path.Combine(fixture.Root, "different-dotnet");
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateCommand(command.Outer, changed, fixture.Tools, fixture.Language, "profile", fixture.ChildRoot, fixture.Output, fixture.Config));
    }

    [TestMethod]
    public void NativeChronologyBindsItsRetainedMutationStageIndependentlyOfLaterUnitRefresh()
    {
        DateTimeOffset first = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        JsonObject native = Parse(new { startedUtc = first, finishedUtc = first.AddSeconds(4) });
        JsonObject stage = Parse(new { finishedUtc = first.AddSeconds(5), execution = new { durationMilliseconds = 5000L } });
        Children.ValidateNativeWindow(native, stage);
        stage["unitFinishedUtc"] = first.AddHours(1).ToString("O");
        Children.ValidateNativeWindow(native, stage);
        native["startedUtc"] = first.AddHours(-1).ToString("O");
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidateNativeWindow(native, stage));
    }

    [TestMethod]
    public void PhaseBindingsRejectAReplacedSessionCommandOrNativeProcess()
    {
        var now = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        JsonObject pin = Parse(new { path = "/pin", sha256 = Digest("pin"), bytes = 1L });
        JsonObject binding = Parse(new { profileId = "profile", attemptId = "123456abcdef", phase = "mutation", languageRoot = "/language", outputRoot = "/output", workingDirectory = "/working", config = pin, session = pin, source = pin, tools = new { x = pin }, native = new { command = new[] { "dotnet", "stryker" }, processId = 42L, startedUtc = now } });
        JsonObject prepared = (JsonObject)binding.DeepClone(); prepared.Remove("native");
        prepared["schema"] = "cis.healthcheck.shared-child-attempt-prepared/v1";
        prepared["command"] = JsonSerializer.SerializeToNode(new[] { "dotnet", "stryker" }); prepared["createdUtc"] = now.ToString("O");
        JsonObject started = Parse(new { schema = "cis.healthcheck.shared-child-attempt-started/v1", prepared = pin, processId = 42L, startedUtc = now, command = new[] { "dotnet", "stryker" } });
        Children.ValidatePhases(binding, prepared, started, pin);
        started["processId"] = 43L;
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidatePhases(binding, prepared, started, pin));
        started["processId"] = 42L; prepared["session"]!["sha256"] = Digest("replaced session");
        Assert.ThrowsExactly<InvalidDataException>(() => Children.ValidatePhases(binding, prepared, started, pin));
    }

    private static Definition Definition(string id) => new(id, "Healthcheck/Pipes/Example.cs", Digest("source"), 0, 1, "Boolean mutation", "true", "false", "Killed", [NativeId]);
    private static Shared.Batch Batch(string group, string id) => new(new("Healthcheck/" + group + "/" + id + ".cs", Digest("source"), group, "source"), Digest(group + id), [Definition(id)], [id], [], []);
    private static Children.Result Evidence(Shared.Batch batch, string[] active) => new(batch.Source.Group, batch.ReportSha256, true, false, active, [], new());
    private static JsonObject Report(string path) => Parse(new Dictionary<string, object>
    {
        ["testFiles"] = new Dictionary<string, object> { ["/old/snapshot/" + path] = new { source = UnitText, tests = new[] { new { id = NativeId, name = "No assertion wording rule" } } } }
    });
    private static JsonObject Parse<T>(T value) => JsonNode.Parse(JsonSerializer.Serialize(value))!.AsObject();
    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class CaptureFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-shared-child-test-" + Guid.NewGuid().ToString("N"));
        internal HealthcheckMutationChildProtocol.Session Session { get; }
        internal CaptureFixture()
        {
            Directory.CreateDirectory(Root);
            HealthcheckMutationChildCapture.PrepareRoots(Root);
            Directory.CreateDirectory(Path.Combine(Root, "launches"));
            Directory.CreateDirectory(Path.Combine(Root, "stages"));
            Session = new(HealthcheckMutationChildProtocol.Schema, Root, Root,
                "unused", "unused", "unused", "unused", "unused", "unused", "unused", "unused", "unused", [],
                DateTimeOffset.UtcNow, true, true, BoundIntentVersion: 1);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class Fixture
    {
        internal string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cis-shared-child-model"));
        internal string Language => Path.Combine(Root, "implementations", "csharp");
        internal string ChildRoot => Path.Combine(Root, ".cis", "local", "testing", "incremental", "profile", "child-evidence");
        internal string Output => Path.Combine(Root, ".cis", "local", "testing", "incremental", "profile", "work", "123456abcdef");
        internal string Config => Path.Combine(ChildRoot, "123456abcdef", "native-config.json");
        internal Dictionary<string, string> Tools => new(StringComparer.Ordinal)
        {
            ["dotnet"] = Path.Combine(Root, "tools", "dotnet"),
            ["wrapper"] = Path.Combine(Root, "tools", "wrapper.dll"),
            ["strykerCli"] = Path.Combine(Root, "tools", "Stryker.CLI.dll"),
            ["strykerCore"] = Path.Combine(Root, "tools", "Stryker.Core.dll")
        };
        internal string Read(string path) => path == Path.Combine(Root, UnitPath.Replace('/', Path.DirectorySeparatorChar)) ? UnitText : throw new AssertFailedException("A non-Unit source cannot establish Unit-only evidence.");
        internal (string[] Outer, string[] Native) Commands()
        {
            var tools = Tools;
            string[] args = ["--config-file", Path.Combine(Output, "stryker-config.json"), "--output", Output];
            string[] outer = [tools["dotnet"], "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", tools["wrapper"], "healthcheck-mutation-run",
                "--language-root", Language, "--profile-id", "profile", "--dotnet", tools["dotnet"], "--stryker-cli", tools["strykerCli"], "--stryker-core", tools["strykerCore"], "--child-root", ChildRoot, "--", .. args];
            string[] native = [tools["dotnet"], tools["strykerCli"], "--config-file", Config, "--output", Output];
            return (outer, native);
        }
    }
}
