using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Grouped = CIS.Verification.HealthcheckGroupedMutation;

namespace CIS.Tests.Verification;

// Synthetic schema/receiver contracts only. These tests do not execute an OS
// identity, product mutant or native campaign and supply no qualification data.
[TestClass]
public sealed class HealthcheckForeignOwnerAdmissionTests
{
    private const string Language = "implementations/csharp/";
    private const string SourceName = "Healthcheck/Pipes/HealthProbeClient.cs";
    private const string Expression = "PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly";
    private const string Replacement = "PipeOptions.Asynchronous & PipeOptions.CurrentUserOnly";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [TestMethod]
    public void ConstructorControlRequiresItsExactNativeKillAndForeignOwnerWitness()
    {
        var fixture = ControlFixture();
        string before = fixture.Report.ToJsonString();
        JsonObject result = Grouped.ValidateLinuxIdentityControl(fixture.Definitions, fixture.Report, path => fixture.Sources[path]);
        Assert.AreEqual("Killed", (string?)result["status"]);
        Assert.AreEqual("synthetic-definition", (string?)result["definitionIdentity"]);
        Assert.AreEqual(Grouped.LinuxIdentityTestName, (string?)result["testName"]);
        Assert.AreEqual("witness", (string?)result["killedBy"]![0]);
        Assert.AreEqual(before, fixture.Report.ToJsonString(), "The receiver must preserve raw native statuses and identities.");
    }

    [TestMethod]
    [DataRow("Survived")]
    [DataRow("Timeout")]
    [DataRow("operator")]
    [DataRow("span")]
    [DataRow("replacement")]
    [DataRow("source")]
    [DataRow("witness-name")]
    [DataRow("witness-source")]
    [DataRow("ambiguous-id")]
    [DataRow("missing-killedBy")]
    [DataRow("missing-testFiles")]
    public void AnotherOutcomeOrMappingCannotSubstituteForTheConstructorControl(string defect)
    {
        var fixture = ControlFixture();
        JsonObject native = fixture.Report["files"]![SourceName]!["mutants"]![0]!.AsObject();
        JsonObject testFile = fixture.Report["testFiles"]!.AsObject().Single().Value!.AsObject();
        Grouped.Definition definition = fixture.Definitions[0];
        switch (defect)
        {
            case "Survived":
            case "Timeout": definition = definition with { Status = defect }; native["status"] = defect; break;
            case "operator": definition = definition with { Operator = "Boolean mutation" }; native["mutatorName"] = definition.Operator; break;
            case "span":
                JsonObject location = JsonNode.Parse(definition.Location)!.AsObject();
                location["start"]!["column"] = (int)location["start"]!["column"]! + 1;
                definition = definition with { Location = location.ToJsonString() }; native["location"] = location; break;
            case "replacement": definition = definition with { Replacement = "PipeOptions.None" }; native["replacement"] = definition.Replacement; break;
            case "source": definition = definition with { Source = "Healthcheck/Pipes/Other.cs" }; break;
            case "witness-name": testFile["tests"]![0]!["name"] = "Other.Test"; break;
            case "witness-source": testFile["source"] = "changed synthetic test source"; break;
            case "ambiguous-id": fixture.Report["testFiles"]!["Other.cs"] = new JsonObject { ["tests"] = new JsonArray(new JsonObject { ["id"] = "witness", ["name"] = "Other.Test" }) }; break;
            case "missing-killedBy": native.Remove("killedBy"); break;
            case "missing-testFiles": fixture.Report.Remove("testFiles"); break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        string before = fixture.Report.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateLinuxIdentityControl([definition], fixture.Report, path => fixture.Sources[path]));
        Assert.AreEqual(before, fixture.Report.ToJsonString(), "Rejection must not rewrite native data into an accepted kill.");
    }

