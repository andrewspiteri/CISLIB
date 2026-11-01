using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckGroupedMutation
{
    internal sealed record PilotControl(string Purpose, string DefinitionIdentity, string ExpectedStatus, string Requirement, string Reviewer, string Rationale);
    private sealed record AsyncWitness(string Source, string Name);
    private sealed record AsyncRequirement(string Source, string Statement, AsyncWitness[] Witnesses);

    private static AsyncRequirement[] TransportAsyncRequirements(string group) => group switch
    {
        "http" => [new("Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "await WriteAsync(context, report, status).ConfigureAwait(false);",
            [new("tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckHttpTransportTests.cs",
                "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.ActualBodyWriteCompletionFaultAndCancellationGovernTheHealthRequest")])],
        "pipes" =>
        [
            new("Healthcheck/Pipes/HealthProbeClient.cs", "await WriteErrorAsync(standardError, \"TimeoutOrCanceled\").ConfigureAwait(false);",
                [new("tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeActiveCancellationBoundaryTests.cs",
                    "CIS.Tests.UnitTests.Healthcheck.HealthPipeActiveCancellationBoundaryTests.CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse")]),
            new("Healthcheck/Pipes/HealthPipeServer.cs", "await stopCompletion.Task.ConfigureAwait(false);",
                [new("tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeActiveCancellationBoundaryTests.cs",
                    "CIS.Tests.UnitTests.Healthcheck.HealthPipeActiveCancellationBoundaryTests.ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource"),
                 new("tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckPipeTransportTests.cs",
                    "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckPipeTransportTests.DisposalWaitsForAnActuallyRunningSnapshotSource")])
        ],
        _ => []
    };

    private static string CandidateSource(string root, Candidate candidate, string relative)
    {
        string path = Path.Combine(root, "workspace", relative);
        Require(candidate.Inputs.Any(pin => pin.Path == relative && pin.Sha256 == Hash(path)), "An async control's actual shipping/test source differs from the frozen candidate: " + relative);
        return File.ReadAllText(path);
    }

    // One small rule per actual async transport path; the whole native group report remains the evidence.
    internal static JsonArray ValidateTransportAsyncControls(string group, Definition[] definitions, JsonObject report, Func<string, string> readSource)
    {
        var controls = new JsonArray();
        foreach (AsyncRequirement requirement in TransportAsyncRequirements(group))
        {
            Require(report["files"] is JsonObject && report["testFiles"] is JsonObject, "Actual async admission needs the native source and test witness mappings.");
            string source = readSource(CorePrefix + requirement.Source);
            JsonObject expected = StatementLocation(source, requirement.Statement);
            Definition[] matching = definitions.Where(item => item.Source == requirement.Source
                && JsonNode.DeepEquals(JsonNode.Parse(item.Location), expected)).ToArray();
            Require(matching.Length == 1, "Missing or ambiguous actual async statement-removal control: " + requirement.Source);
            Definition definition = matching[0];
            Require(definition.Status == "Killed" && definition.Operator == "Statement mutation" && definition.Replacement == ";",
                "The actual async transport statement removal must be natively Killed, with its exact operator/span/replacement; Timeout has no kill credit: " + requirement.Source);
            JsonNode nativeFile = report["files"]!.AsObject().Single(item => CoreReportPath(item.Key) == requirement.Source).Value!;
            JsonNode native = nativeFile["mutants"]!.AsArray().Single(item => (string?)item!["id"] == definition.NativeId)!;
            Require((string?)native["status"] == "Killed" && (string?)native["mutatorName"] == definition.Operator
                && (string?)native["replacement"] == definition.Replacement && JsonNode.DeepEquals(native["location"], expected), "Async control differs from its original native definition.");
            string[] killed = native["killedBy"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
            Require(killed.Length > 0 && killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "Actual async kill has no unambiguous native killedBy witnesses.");
            AsyncWitness? chosen = null;
            JsonNode[] witnesses = [];
            foreach (AsyncWitness allowed in requirement.Witnesses)
            {
                KeyValuePair<string, JsonNode?>[] testFiles = report["testFiles"]!.AsObject().Where(item => LanguageReportPath(item.Key) == allowed.Source).ToArray();
                Require(testFiles.Length <= 1, "Native async test-source mapping is ambiguous: " + allowed.Source);
                if (testFiles.Length == 0) { continue; }
                JsonNode[] matchingWitnesses = testFiles[0].Value!["tests"]!.AsArray().Where(item => killed.Contains((string?)item!["id"], StringComparer.Ordinal)
                    && (string?)item!["name"] == allowed.Name).Select(item => item!).ToArray();
                if (matchingWitnesses.Length == 0) { continue; }
                Require((string?)testFiles[0].Value!["source"] == readSource(LanguagePrefix + allowed.Source),
                    "Async kill's native test-source mapping differs from the exact maintained test file.");
                chosen = allowed;
                witnesses = matchingWitnesses;
                break;
            }
            if (chosen is null) { throw new InvalidDataException("The actual async kill is missing its required native product-test witness: " + requirement.Source); }
            foreach (JsonNode witness in witnesses)
            {
                string id = witness["id"]!.GetValue<string>();
                Require(report["testFiles"]!.AsObject().SelectMany(file => file.Value!["tests"]!.AsArray()).Count(test => (string?)test!["id"] == id) == 1,
                    "Native async killedBy test identity is ambiguous.");
            }
            controls.Add(new JsonObject
            {
                ["source"] = requirement.Source,
                ["definitionIdentity"] = definition.Identity,
                ["nativeId"] = definition.NativeId,
                ["statement"] = requirement.Statement,
                ["location"] = expected,
                ["status"] = definition.Status,
                ["testSource"] = chosen.Source,
                ["testName"] = chosen.Name,
                ["killedBy"] = new JsonArray(witnesses.Select(witness => witness["id"]!.DeepClone()).ToArray())
            });
        }
        return controls;
    }

    private static JsonArray ValidateCompletedTransportAsyncControls(string root, Candidate candidate, Group group)
    {
        string directory = Path.Combine(root, "groups", group.Name), reportPath = Path.Combine(directory, "native/reports/mutation-report.json");
        JsonObject run = Read(Path.Combine(directory, "run.json"));
        Require((string?)run["schema"] == "cis.healthcheck.grouped-run/v1" && (string?)run["candidateIdentity"] == candidate.Identity
            && (string?)run["group"] == group.Name && (string?)run["configSha256"] == Hash(Path.Combine(root, "profiles", group.Name + ".json"))
            && (string?)run["reportSha256"] == Hash(reportPath) && (string?)run["toolchainSha256"] == Hash(Path.Combine(root, "toolchain.json")),
            "Transport async admission belongs to another candidate/configuration/toolchain/native report.");
        Require(run["sources"]!.AsArray().Select(item => item!.GetValue<string>()).SequenceEqual(group.Sources, StringComparer.Ordinal), "Transport async admission source inventory differs from its whole group.");
        ValidateExecution(run);
        ValidateContext(run, group);
        ValidateSdkMsBuildEnvironment(run, ReadToolchain(root, candidate));
        Input[] reports = run["nativeTrxPins"]!.Deserialize<Input[]>(Json)!;
        foreach (string project in group.Projects)
        {
            foreach (string phase in new[] { "baseline", "restored" })
            {
                string name = phase + "-" + Path.GetFileNameWithoutExtension(project) + ".trx", path = Path.Combine(directory, name);
                Require(reports.Single(pin => pin.Path == name).Sha256 == Hash(path), "Async admission baseline/restored report changed.");
                PassingTrx(path);
            }
        }
        JsonObject report = Read(reportPath);
        Definition[] definitions = Definitions(report, group.Sources, candidate.Inputs);
        JsonArray controls = ValidateTransportAsyncControls(group.Name, definitions, report, relative => CandidateSource(root, candidate, relative));
        ValidateLinuxIdentityAdmission(root, candidate, group, run, definitions, report, relative => CandidateSource(root, candidate, relative));
        return controls;
    }

    private static string CoreReportPath(string path)
    {
        string normalized = path.Replace('\\', '/'); const string marker = "/src/CIS.Core/";
        int start = normalized.LastIndexOf(marker, StringComparison.Ordinal);
        return start >= 0 ? normalized[(start + marker.Length)..] : normalized;
    }

    private static string LanguageReportPath(string path)
    {
        string normalized = path.Replace('\\', '/'); const string marker = "/implementations/csharp/";
        int start = normalized.LastIndexOf(marker, StringComparison.Ordinal);
        return start >= 0 ? normalized[(start + marker.Length)..] : normalized;
    }

    private static JsonObject StatementLocation(string source, string statement)
    {
        int offset = source.IndexOf(statement, StringComparison.Ordinal);
        Require(offset >= 0 && source.LastIndexOf(statement, StringComparison.Ordinal) == offset, "The reviewed actual async statement changed or is ambiguous.");
        int line = 1 + source.AsSpan(0, offset).Count('\n');
        int column = offset - (offset == 0 ? -1 : source.LastIndexOf('\n', offset - 1));
        return new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = line, ["column"] = column },
            ["end"] = new JsonObject { ["line"] = line, ["column"] = column + statement.Length }
        };
    }

    private static async Task<int> PilotControlsAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["candidate-root", "controls", "stryker-cli"], []);
        string root = input.PathOf("candidate-root");
        Require(OwnedBytes(root, skipWorkspace: true) <= RetainedLimit, "Aggregate retained evidence already exceeds 256 MiB.");
        Candidate candidate = CandidateAt(root);
        Toolchain tools = ReadToolchain(root, candidate);
        JsonObject run = Read(Path.Combine(root, "groups/pilot/run.json"));
        ValidateExecution(run);
        ValidateContext(run, Groups(candidate).Single(group => group.Name == "pilot"));
        ValidateSdkMsBuildEnvironment(run, tools);
        Require((string?)run["toolchainSha256"] == Hash(Path.Combine(root, "toolchain.json")), "The actual pilot native toolchain identity changed.");
        string report = Path.Combine(root, "groups/pilot/native/reports/mutation-report.json");
        Require((string?)run["reportSha256"] == Hash(report), "The actual pilot report changed.");
        Definition[] definitions = Definitions(Read(report), Groups(candidate).Single(group => group.Name == "pilot").Sources, candidate.Inputs);
        PilotControl[] controls = JsonSerializer.Deserialize<PilotControl[]>(File.ReadAllText(input.PathOf("controls")), Json)
            ?? throw new InvalidDataException("Missing reviewed native control definitions.");
        ValidateControls(root, candidate, definitions, controls);
        string tool = input.PathOf("stryker-cli");
        Require(Hash(tool) == (string?)run["strykerCliSha256"], "Infrastructure control must use the actual pilot native CLI.");
        string directory = Path.Combine(root, "infrastructure-control");
        Require(!Directory.Exists(directory), "An infrastructure control attempt already exists; preserve its result.");
        Directory.CreateDirectory(directory);
        string invalid = Path.Combine(directory, "invalid-config.json");
        Write(invalid, "{ definitely invalid native configuration }");
        string[] command = ["exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", tool, "--config-file", invalid,
            "--output", Path.Combine(directory, "native"), "--skip-version-check"];
        ProcessResult failure = await ProcessRunner.RunAsync(tools.ToolDotnet, command, Path.Combine(root, "workspace", LanguagePrefix, "src/CIS.Core"),
            TimeSpan.FromSeconds(30), maximumCapturedBytesPerStream: 65536);
        Require(failure.ExitCode != 0 && failure.OutputDrained, "Deliberately invalid native initialization did not fail cleanly.");
        WriteRetained(root, Path.Combine(directory, "native.log"), failure.StandardOutput + failure.StandardError);
        // Carry the real infrastructure exit through the receiver even if a broken adapter supplies all Killed.
        JsonObject unhealthy = run.DeepClone().AsObject();
        unhealthy["nativeExitCode"] = failure.ExitCode;
        bool rejected = false;
        try { ValidateExecution(unhealthy); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Infrastructure failure was accepted as product kill evidence.");
        var receipt = new JsonObject
        {
            ["schema"] = "cis.healthcheck.grouped-pilot-controls/v1",
            ["candidateIdentity"] = candidate.Identity,
            ["pilotReportSha256"] = Hash(report),
            ["pilotRunSha256"] = Hash(Path.Combine(root, "groups/pilot/run.json")),
            ["toolchainSha256"] = Hash(Path.Combine(root, "toolchain.json")),
            ["controls"] = JsonSerializer.SerializeToNode(controls, Json),
            ["infrastructure"] = new JsonObject
            {
                ["executable"] = tools.ToolDotnet,
                ["arguments"] = JsonSerializer.SerializeToNode(command),
                ["exitCode"] = failure.ExitCode,
                ["outputDrained"] = failure.OutputDrained,
                ["logSha256"] = Hash(Path.Combine(directory, "native.log")),
                ["receiverRejection"] = "Infrastructure/initialization/abort/cleanup failure cannot supply product kill credit"
            }
        };
        WriteRetained(root, Path.Combine(root, "pilot-controls.json"), receipt.ToJsonString(Json));
        Console.WriteLine("Current native baseline, genuine product kill, actual async settlement-path kill, reviewed survivor, infrastructure rejection and healthy restoration are bound to the pilot report.");
        return 0;
    }

    private static void ValidatePilot(string root, Candidate candidate)
    {
        JsonObject controls = Read(Path.Combine(root, "pilot-controls.json"));
        Toolchain tools = ReadToolchain(root, candidate);
        Require((string?)controls["schema"] == "cis.healthcheck.grouped-pilot-controls/v1" && (string?)controls["candidateIdentity"] == candidate.Identity,
            "Current native pilot controls are required before expansion.");
        string report = Path.Combine(root, "groups/pilot/native/reports/mutation-report.json"), run = Path.Combine(root, "groups/pilot/run.json");
        Require((string?)controls["pilotReportSha256"] == Hash(report) && (string?)controls["pilotRunSha256"] == Hash(run), "Pilot control input identity changed.");
        ValidateExecution(Read(run));
        ValidateContext(Read(run), Groups(candidate).Single(group => group.Name == "pilot"));
        Require((string?)controls["toolchainSha256"] == Hash(Path.Combine(root, "toolchain.json")) && (string?)Read(run)["toolchainSha256"] == Hash(Path.Combine(root, "toolchain.json")),
            "Pilot control native tool/SDK/runtime/adapter identity changed.");
        Definition[] definitions = Definitions(Read(report), Groups(candidate).Single(group => group.Name == "pilot").Sources, candidate.Inputs);
        ValidateControls(root, candidate, definitions, controls["controls"]!.Deserialize<PilotControl[]>(Json)!);
        JsonObject infrastructure = controls["infrastructure"]!.AsObject();
        Require((int?)infrastructure["exitCode"] is not null and not 0 && (bool?)infrastructure["outputDrained"] == true
            && (string?)infrastructure["executable"] == tools.ToolDotnet
            && (string?)infrastructure["logSha256"] == Hash(Path.Combine(root, "infrastructure-control/native.log"))
            && (string?)infrastructure["receiverRejection"] == "Infrastructure/initialization/abort/cleanup failure cannot supply product kill credit",
            "A healthy rejected native infrastructure control is required before expansion.");
    }

    private static void ValidateControls(string root, Candidate candidate, Definition[] definitions, PilotControl[] controls)
    {
        Require(controls.Length == 3 && controls.Select(item => item.Purpose).Order(StringComparer.Ordinal).SequenceEqual(new[] { "knownProductKill", "knownAsyncProductKill", "knownSurvivor" }.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            && controls.Select(item => item.DefinitionIdentity).Distinct(StringComparer.Ordinal).Count() == controls.Length,
            "The pilot requires a genuine product kill, a distinct actual async-path kill and a known survivor/control.");
        string authorityPath = Path.Combine(root, "workspace", LanguagePrefix, "verification/mutation/healthcheck/review-authority.json");
        string relative = LanguagePrefix + "verification/mutation/healthcheck/review-authority.json";
        Require(candidate.Inputs.Any(pin => pin.Path == relative && pin.Sha256 == Hash(authorityPath)), "Pilot review authority is not bound to the immutable candidate.");
        Authority authority = JsonSerializer.Deserialize<Authority>(File.ReadAllText(authorityPath), Json)!;
        foreach (PilotControl control in controls)
        {
            Definition definition = definitions.Single(item => item.Identity == control.DefinitionIdentity);
            string expected = control.Purpose == "knownSurvivor" ? "Survived" : "Killed";
            Require(control.ExpectedStatus == expected && definition.Status == expected && authority.Reviewers.Contains(control.Reviewer, StringComparer.Ordinal)
                && !string.IsNullOrWhiteSpace(control.Requirement) && control.Rationale.Trim().Length >= 20,
                "Pilot controls must identify actual reviewed product definitions and their native outcomes; positive kill counts alone are insufficient.");
            if (control.Purpose == "knownAsyncProductKill")
            {
                string source = Path.Combine(root, "workspace", CorePrefix, definition.Source);
                Require(Hash(source) == definition.SourceSha256, "The async control's actual shipping source changed.");
                ValidateAsyncPilotDefinition(definition, File.ReadAllText(source));
            }
        }
    }

    internal static void ValidateAsyncPilotDefinition(Definition definition, string source)
    {
        const string statement = "await actual.ConfigureAwait(false);";
        int offset = source.IndexOf(statement, StringComparison.Ordinal);
        Require(offset >= 0 && source.LastIndexOf(statement, StringComparison.Ordinal) == offset,
            "The fixed actual async settlement control changed; review the pilot recipe.");
        int line = 1 + source.AsSpan(0, offset).Count('\n');
        JsonObject location = JsonNode.Parse(definition.Location)!.AsObject();
        Require(definition.Source == "Healthcheck/Execution/HealthExecutionBinder.cs" && definition.Status == "Killed"
            && definition.Operator == "Statement mutation" && definition.Replacement == ";"
            && (int?)location["start"]?["line"] == line && (int?)location["end"]?["line"] == line,
            "The async pilot control must be the native killed removal of the actual awaited collection-settlement barrier, not a synchronous mutation or a timeout.");
    }
}
