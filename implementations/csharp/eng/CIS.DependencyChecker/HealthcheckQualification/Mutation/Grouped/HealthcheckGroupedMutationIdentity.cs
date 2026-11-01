using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckGroupedMutation
{
    internal const string LinuxIdentityManifestVariable = "CIS_HEALTHCHECK_IDENTITY_MANIFEST";
    internal const string LinuxIdentityTestSource = "tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeForeignOwnerBoundaryTests.cs";
    internal const string LinuxIdentityTestName = "CIS.Tests.UnitTests.Healthcheck.HealthPipeForeignOwnerBoundaryTests.CurrentUserOnlyRejectsAnAccessibleForeignOwnerBeforeSendingARequest";
    internal const string LinuxServerRecoveryTestName = "CIS.Tests.UnitTests.Healthcheck.HealthPipeForeignOwnerBoundaryTests.ForeignPeerRejectionPreservesTheActualOwnersAdmission";

    // Call after the unmutated selected projects have built and before baseline
    // tests. The supplied runner is the existing deadline/capacity monitored Run.
    internal static async Task<JsonObject?> PrepareLinuxIdentityAsync(string root, Candidate candidate, Group group,
        string language, string output, string temporary, string dotnet, IDictionary<string, string?> environment,
        Func<string, string[], string, string, Task<ProcessResult>> run)
    {
        bool requested = group.BuildProperties.TryGetValue("CisLinuxIdentityQualification", out string? value) && value == "true";
        environment[LinuxIdentityManifestVariable] = null;
        if (!requested) { return null; }
        if (!OperatingSystem.IsLinux() || group.Name != "pipes") { throw new InvalidDataException("The foreign-owner cohort is an explicit Linux Pipes lane."); }
        Require(IdentitySame(output, Path.Combine(root, "groups", group.Name)), "The Linux identity context must remain inside its active owned group.");
        Require(environment.TryGetValue("CisLinuxIdentityQualification", out string? flag) && flag == "true", "Compile/runtime Linux identity flags must agree.");
        Require(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12", "The Linux identity lane must run on the actual pinned subject runtime.");
        string home = environment.TryGetValue("DOTNET_CLI_HOME", out string? selectedHome) && selectedHome is not null
            ? selectedHome : Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") ?? "";
        Require(Path.IsPathFullyQualified(home) && Directory.Exists(home) && !AtOrUnder(Path.Combine(root, "workspace"), home)
            && !AtOrUnder("/home", home), "Root SDK work requires an explicit existing task-owned DOTNET_CLI_HOME outside the source workspace and user homes.");
        string marker = Path.Combine(temporary, "owner.json");
        Require(File.Exists(marker) && (string?)Read(marker)["candidateIdentity"] == candidate.Identity && (string?)Read(marker)["group"] == group.Name,
            "Only the active group's exactly marked owned temporary directory can be made shared.");
        string runuser = IdentitySystemTool("runuser", "/usr/sbin/runuser", "/sbin/runuser", "/usr/bin/runuser");
        string id = IdentitySystemTool("id", "/usr/bin/id", "/bin/id");
        string stat = IdentitySystemTool("stat", "/usr/bin/stat", "/bin/stat");
        ProcessResult rootIdentity = await run("identity-root-preflight", ["-u"], language, id);
        ProcessResult nobodyIdentity = await run("identity-nobody-preflight", ["-u", "nobody", "--", id, "-u"], language, runuser);
        Require(rootIdentity.ExitCode == 0 && rootIdentity.OutputDrained && rootIdentity.StandardOutput.Trim() == "0"
            && nobodyIdentity.ExitCode == 0 && nobodyIdentity.OutputDrained && nobodyIdentity.StandardOutput.Trim() == "65534",
            "The actual root orchestrator and existing nobody65534 identity are required before mutation.");
        UnixFileMode previousMode = File.GetUnixFileMode(temporary);
        File.SetUnixFileMode(temporary, (UnixFileMode)1023); // 01777: only this new marked active TMP.
        ProcessResult writable = await run("identity-temporary-preflight",
            ["-u", "nobody", "--", "/bin/sh", "-c", "test -d \"$1\" && test -w \"$1\" && test -x \"$1\"", "cis-identity-preflight", temporary], language, runuser);
        Require(writable.ExitCode == 0 && writable.OutputDrained && File.GetUnixFileMode(temporary) == (UnixFileMode)1023,
            "The actual peer must be able to traverse and create its endpoint in the same shared active TMP.");

        string directory = Path.Combine(output, "identity"), immutable = Path.Combine(directory, "immutable"), journals = Path.Combine(directory, "journal");
        Require(!Directory.Exists(directory), "A Linux identity context already exists; retain it instead of reusing an attempt.");
        Directory.CreateDirectory(journals);
        foreach (string path in new[] { directory, journals }) { File.SetUnixFileMode(path, (UnixFileMode)493); }
        string host = Snapshot("host", Path.Combine(language, "tests/fixtures/HealthcheckHttpHost/bin/Release/net10.0"), "CIS.Healthcheck.HttpFixture.dll");
        string probe = Snapshot("probe", Path.Combine(language, "tools/CisHealthProbe/bin/Release/net10.0"), "CisHealthProbe.dll");
        File.SetUnixFileMode(immutable, (UnixFileMode)365);
        string coreHash = Hash(Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"));
        Require(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")) == coreHash
            && Hash(Path.Combine(Path.GetDirectoryName(probe)!, "CIS.Core.dll")) == coreHash,
            "The immutable fixture/probe must have the actual freshly built uninstrumented Core bytes.");
        var files = Directory.EnumerateFiles(immutable, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => new Input(Path.GetFullPath(path), Hash(path))).ToList();
        string pipes = typeof(System.IO.Pipes.NamedPipeClientStream).Assembly.Location;
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        files.AddRange(new[] { dotnet, runuser, id, stat, "/bin/sh", pipes, typeof(object).Assembly.Location,
            Path.Combine(runtimeDirectory, "System.Net.Sockets.dll"), Path.Combine(runtimeDirectory, "libSystem.Native.so") }
            .Distinct(StringComparer.Ordinal).Select(path => new Input(Path.GetFullPath(path), Hash(path))));
        ProcessResult readable = await run("identity-immutable-readability-preflight",
            ["-u", "nobody", "--", "/bin/sh", "-c", "for identity_file in \"$@\"; do test -r \"$identity_file\" || exit 1; done",
                "cis-identity-readability", .. files.Select(pin => pin.Path)], language, runuser);
        Require(readable.ExitCode == 0 && readable.OutputDrained, "The actual foreign identity cannot traverse/read the immutable fixture, probe and pinned runtime closure.");
        var context = new JsonObject
        {
            ["schema"] = "cis.healthcheck.foreign-owner-context/v1",
            ["candidateIdentity"] = candidate.Identity,
            ["group"] = group.Name,
            ["runtime"] = ".NET 10.0.12",
            ["effectiveUid"] = 0,
            ["peerUid"] = 65534,
            ["temporaryRoot"] = Path.GetFullPath(temporary),
            ["previousTemporaryMode"] = (int)previousMode,
            ["sharedTemporaryMode"] = 1023,
            ["dotnetCliHome"] = Path.GetFullPath(home),
            ["journalRoot"] = journals,
            ["dotnet"] = dotnet,
            ["runuser"] = runuser,
            ["stat"] = stat,
            ["host"] = host,
            ["probe"] = probe,
            ["coreSha256"] = coreHash,
            ["pipesSha256"] = Hash(pipes),
            ["files"] = JsonSerializer.SerializeToNode(files, Json),
            ["testName"] = LinuxIdentityTestName,
            ["roles"] = new JsonArray("client-guard", "server-recovery"),
            ["testSourceSha256"] = candidate.Inputs.Single(pin => pin.Path == LanguagePrefix + LinuxIdentityTestSource).Sha256
        };
        string manifest = Path.Combine(directory, "context.json");
        WriteJson(manifest, context);
        File.SetUnixFileMode(manifest, (UnixFileMode)292);
        environment[LinuxIdentityManifestVariable] = manifest;
        return new JsonObject { ["contextPath"] = manifest, ["contextSha256"] = Hash(manifest), ["journalRoot"] = journals };

        string Snapshot(string name, string source, string assembly)
        {
            Require(Directory.Exists(source) && File.Exists(Path.Combine(source, assembly)), "Build the actual identity fixture/probe closure before snapshotting: " + source);
            string destination = Path.Combine(immutable, name);
            Directory.CreateDirectory(destination);
            File.SetUnixFileMode(immutable, (UnixFileMode)493);
            foreach (string file in Directory.EnumerateFiles(source).Where(path => Path.GetExtension(path) is ".dll" or ".json" or ".so").Order(StringComparer.Ordinal))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                Copy(file, target, Hash(file));
                File.SetUnixFileMode(target, (UnixFileMode)292);
            }
            File.SetUnixFileMode(destination, (UnixFileMode)365);
            return Path.Combine(destination, assembly);
        }
    }

    // Call after native (even on a nonzero native result) and after restored
    // tests, before scratch removal. A native Killed cannot override this hold.
    internal static JsonObject? ValidateLinuxIdentityJournals(string root, Candidate candidate, Group group, JsonObject? binding)
    {
        bool requested = group.BuildProperties.TryGetValue("CisLinuxIdentityQualification", out string? value) && value == "true";
        Require(requested == (binding is not null), "The declared Linux identity lane lacks its bound context.");
        if (!requested) { return null; }
        string contextPath = (string?)binding!["contextPath"] ?? "";
        Require(IdentitySame(contextPath, Path.Combine(root, "groups", group.Name, "identity/context.json")) && Hash(contextPath) == (string?)binding["contextSha256"],
            "The immutable Linux identity context changed or escaped its owned group.");
        JsonObject context = Read(contextPath);
        Require((string?)context["schema"] == "cis.healthcheck.foreign-owner-context/v1" && (string?)context["candidateIdentity"] == candidate.Identity
            && (string?)context["group"] == group.Name && (int?)context["effectiveUid"] == 0 && (int?)context["peerUid"] == 65534
            && (int?)context["sharedTemporaryMode"] == 1023 && (string?)context["runtime"] == ".NET 10.0.12"
            && (string?)context["testName"] == LinuxIdentityTestName
            && (string?)context["testSourceSha256"] == candidate.Inputs.Single(pin => pin.Path == LanguagePrefix + LinuxIdentityTestSource).Sha256,
            "The Linux identity context does not bind this candidate, test, runtime and actual identities.");
        Input[] immutablePins = context["files"]!.Deserialize<Input[]>(Json)!;
        string expectedHost = Path.Combine(Path.GetDirectoryName(contextPath)!, "immutable/host/CIS.Healthcheck.HttpFixture.dll");
        string expectedProbe = Path.Combine(Path.GetDirectoryName(contextPath)!, "immutable/probe/CisHealthProbe.dll");
        Require(immutablePins.Length > 0 && immutablePins.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == immutablePins.Length
            && IdentitySame((string)context["host"]!, expectedHost) && IdentitySame((string)context["probe"]!, expectedProbe),
            "The immutable identity closure is empty, duplicated or outside its declared snapshot.");
        foreach (string path in new[] { expectedHost, expectedProbe, Path.ChangeExtension(expectedHost, ".deps.json"), Path.ChangeExtension(expectedHost, ".runtimeconfig.json"),
            Path.ChangeExtension(expectedProbe, ".deps.json"), Path.ChangeExtension(expectedProbe, ".runtimeconfig.json"),
            Path.Combine(Path.GetDirectoryName(expectedHost)!, "CIS.Core.dll"), Path.Combine(Path.GetDirectoryName(expectedProbe)!, "CIS.Core.dll"),
            (string)context["dotnet"]!, (string)context["runuser"]!, (string)context["stat"]! })
        { Require(immutablePins.Any(pin => IdentitySame(pin.Path, path)), "The immutable identity closure omits a required actual control/tool: " + path); }
        foreach (Input pin in immutablePins) { Require(File.Exists(pin.Path) && Hash(pin.Path) == pin.Sha256, "An immutable Linux identity control/tool changed: " + pin.Path); }
        Require(Hash(Path.Combine(Path.GetDirectoryName(expectedHost)!, "CIS.Core.dll")) == (string?)context["coreSha256"]
            && Hash(Path.Combine(Path.GetDirectoryName(expectedProbe)!, "CIS.Core.dll")) == (string?)context["coreSha256"]
            && immutablePins.Any(pin => Path.GetFileName(pin.Path) == "System.IO.Pipes.dll" && pin.Sha256 == (string?)context["pipesSha256"]),
            "The independently pinned positive Core/BCL controls differ from their prepared context.");
        string journals = (string)context["journalRoot"]!;
        Require(IdentitySame(journals, Path.Combine(Path.GetDirectoryName(contextPath)!, "journal")) && IdentitySame(journals, (string)binding["journalRoot"]!), "The fixture journal root differs from the prepared group.");
        string[] attempts = Directory.GetDirectories(journals).Order(StringComparer.Ordinal).ToArray();
        Require(attempts.Length > 0, "No actual foreign-owner fixture invocation was observed.");
        var pins = new List<Input>();
        var roles = new Dictionary<string, int>(StringComparer.Ordinal) { ["client-guard"] = 0, ["server-recovery"] = 0 };
        Require(JsonNode.DeepEquals(context["roles"], new JsonArray("client-guard", "server-recovery")), "The native identity context must retain both actual client-guard and server-recovery roles.");
        foreach (string attempt in attempts)
        {
            Require(Guid.TryParseExact(Path.GetFileName(attempt), "N", out _), "Unexpected foreign-owner journal entry.");
            Require(!Directory.EnumerateFiles(attempt, "failure.json", SearchOption.AllDirectories).Any(), "A foreign-owner helper/setup/drain/cleanup infrastructure failure invalidates the whole native group.");
            Require(!File.Exists(Path.Combine(attempt, "cleanup-hold.json")), "An actual product owner/socket cleanup hold cannot supply accepted native kill credit.");
            string startedPath = Path.Combine(attempt, "started.json"), terminalPath = Path.Combine(attempt, "terminal.json"), helperTerminal = Path.Combine(attempt, "helper/terminal.json");
            Require(File.Exists(startedPath) && File.Exists(terminalPath), "An interrupted identity attempt lacks its actual started/terminal receipt.");
            JsonObject commonStarted = Read(startedPath), commonTerminal = Read(terminalPath);
            string role = (string?)commonStarted["role"] ?? "";
            Require(roles.ContainsKey(role) && (string?)commonTerminal["role"] == role
                && (string?)commonStarted["schema"] == "cis.healthcheck.foreign-owner-started/v1" && (int?)commonStarted["pid"] is > 0
                && (string?)commonStarted["manifestSha256"] == Hash(contextPath) && (int?)commonTerminal["pid"] == (int?)commonStarted["pid"]
                && (string?)commonTerminal["schema"] == "cis.healthcheck.foreign-owner-terminal/v1" && (string?)commonTerminal["manifestSha256"] == Hash(contextPath)
                && (bool?)commonTerminal["cleanupHealthy"] == true && (bool?)commonTerminal["productOperationSettled"] == true,
                "The actual identity role, producer, context or terminal resource cleanup is missing or unhealthy.");
            roles[role]++;
            if (role == "server-recovery")
            {
                ValidateServerRecoveryJournal(attempt, context, commonTerminal);
                foreach (string path in Directory.EnumerateFiles(attempt, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                { pins.Add(new Input(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(path))); }
                continue;
            }
            string readyPath = Path.Combine(attempt, "helper/ready.json"), controlsPath = Path.Combine(attempt, "controls.json");
            Require(File.Exists(startedPath) && File.Exists(terminalPath) && File.Exists(helperTerminal) && File.Exists(readyPath) && File.Exists(controlsPath), "An interrupted foreign-owner fixture lacks its required ready/control/terminal cleanup receipts.");
            JsonObject started = Read(startedPath), terminal = Read(terminalPath), helper = Read(helperTerminal), ready = Read(readyPath), controls = Read(controlsPath);
            Require((string?)started["schema"] == "cis.healthcheck.foreign-owner-started/v1" && (int?)started["pid"] is > 0
                && (string?)started["manifestSha256"] == Hash(contextPath)
                && (string?)terminal["schema"] == "cis.healthcheck.foreign-owner-terminal/v1" && (int?)terminal["pid"] == (int?)started["pid"]
                && (string?)terminal["manifestSha256"] == Hash(contextPath) && (bool?)terminal["cleanupHealthy"] == true
                && (bool?)terminal["productOperationSettled"] == true
                && (int?)terminal["helperExitCode"] == 0 && (bool?)terminal["helperOutputDrained"] == true
                && (string?)terminal["helperTerminalSha256"] == Hash(helperTerminal)
                && (string?)helper["schema"] == "cis.healthcheck.foreign-peer-terminal/v1" && (bool?)helper["healthy"] == true
                && (bool?)helper["socketRemoved"] == true && (int?)helper["pid"] is > 0,
                "A foreign-owner fixture's started/terminal/peer cleanup evidence is unhealthy or ambiguous.");
            Require((string?)ready["schema"] == "cis.healthcheck.foreign-peer-ready/v1" && (int?)ready["pid"] == (int?)helper["pid"]
                && (string?)ready["runtime"] == ".NET 10.0.12" && (string?)ready["coreSha256"] == (string?)context["coreSha256"]
                && (string?)ready["pipesSha256"] == (string?)context["pipesSha256"]
                && (string?)ready["fixtureSha256"] == Hash((string)context["host"]!)
                && IdentitySame((string)ready["temporaryRoot"]!, (string)context["temporaryRoot"]!)
                && (string?)controls["schema"] == "cis.healthcheck.foreign-owner-controls/v1"
                && IdentitySame((string)controls["socket"]!, (string)ready["socket"]!) && (string?)controls["pipe"] == (string?)ready["pipe"]
                && (string?)controls["socketStat"] == "65534:777:socket" && IdentityUidLine((string?)controls["rootUidLine"], "0")
                && IdentityUidLine((string?)controls["helperUidLine"], "65534") && IdentityUidLine((string?)ready["uidLine"], "65534")
                && (bool?)controls["unrestrictedRootHealthy"] == true && (int?)controls["sameOwnerPid"] is > 0
                && (int?)controls["sameOwnerExitCode"] == 0 && (bool?)controls["sameOwnerOutputDrained"] == true
                && (string?)controls["sameOwnerError"] == ""
                && (string?)JsonNode.Parse((string)controls["sameOwnerOutput"]!)?["healthStatus"] == "Healthy",
                "The actual owner/runtime/socket/positive-control observations do not prove the foreign-server policy boundary.");
            Require((int?)helper["counters"]?["expectedRequests"] is >= 2 && (int?)helper["counters"]?["responses"] is >= 2,
                "The independent unrestricted/same-owner positive controls did not complete.");
            foreach (string path in Directory.EnumerateFiles(attempt, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            { pins.Add(new Input(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(path))); }
        }
        Require(roles.Values.All(count => count > 0), "The actual client-guard and server-recovery cohorts must both execute; neither role can substitute for the other.");
        // Product results (including a mutant's extra request/healthy success)
        // intentionally do not enter infrastructure acceptance.
        return new JsonObject
        {
            ["contextPath"] = contextPath,
            ["contextSha256"] = Hash(contextPath),
            ["journalRoot"] = journals,
            ["attempts"] = attempts.Length,
            ["roles"] = JsonSerializer.SerializeToNode(roles, Json),
            ["cleanupHealthy"] = true,
            ["journalPins"] = JsonSerializer.SerializeToNode(pins, Json)
        };
    }

    internal static JsonObject? ValidateLinuxIdentityAdmission(string root, Candidate candidate, Group group, JsonObject run,
        Definition[] definitions, JsonObject report, Func<string, string> readSource)
    {
        bool requested = group.BuildProperties.TryGetValue("CisLinuxIdentityQualification", out string? value) && value == "true";
        if (!requested)
        {
            Require(run["linuxIdentity"] is null, "A nonidentity group cannot carry a Linux root identity qualification.");
            return null;
        }
        Require(group.Name == "pipes" && (string?)run["candidateIdentity"] == candidate.Identity && (string?)run["group"] == "pipes"
            && (string?)run["executionIdentity"]?["operatingSystem"] == "linux" && (uint?)run["executionIdentity"]?["uid"] == 0
            && (uint?)run["executionIdentity"]?["effectiveUid"] == 0 && run["linuxIdentity"] is JsonObject,
            "Linux Pipes identity admission requires its actual UID0/effectiveUID0 native execution and bound infrastructure receipt.");
        JsonObject receipt = ValidateLinuxIdentityJournals(root, candidate, group, run["linuxIdentity"]!.AsObject())!;
        Require(JsonNode.DeepEquals(receipt, run["linuxIdentity"]), "The retained Linux identity journal/context receipt differs from its completed native run.");
        return new JsonObject
        {
            ["executionIdentity"] = run["executionIdentity"]!.DeepClone(),
            ["infrastructure"] = receipt,
            ["constructorControl"] = ValidateLinuxIdentityControl(definitions, report, readSource),
            ["serverRecoveryControl"] = ValidateLinuxServerRecoveryControl(definitions, report, readSource)
        };
    }

    private static void ValidateServerRecoveryJournal(string attempt, JsonObject context, JsonObject terminal)
    {
        Require((bool?)terminal["actualOwnerReleased"] == true, "The actual instrumented server owner/socket remain a cleanup hold.");
        JsonObject controls = Read(Path.Combine(attempt, "controls.json"));
        Require((string?)controls["schema"] == "cis.healthcheck.server-owner-controls/v1" && IdentityUidLine((string?)controls["rootUidLine"], "0")
            && (string?)controls["baselineSocketStat"] == "0:777:socket" && (bool?)controls["baselineProbeHealthy"] == true,
            "The actual unrestricted immutable root peer/production-probe controls did not pass.");
        bool targetRequested = (bool?)terminal["targetExchangeRequested"] == true || File.Exists(Path.Combine(attempt, "foreign-client/target-go"));
        Require(targetRequested || !string.IsNullOrWhiteSpace((string?)terminal["productException"]), "The recovery role neither exercised its target nor retained an actual product startup failure.");
        if (targetRequested)
        {
            string targetSocket = Path.Combine(attempt, "target-socket.json");
            Require((bool?)terminal["targetSocketPrepared"] == true && File.Exists(targetSocket), "The actual target exchange lacks its required socket permission/setup receipt.");
            JsonObject socket = Read(targetSocket);
            Require((string?)socket["stat"] == "0:777:socket" && (string?)socket["pipe"] == (string?)controls["targetPipe"]
                && IdentitySame((string)socket["socket"]!, Path.Combine((string)context["temporaryRoot"]!, "CoreFxPipe_" + (string)controls["targetPipe"]!)),
                "The instrumented target socket setup differs from the declared actual root pipe0777.");
        }
        foreach ((string role, string uid, string schema) in new[] { ("baseline-peer", "0", "foreign-peer"), ("foreign-client", "65534", "foreign-client") })
        {
            string directory = Path.Combine(attempt, role);
            foreach (string file in new[] { "ready.json", "terminal.json" }) { Require(File.Exists(Path.Combine(directory, file)), "The recovery helper lacks a real ready/terminal receipt: " + role); }
            JsonObject ready = Read(Path.Combine(directory, "ready.json")), finished = Read(Path.Combine(directory, "terminal.json"));
            JsonObject process = Read(Path.Combine(attempt, role + "-process.json")), proc = Read(Path.Combine(attempt, role + "-proc.json"));
            Require((string?)ready["schema"] == "cis.healthcheck." + schema + "-ready/v1" && (int?)ready["pid"] is > 0
                && (int?)finished["pid"] == (int?)ready["pid"] && (string?)finished["schema"] == "cis.healthcheck." + schema + "-terminal/v1"
                && (bool?)finished["healthy"] == true && IdentityUidLine((string?)ready["uidLine"], uid) && IdentityUidLine((string?)proc["uidLine"], uid)
                && (int?)proc["pid"] == (int?)ready["pid"] && (string?)ready["runtime"] == ".NET 10.0.12"
                && (string?)ready["fixtureSha256"] == Hash((string)context["host"]!) && (string?)ready["coreSha256"] == (string?)context["coreSha256"]
                && (string?)ready["pipesSha256"] == (string?)context["pipesSha256"] && IdentitySame((string)ready["temporaryRoot"]!, (string)context["temporaryRoot"]!)
                && (int?)process["ProcessId"] is > 0 && (int?)process["ExitCode"] == 0 && (bool?)process["OutputDrained"] == true && (string?)process["StandardError"] == "",
                "An actual recovery helper's UID/runtime/origin/drain/cleanup failed: " + role);
            if (role == "baseline-peer")
            {
                Require((bool?)finished["socketRemoved"] == true && (int?)finished["counters"]?["responses"] is >= 2
                    && (int?)finished["counters"]?["expectedRequests"] is >= 2
                    && (string?)ready["pipe"] == (string?)controls["positivePipe"], "The immutable root peer did not complete both positive exchanges and actual socket cleanup.");
            }
            else
            {
                JsonObject positive = Read(Path.Combine(directory, "positive.json"));
                Require((bool?)positive["healthy"] == true && (bool?)positive["connected"] == true
                    && (string?)positive["pipe"] == (string?)controls["positivePipe"]
                    && (string?)ready["positivePipe"] == (string?)controls["positivePipe"] && (string?)ready["targetPipe"] == (string?)controls["targetPipe"],
                    "The same foreign raw client did not prove its unrestricted valid-frame exchange before the actual guarded attempt.");
            }
        }
        // Managed notification outcomes and target product results stay raw.
        // An unreachable collected pending cycle is not labeled settled, nor
        // does it poison otherwise proven physical helper/owner cleanup.
    }

    internal static JsonObject ValidateLinuxServerRecoveryControl(Definition[] definitions, JsonObject report, Func<string, string> readSource)
    {
        Require(report["files"] is JsonObject && report["testFiles"] is JsonObject, "Server-recovery admission requires actual native source/test mappings.");
        const string sourceName = "Healthcheck/Pipes/HealthPipeServer.cs";
        string source = readSource(CorePrefix + sourceName);
        const string marker = "catch (UnauthorizedAccessException)";
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        Require(start >= 0 && source.LastIndexOf(marker, StringComparison.Ordinal) == start, "The reviewed actual UID rejection catch changed or is ambiguous.");
        int end = source.IndexOf('}', start), statement = source.IndexOf("continue;", start, StringComparison.Ordinal);
        Require(statement > start && end > statement, "The actual UID rejection catch must contain its real continue statement.");
        int line = 1 + source.AsSpan(0, statement).Count('\n'), column = statement - source.LastIndexOf('\n', statement - 1);
        var location = new JsonObject { ["start"] = new JsonObject { ["line"] = line, ["column"] = column }, ["end"] = new JsonObject { ["line"] = line, ["column"] = column + "continue;".Length } };
        Definition[] matching = definitions.Where(item => item.Source == sourceName && item.Operator == "Statement mutation" && item.Replacement == ";"
            && JsonNode.DeepEquals(JsonNode.Parse(item.Location), location)).ToArray();
        Require(matching.Length == 1 && matching[0].Status == "Killed", "The actual foreign-peer rejection continue removal must be natively Killed; Timeout/NoCoverage/another statement do not qualify.");
        Definition definition = matching[0];
        JsonNode file = report["files"]!.AsObject().Single(item => CoreReportPath(item.Key) == sourceName).Value!;
        JsonNode native = file["mutants"]!.AsArray().Single(item => (string?)item!["id"] == definition.NativeId)!;
        Require((string?)native["status"] == "Killed" && (string?)native["mutatorName"] == "Statement mutation" && (string?)native["replacement"] == ";" && JsonNode.DeepEquals(native["location"], location), "The server-recovery admission differs from its unchanged native definition.");
        string[] killed = native["killedBy"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
        Require(killed.Length > 0 && killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "The rejection-continue removal lacks unambiguous native killedBy identities.");
        KeyValuePair<string, JsonNode?>[] testFiles = report["testFiles"]!.AsObject().Where(item => LanguageReportPath(item.Key) == LinuxIdentityTestSource).ToArray();
        Require(testFiles.Length == 1 && (string?)testFiles[0].Value!["source"] == readSource(LanguagePrefix + LinuxIdentityTestSource), "The actual server-recovery witness source differs from the candidate.");
        JsonNode[] witnesses = testFiles[0].Value!["tests"]!.AsArray().Where(test => (string?)test!["name"] == LinuxServerRecoveryTestName && killed.Contains((string?)test["id"], StringComparer.Ordinal)).Select(test => test!).ToArray();
        Require(witnesses.Length > 0 && witnesses.All(witness => report["testFiles"]!.AsObject().SelectMany(entry => entry.Value!["tests"]!.AsArray()).Count(test => (string?)test!["id"] == (string?)witness["id"]) == 1), "The actual rejection-continue kill lacks its real owner-recovery native witness.");
        return new JsonObject
        {
            ["definitionIdentity"] = definition.Identity,
            ["nativeId"] = definition.NativeId,
            ["source"] = sourceName,
            ["location"] = location,
            ["operator"] = "Statement mutation",
            ["replacement"] = ";",
            ["status"] = "Killed",
            ["testSource"] = LinuxIdentityTestSource,
            ["testName"] = LinuxServerRecoveryTestName,
            ["killedBy"] = new JsonArray(witnesses.Select(witness => witness["id"]!.DeepClone()).ToArray())
        };
    }

    internal static JsonObject ValidateLinuxIdentityControl(Definition[] definitions, JsonObject report, Func<string, string> readSource)
    {
        Require(report["files"] is JsonObject && report["testFiles"] is JsonObject, "The Linux identity admission requires actual native source/test mappings.");
        const string sourceName = "Healthcheck/Pipes/HealthProbeClient.cs";
        const string expression = "PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly";
        const string replacement = "PipeOptions.Asynchronous & PipeOptions.CurrentUserOnly";
        JsonObject location = StatementLocation(readSource(CorePrefix + sourceName), expression);
        Definition[] matches = definitions.Where(item => item.Source == sourceName && item.Operator == "Bitwise mutation"
            && item.Replacement == replacement && JsonNode.DeepEquals(JsonNode.Parse(item.Location), location)).ToArray();
        Require(matches.Length == 1 && matches[0].Status == "Killed", "The actual constructor OR-to-AND CurrentUserOnly removal must be natively Killed; a Timeout or another kill cannot substitute.");
        Definition definition = matches[0];
        JsonNode file = report["files"]!.AsObject().Single(item => CoreReportPath(item.Key) == sourceName).Value!;
        JsonNode native = file["mutants"]!.AsArray().Single(item => (string?)item!["id"] == definition.NativeId)!;
        Require((string?)native["status"] == "Killed" && (string?)native["mutatorName"] == "Bitwise mutation" && (string?)native["replacement"] == replacement
            && JsonNode.DeepEquals(native["location"], location), "The Linux identity admission differs from the unchanged native definition.");
        string[] killed = native["killedBy"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
        Require(killed.Length > 0 && killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "The constructor mutation lacks unambiguous native killedBy identities.");
        KeyValuePair<string, JsonNode?>[] testFiles = report["testFiles"]!.AsObject().Where(item => LanguageReportPath(item.Key) == LinuxIdentityTestSource).ToArray();
        Require(testFiles.Length == 1 && (string?)testFiles[0].Value!["source"] == readSource(LanguagePrefix + LinuxIdentityTestSource), "The actual foreign-owner witness source differs from the candidate.");
        JsonNode[] witnesses = testFiles[0].Value!["tests"]!.AsArray().Where(test => (string?)test!["name"] == LinuxIdentityTestName
            && killed.Contains((string?)test["id"], StringComparer.Ordinal)).Select(test => test!).ToArray();
        Require(witnesses.Length > 0 && witnesses.All(witness => report["testFiles"]!.AsObject().SelectMany(entry => entry.Value!["tests"]!.AsArray())
            .Count(test => (string?)test!["id"] == (string?)witness["id"]) == 1), "The actual constructor kill lacks the unambiguous real foreign-owner native witness.");
        return new JsonObject
        {
            ["definitionIdentity"] = definition.Identity,
            ["nativeId"] = definition.NativeId,
            ["source"] = sourceName,
            ["location"] = location,
            ["replacement"] = replacement,
            ["status"] = "Killed",
            ["testSource"] = LinuxIdentityTestSource,
            ["testName"] = LinuxIdentityTestName,
            ["killedBy"] = new JsonArray(witnesses.Select(witness => witness["id"]!.DeepClone()).ToArray())
        };
    }

    private static string IdentitySystemTool(string name, params string[] paths) => paths.FirstOrDefault(File.Exists)
        ?? throw new InvalidDataException("The explicit Linux identity prerequisite is absent: " + name);

    private static bool IdentitySame(string first, string second) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(first))
        .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IdentityUidLine(string? line, string uid)
    {
        string[] values = (line ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return values.Length == 5 && values[0] == "Uid:" && values[1..].All(value => value == uid);
    }
}