    [TestMethod]
    [DataRow("Killed")]
    [DataRow("Survived")]
    [DataRow("Timeout")]
    [DataRow("NoCoverage")]
    [DataRow("operator")]
    [DataRow("span")]
    [DataRow("replacement")]
    [DataRow("witness-name")]
    [DataRow("witness-source")]
    [DataRow("ambiguous-id")]
    public void ServerRejectionContinueNeedsItsActualOwnerRecoveryKill(string outcome)
    {
        var fixture = ControlFixture();
        Grouped.Definition definition = fixture.Definitions[1];
        JsonObject native = fixture.Report["files"]!["Healthcheck/Pipes/HealthPipeServer.cs"]!["mutants"]![0]!.AsObject();
        JsonObject tests = fixture.Report["testFiles"]!.AsObject().Single().Value!.AsObject();
        switch (outcome)
        {
            case "Killed": break;
            case "Survived": case "Timeout": case "NoCoverage": definition = definition with { Status = outcome }; native["status"] = outcome; break;
            case "operator": definition = definition with { Operator = "Boolean mutation" }; native["mutatorName"] = definition.Operator; break;
            case "span":
                JsonObject location = JsonNode.Parse(definition.Location)!.AsObject(); location["start"]!["column"] = (int)location["start"]!["column"]! + 1;
                definition = definition with { Location = location.ToJsonString() }; native["location"] = location; break;
            case "replacement": definition = definition with { Replacement = "return;" }; native["replacement"] = "return;"; break;
            case "witness-name": tests["tests"]![1]!["name"] = "Other.Test"; break;
            case "witness-source": tests["source"] = "changed source"; break;
            case "ambiguous-id": fixture.Report["testFiles"]!["Other.cs"] = new JsonObject { ["tests"] = new JsonArray(new JsonObject { ["id"] = "server-witness", ["name"] = "Other.Test" }) }; break;
            default: throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        string before = fixture.Report.ToJsonString();
        if (outcome == "Killed")
        {
            Assert.AreEqual(Grouped.LinuxServerRecoveryTestName, (string?)Grouped.ValidateLinuxServerRecoveryControl([definition], fixture.Report, path => fixture.Sources[path])["testName"]);
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateLinuxServerRecoveryControl([definition], fixture.Report, path => fixture.Sources[path])); }
        Assert.AreEqual(before, fixture.Report.ToJsonString());
    }

    public static IEnumerable<object[]> JournalCases => OperatingSystem.IsLinux()
        ? new[] { "healthy", "released-owner-pending-notifications", "failure-marker", "helper-failure-marker", "missing-terminal", "missing-helper-terminal", "unsettled-product", "unhealthy-helper",
            "changed-immutable", "changed-context", "changed-journal-bytes", "changed-receipt-pins", "missing-control-pin", "nonroot-execution", "missing-binding",
            "missing-server-role", "server-live-owner", "server-helper-failure", "server-client-positive-failure", "missing-target-socket" }
            .Select(value => new object[] { value })
        : new[] { new object[] { "nonlinux-nonidentity-null-and-refusal" } };

    [TestMethod]
    [DynamicData(nameof(JournalCases))]
    public void JournalAdmissionFollowsTheDeclaredPlatformAndRetainedCleanupEvidence(string scenario)
    {
        var group = new Grouped.Group("pipes", [SourceName], [], "", "perTest");
        if (!OperatingSystem.IsLinux())
        {
            Assert.AreEqual("nonlinux-nonidentity-null-and-refusal", scenario);
            Assert.AreEqual("false", group.BuildProperties["CisLinuxIdentityQualification"]);
            var fixture = ControlFixture();
            var run = new JsonObject();
            var candidate = new Grouped.Candidate("synthetic", "synthetic", "nonlinux", "synthetic", [], [SourceName], "synthetic");
            Assert.IsNull(Grouped.ValidateLinuxIdentityAdmission("synthetic", candidate, group, run, fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
            run["linuxIdentity"] = new JsonObject();
            Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateLinuxIdentityAdmission("synthetic", candidate, group, run, fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
            return;
        }
        Assert.AreEqual("true", group.BuildProperties["CisLinuxIdentityQualification"]);
        using var journal = new JournalFixture(group);
        var control = ControlFixture();
        if (scenario == "released-owner-pending-notifications") { journal.Corrupt(scenario); }
        if (scenario is "healthy" or "released-owner-pending-notifications")
        {
            JsonObject? admitted = Grouped.ValidateLinuxIdentityAdmission(journal.Root, journal.Candidate, group, journal.Run,
                control.Definitions, control.Report, path => control.Sources[path]);
            Assert.IsNotNull(admitted);
            Assert.IsTrue((bool)admitted["infrastructure"]!["cleanupHealthy"]!);
            Assert.AreEqual(2, (int)admitted["infrastructure"]!["attempts"]!);
            Assert.AreEqual("Killed", (string?)admitted["constructorControl"]!["status"]);
            if (scenario == "released-owner-pending-notifications")
            {
                Assert.IsFalse((bool)journal.RecoveryTerminal["managedStopSettled"]!);
                Assert.IsFalse((bool)journal.RecoveryTerminal["managedDisposeSettled"]!);
            }
            return;
        }
        journal.Corrupt(scenario);
        string before = control.Report.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateLinuxIdentityAdmission(journal.Root, journal.Candidate, group, journal.Run,
            control.Definitions, control.Report, path => control.Sources[path]));
        Assert.AreEqual("Killed", control.Definitions[0].Status, "The synthetic raw kill remains raw; refusal supplies no admitted qualification.");
        Assert.AreEqual(before, control.Report.ToJsonString());
    }

    [TestMethod]
    public void ADeclaredNonidentityGroupRejectsAnUnexpectedRootReceipt()
    {
        var fixture = ControlFixture();
        var group = new Grouped.Group("runtime", [], [], "", "perTest");
        var candidate = new Grouped.Candidate("synthetic", "synthetic", "synthetic", "synthetic", [], [], "synthetic");
        var run = new JsonObject();
        Assert.IsNull(Grouped.ValidateLinuxIdentityAdmission("synthetic", candidate, group, run, fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
        run["linuxIdentity"] = new JsonObject();
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateLinuxIdentityAdmission("synthetic", candidate, group, run, fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
    }

    private static (Grouped.Definition[] Definitions, JsonObject Report, Dictionary<string, string> Sources) ControlFixture()
    {
        string source = "// Synthetic receiver-schema control, not executed product source.\nvar options = " + Expression + ";\n";
        const string testSource = "// Synthetic native test-source mapping only.";
        int offset = source.IndexOf(Expression, StringComparison.Ordinal), line = 1 + source.AsSpan(0, offset).Count('\n');
        int column = offset - source.LastIndexOf('\n', offset - 1);
        var location = new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = line, ["column"] = column },
            ["end"] = new JsonObject { ["line"] = line, ["column"] = column + Expression.Length }
        };
        var definition = new Grouped.Definition(SourceName, HashText(source), "native-synthetic", "synthetic-definition", "Bitwise mutation", location.ToJsonString(), Replacement, "Killed");
        var report = new JsonObject
        {
            ["files"] = new JsonObject
            {
                [SourceName] = new JsonObject
                {
                    ["source"] = source,
                    ["mutants"] = new JsonArray(new JsonObject
                    {
                        ["id"] = definition.NativeId,
                        ["mutatorName"] = definition.Operator,
                        ["replacement"] = Replacement,
                        ["location"] = location,
                        ["status"] = "Killed",
                        ["killedBy"] = new JsonArray("witness")
                    })
                }
            },
            ["testFiles"] = new JsonObject
            {
                [Grouped.LinuxIdentityTestSource] = new JsonObject
                {
                    ["source"] = testSource,
                    ["tests"] = new JsonArray(new JsonObject { ["id"] = "witness", ["name"] = Grouped.LinuxIdentityTestName })
                }
            }
        };
        const string serverName = "Healthcheck/Pipes/HealthPipeServer.cs";
        const string serverSource = "// Synthetic receiver-schema control only.\ncatch (UnauthorizedAccessException)\n{\n    continue;\n}\n";
        int statement = serverSource.IndexOf("continue;", StringComparison.Ordinal), serverLine = 1 + serverSource.AsSpan(0, statement).Count('\n');
        int serverColumn = statement - serverSource.LastIndexOf('\n', statement - 1);
        var serverLocation = new JsonObject { ["start"] = new JsonObject { ["line"] = serverLine, ["column"] = serverColumn }, ["end"] = new JsonObject { ["line"] = serverLine, ["column"] = serverColumn + 9 } };
        var serverDefinition = new Grouped.Definition(serverName, HashText(serverSource), "server-synthetic", "server-definition", "Statement mutation", serverLocation.ToJsonString(), ";", "Killed");
        report["files"]![serverName] = new JsonObject
        {
            ["source"] = serverSource,
            ["mutants"] = new JsonArray(new JsonObject
            { ["id"] = serverDefinition.NativeId, ["mutatorName"] = "Statement mutation", ["replacement"] = ";", ["location"] = serverLocation, ["status"] = "Killed", ["killedBy"] = new JsonArray("server-witness") })
        };
        report["testFiles"]![Grouped.LinuxIdentityTestSource]!["tests"]!.AsArray().Add(new JsonObject { ["id"] = "server-witness", ["name"] = Grouped.LinuxServerRecoveryTestName });
        return ([definition, serverDefinition], report, new Dictionary<string, string>(StringComparer.Ordinal)
        { [Language + "src/CIS.Core/" + SourceName] = source, [Language + "src/CIS.Core/" + serverName] = serverSource, [Language + Grouped.LinuxIdentityTestSource] = testSource });
    }

    private sealed class JournalFixture : IDisposable
    {
        private readonly TemporaryDirectory owned = new();
        private readonly string contextPath, attempt, helperTerminal, terminalPath, firstImmutable, recoveryAttempt;
        internal string Root => owned.Path;
        internal Grouped.Candidate Candidate { get; }
        internal JsonObject Run { get; }
        internal JsonObject Context { get; }
        internal JsonObject RecoveryTerminal => JsonNode.Parse(File.ReadAllText(Path.Combine(recoveryAttempt, "terminal.json")))!.AsObject();

        internal JournalFixture(Grouped.Group group)
        {
            string directory = Path.Combine(Root, "groups/pipes/identity");
            contextPath = Path.Combine(directory, "context.json");
            string journals = Path.Combine(directory, "journal");
            attempt = Path.Combine(journals, Guid.NewGuid().ToString("N"));
            helperTerminal = Path.Combine(attempt, "helper/terminal.json");
            terminalPath = Path.Combine(attempt, "terminal.json");
            string host = Add("immutable/host/CIS.Healthcheck.HttpFixture.dll", "synthetic-host"), probe = Add("immutable/probe/CisHealthProbe.dll", "synthetic-probe");
            firstImmutable = host;
            Add("immutable/host/CIS.Healthcheck.HttpFixture.deps.json", "{}"); Add("immutable/host/CIS.Healthcheck.HttpFixture.runtimeconfig.json", "{}");
            Add("immutable/probe/CisHealthProbe.deps.json", "{}"); Add("immutable/probe/CisHealthProbe.runtimeconfig.json", "{}");
            string hostCore = Add("immutable/host/CIS.Core.dll", "synthetic-core"); Add("immutable/probe/CIS.Core.dll", "synthetic-core");
            string dotnet = Add("tools/dotnet", "synthetic-dotnet"), runuser = Add("tools/runuser", "synthetic-runuser"), stat = Add("tools/stat", "synthetic-stat");
            string pipes = Add("tools/System.IO.Pipes.dll", "synthetic-pipes");
            Grouped.Input[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new Grouped.Input(path, Hash(path))).ToArray();
            string witnessHash = HashText("synthetic maintained witness pin");
            Candidate = new Grouped.Candidate("synthetic", "synthetic-candidate", "synthetic-linux", "synthetic", [new(Language + Grouped.LinuxIdentityTestSource, witnessHash)], [SourceName], "synthetic");
            Context = new JsonObject
            {
                ["schema"] = "cis.healthcheck.foreign-owner-context/v1",
                ["candidateIdentity"] = Candidate.Identity,
                ["group"] = "pipes",
                ["runtime"] = ".NET 10.0.12",
                ["effectiveUid"] = 0,
                ["peerUid"] = 65534,
                ["sharedTemporaryMode"] = 1023,
                ["temporaryRoot"] = Path.Combine(Root, "synthetic-tmp"),
                ["journalRoot"] = journals,
                ["host"] = host,
                ["probe"] = probe,
                ["dotnet"] = dotnet,
                ["runuser"] = runuser,
                ["stat"] = stat,
                ["coreSha256"] = Hash(hostCore),
                ["pipesSha256"] = Hash(pipes),
                ["testName"] = Grouped.LinuxIdentityTestName,
                ["testSourceSha256"] = witnessHash,
                ["files"] = JsonSerializer.SerializeToNode(files, Json)
            };
            Context["roles"] = new JsonArray("client-guard", "server-recovery");
            Write(contextPath, Context);
            Write(Path.Combine(attempt, "started.json"), new JsonObject { ["schema"] = "cis.healthcheck.foreign-owner-started/v1", ["role"] = "client-guard", ["pid"] = 41, ["manifestSha256"] = Hash(contextPath) });
            var counters = new JsonObject { ["connections"] = 3, ["requests"] = 2, ["expectedRequests"] = 2, ["responses"] = 2, ["emptyConnections"] = 1 };
            Write(helperTerminal, new JsonObject { ["schema"] = "cis.healthcheck.foreign-peer-terminal/v1", ["pid"] = 42, ["healthy"] = true, ["socketRemoved"] = true, ["counters"] = counters });
            string socket = Path.Combine(Root, "synthetic-tmp/CoreFxPipe_cis-foreign-synthetic");
            Write(Path.Combine(attempt, "helper/ready.json"), new JsonObject
            {
                ["schema"] = "cis.healthcheck.foreign-peer-ready/v1",
                ["pid"] = 42,
                ["runtime"] = ".NET 10.0.12",
                ["coreSha256"] = Hash(hostCore),
                ["pipesSha256"] = Hash(pipes),
                ["fixtureSha256"] = Hash(host),
                ["temporaryRoot"] = (string)Context["temporaryRoot"]!,
                ["pipe"] = "cis-foreign-synthetic",
                ["socket"] = socket,
                ["uidLine"] = "Uid:\t65534\t65534\t65534\t65534"
            });
            Write(Path.Combine(attempt, "controls.json"), new JsonObject
            {
                ["schema"] = "cis.healthcheck.foreign-owner-controls/v1",
                ["socket"] = socket,
                ["pipe"] = "cis-foreign-synthetic",
                ["socketStat"] = "65534:777:socket",
                ["rootUidLine"] = "Uid:\t0\t0\t0\t0",
                ["helperUidLine"] = "Uid:\t65534\t65534\t65534\t65534",
                ["unrestrictedRootHealthy"] = true,
                ["sameOwnerPid"] = 43,
                ["sameOwnerExitCode"] = 0,
                ["sameOwnerOutputDrained"] = true,
                ["sameOwnerError"] = "",
                ["sameOwnerOutput"] = "{\"healthStatus\":\"Healthy\"}"
            });
            Write(terminalPath, new JsonObject
            {
                ["schema"] = "cis.healthcheck.foreign-owner-terminal/v1",
                ["role"] = "client-guard",
                ["pid"] = 41,
                ["manifestSha256"] = Hash(contextPath),
                ["cleanupHealthy"] = true,
                ["productOperationSettled"] = true,
                ["helperExitCode"] = 0,
                ["helperOutputDrained"] = true,
                ["helperTerminalSha256"] = Hash(helperTerminal)
            });
            recoveryAttempt = Path.Combine(journals, Guid.NewGuid().ToString("N"));
            Write(Path.Combine(recoveryAttempt, "started.json"), new JsonObject { ["schema"] = "cis.healthcheck.foreign-owner-started/v1", ["role"] = "server-recovery", ["pid"] = 51, ["manifestSha256"] = Hash(contextPath) });
            Write(Path.Combine(recoveryAttempt, "terminal.json"), new JsonObject
            {
                ["schema"] = "cis.healthcheck.foreign-owner-terminal/v1",
                ["role"] = "server-recovery",
                ["pid"] = 51,
                ["manifestSha256"] = Hash(contextPath),
                ["cleanupHealthy"] = true,
                ["productOperationSettled"] = true,
                ["actualOwnerReleased"] = true,
                ["managedStopSettled"] = true,
                ["managedDisposeSettled"] = true,
                ["targetSocketPrepared"] = true,
                ["targetExchangeRequested"] = true
            });
            Write(Path.Combine(recoveryAttempt, "controls.json"), new JsonObject
            {
                ["schema"] = "cis.healthcheck.server-owner-controls/v1",
                ["rootUidLine"] = "Uid:\t0\t0\t0\t0",
                ["baselineSocketStat"] = "0:777:socket",
                ["baselineProbeHealthy"] = true,
                ["positivePipe"] = "cis-foreign-positive",
                ["targetPipe"] = "cis-foreign-target"
            });
            Write(Path.Combine(recoveryAttempt, "target-socket.json"), new JsonObject
            {
                ["socket"] = Path.Combine((string)Context["temporaryRoot"]!, "CoreFxPipe_cis-foreign-target"),
                ["pipe"] = "cis-foreign-target",
                ["stat"] = "0:777:socket"
            });
            foreach ((string role, string uid, string schema, int pid) in new[] { ("baseline-peer", "0", "foreign-peer", 52), ("foreign-client", "65534", "foreign-client", 53) })
            {
                var ready = new JsonObject
                {
                    ["schema"] = "cis.healthcheck." + schema + "-ready/v1",
                    ["pid"] = pid,
                    ["uidLine"] = "Uid:\t" + string.Join('\t', Enumerable.Repeat(uid, 4)),
                    ["runtime"] = ".NET 10.0.12",
                    ["fixtureSha256"] = Hash(host),
                    ["coreSha256"] = Hash(hostCore),
                    ["pipesSha256"] = Hash(pipes),
                    ["temporaryRoot"] = (string)Context["temporaryRoot"]!,
                    ["pipe"] = "cis-foreign-positive",
                    ["positivePipe"] = "cis-foreign-positive",
                    ["targetPipe"] = "cis-foreign-target"
                };
                Write(Path.Combine(recoveryAttempt, role, "ready.json"), ready);
                Write(Path.Combine(recoveryAttempt, role, "terminal.json"), new JsonObject
                {
                    ["schema"] = "cis.healthcheck." + schema + "-terminal/v1",
                    ["pid"] = pid,
                    ["healthy"] = true,
                    ["socketRemoved"] = true,
                    ["counters"] = new JsonObject { ["responses"] = 2, ["expectedRequests"] = 2 }
                });
                Write(Path.Combine(recoveryAttempt, role + "-proc.json"), new JsonObject { ["pid"] = pid, ["uidLine"] = (string)ready["uidLine"]! });
                Write(Path.Combine(recoveryAttempt, role + "-process.json"), new JsonObject { ["ProcessId"] = pid, ["ExitCode"] = 0, ["OutputDrained"] = true, ["StandardError"] = "" });
            }
            Write(Path.Combine(recoveryAttempt, "foreign-client/positive.json"), new JsonObject { ["healthy"] = true, ["connected"] = true, ["pipe"] = "cis-foreign-positive" });
            var binding = new JsonObject { ["contextPath"] = contextPath, ["contextSha256"] = Hash(contextPath), ["journalRoot"] = journals };
            Run = new JsonObject
            {
                ["candidateIdentity"] = Candidate.Identity,
                ["group"] = "pipes",
                ["executionIdentity"] = new JsonObject { ["operatingSystem"] = "linux", ["uid"] = 0u, ["effectiveUid"] = 0u },
                ["linuxIdentity"] = Grouped.ValidateLinuxIdentityJournals(Root, Candidate, group, binding)
            };

            string Add(string relative, string contents)
            {
                string path = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contents);
                return path;
            }
        }

        internal void Corrupt(string scenario)
        {
            switch (scenario)
            {
                case "failure-marker": File.WriteAllText(Path.Combine(attempt, "failure.json"), "synthetic infrastructure failure"); break;
                case "helper-failure-marker": File.WriteAllText(Path.Combine(attempt, "helper/failure.json"), "synthetic helper failure"); break;
                case "missing-terminal": File.Delete(terminalPath); break;
                case "missing-helper-terminal": File.Delete(helperTerminal); break;
                case "unsettled-product": Edit(terminalPath, value => value["productOperationSettled"] = false); break;
                case "unhealthy-helper":
                    Edit(helperTerminal, value => value["healthy"] = false);
                    Edit(terminalPath, value => value["helperTerminalSha256"] = Hash(helperTerminal)); break;
                case "changed-immutable": File.AppendAllText(firstImmutable, "changed"); break;
                case "changed-context": File.AppendAllText(contextPath, " "); break;
                case "changed-journal-bytes": File.AppendAllText(Path.Combine(attempt, "controls.json"), " "); break;
                case "changed-receipt-pins": Run["linuxIdentity"]!["journalPins"]![0]!["sha256"] = "changed"; break;
                case "missing-control-pin":
                    Context["files"]!.AsArray().RemoveAt(0);
                    Write(contextPath, Context);
                    Run["linuxIdentity"]!["contextSha256"] = Hash(contextPath);
                    break;
                case "nonroot-execution": Run["executionIdentity"]!["effectiveUid"] = 1000u; break;
                case "missing-binding": Run["linuxIdentity"] = null; break;
                case "missing-server-role": Directory.Delete(recoveryAttempt, recursive: true); break;
                case "server-live-owner": Edit(Path.Combine(recoveryAttempt, "terminal.json"), value => value["actualOwnerReleased"] = false); break;
                case "server-helper-failure": File.WriteAllText(Path.Combine(recoveryAttempt, "foreign-client/failure.json"), "synthetic helper failure"); break;
                case "server-client-positive-failure": Edit(Path.Combine(recoveryAttempt, "foreign-client/positive.json"), value => value["healthy"] = false); break;
                case "missing-target-socket": File.Delete(Path.Combine(recoveryAttempt, "target-socket.json")); break;
                case "released-owner-pending-notifications":
                    Edit(Path.Combine(recoveryAttempt, "terminal.json"), value => { value["managedStopSettled"] = false; value["managedDisposeSettled"] = false; });
                    Run["linuxIdentity"] = Grouped.ValidateLinuxIdentityJournals(Root, Candidate, new("pipes", [SourceName], [], "", "perTest"), Run["linuxIdentity"]!.AsObject());
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }
        public void Dispose() => owned.Dispose();
        private static void Edit(string path, Action<JsonObject> change) { JsonObject value = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); change(value); Write(path, value); }
    }

    private static void Write(string path, JsonObject value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value.ToJsonString(Json)); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
