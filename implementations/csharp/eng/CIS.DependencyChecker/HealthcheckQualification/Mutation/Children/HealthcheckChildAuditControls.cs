using System.Globalization;
using System.Text.Json;
using static CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Verification;

// Derivative parser controls only: no child is launched and no native mutation outcome is created.
internal static class HealthcheckChildAuditControls
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try { return await Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task<int> Run(string[] args)
    {
        Require(args.Length == 4, "Expected --session <actual-v4-session.json> --output <empty-root>.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            Require(args[index] is "--session" or "--output" && options.TryAdd(args[index], args[index + 1]), "Unknown or repeated audit-control option.");
        }
        Require(options.Count == 2, "Both session and output are required.");
        string sessionPath = Path.GetFullPath(options["--session"]), output = Path.GetFullPath(options["--output"]);
        Session session = Read<Session>(sessionPath);
        Require(session.Schema == Schema && session.CoverageAnalysisOff, "Audit controls require an actual current v4 child session.");
        Require(Same(sessionPath, Path.Combine(session.OutputRoot, "session.json")), "The actual session path differs from its retained root.");
        foreach (string immutableRoot in new[] { session.OutputRoot, session.LanguageRoot })
        {
            Require(!Same(immutableRoot, output) && !Under(immutableRoot, output) && !Under(output, immutableRoot), "Keep controls separate from actual source and evidence.");
        }
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Audit controls require an empty output root.");
        Directory.CreateDirectory(output);

        string[] directories = Directory.GetDirectories(Path.Combine(session.OutputRoot, "launches"));
        Require(directories.Length is > 0 and <= 200000, "No bounded actual child launch inventory exists.");
        string? selectedDirectory = null;
        Launch? selected = null;
        foreach (string directory in directories.Order(StringComparer.Ordinal))
        {
            string[] names = Directory.GetFiles(directory).Select(Path.GetFileName).Select(name => name!).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(new[] { "exit.json", "launch.json", "parent-exit.json", "receipt.json" })) { continue; }
            Launch candidate = Read<Launch>(Path.Combine(directory, "launch.json"));
            if (candidate.Schema == Schema && candidate.Instrumented && candidate.ActiveMutant >= 0)
            { selectedDirectory = directory; selected = candidate; break; }
        }
        Require(selected is not null && selectedDirectory is not null, "An actual selected instrumented launch with normal completion is required.");
        Launch launch = selected!;
        string originalLaunchDirectory = selectedDirectory!;
        Require(launch.Id == Path.GetFileName(originalLaunchDirectory)
            && Under(Path.Combine(session.OutputRoot, "stages", launch.ParentPid.ToString(CultureInfo.InvariantCulture)), launch.Stage.Directory),
            "The selected original launch escaped its actual session stage.");
        string kind = launch.Stage.AssemblyFile switch
        {
            "CIS.Healthcheck.HttpFixture.dll" => "http-host",
            "CisHealthProbe.dll" => "probe",
            _ => throw new InvalidOperationException("Unknown real child executable in audit controls.")
        };
        VerifyFiles(launch.Stage.Directory, launch.Stage.DeployedFiles);
        long bytes = launch.Stage.DeployedFiles.Sum(pin => new FileInfo(Input(launch.Stage.Directory, pin.Path)).Length);
        Require(bytes <= 256L * 1024 * 1024, "The copied child stage exceeds the finite audit-control size bound.");
        Receipt origin = Read<Receipt>(Path.Combine(originalLaunchDirectory, "receipt.json"));
        ParentExitReceipt parent = Read<ParentExitReceipt>(Path.Combine(originalLaunchDirectory, "parent-exit.json"));
        ExitReceipt exit = Read<ExitReceipt>(Path.Combine(originalLaunchDirectory, "exit.json"));
        var originals = new Dictionary<string, string>(StringComparer.Ordinal) { [sessionPath] = Hash(sessionPath) };
        foreach (string path in Directory.GetFiles(originalLaunchDirectory)) { originals.Add(path, Hash(path)); }
        foreach (FilePin pin in launch.Stage.DeployedFiles)
        { string path = Input(launch.Stage.Directory, pin.Path); originals.Add(path, Hash(path)); }

        var rows = new List<object>();
        var failures = new List<string>();
        try
        {
            await Control("positive", null, null);
            await Control("missing-parent", "Missing terminal/parent receipt, forced stop, timeout, or lifecycle guard failure:",
                copy => File.Delete(Path.Combine(copy.LaunchDirectory, "parent-exit.json")));
            await Control("missing-terminal", "Missing terminal/parent receipt, forced stop, timeout, or lifecycle guard failure:",
                copy => File.Delete(Path.Combine(copy.LaunchDirectory, "exit.json")));
            await Control("exit-disagreement", "The child normal-exit receipt and actual parent exit disagree.",
                copy => Replace(Path.Combine(copy.LaunchDirectory, "parent-exit.json"), copy.Parent with { ExitCode = copy.Parent.ExitCode == 0 ? 1 : 0 }));
            await Control("forced-stop", "Invalid actual child process lifecycle receipt.",
                copy => Replace(Path.Combine(copy.LaunchDirectory, "parent-exit.json"), copy.Parent with { ForcedStop = true }));
            await Control("timed-out", "Invalid actual child process lifecycle receipt.",
                copy => Replace(Path.Combine(copy.LaunchDirectory, "parent-exit.json"), copy.Parent with { TimedOut = true }));
            await Control("timeout-marker", "Missing terminal/parent receipt, forced stop, timeout, or lifecycle guard failure:",
                copy => Write(Path.Combine(copy.LaunchDirectory, "timeout.json"), new { derivativeParserControl = true }));
            await Control("changed-core", "Child input changed: CIS.Core.dll", copy =>
            {
                using var stream = new FileStream(Path.Combine(copy.StageDirectory, "CIS.Core.dll"), FileMode.Append, FileAccess.Write);
                stream.WriteByte(0); // Keep the PE and MVID readable while violating the retained exact file hash.
            });
            await Control("owner-binding", "A child stage does not match its exact parent owner/Core/executable identity.", copy =>
            {
                string path = Path.Combine(copy.LaunchDirectory, "launch.json");
                Launch copiedLaunch = Read<Launch>(path);
                string differentOwner;
                do { differentOwner = Guid.NewGuid().ToString("N"); } while (differentOwner == copiedLaunch.StageOwnerId);
                Replace(path, copiedLaunch with { StageOwnerId = differentOwner });
                string reboundHash = Hash(path);
                string originPath = Path.Combine(copy.LaunchDirectory, "receipt.json");
                Replace(originPath, Read<Receipt>(originPath) with { LaunchSha256 = reboundHash });
                Replace(Path.Combine(copy.LaunchDirectory, "parent-exit.json"), copy.Parent with { LaunchSha256 = reboundHash });
            });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            foreach ((string path, string hash) in originals)
            {
                try { Require(Hash(path) == hash, "Original native evidence changed: " + path); }
                catch (Exception error) { failures.Add(error.Message); }
            }
            Write(Path.Combine(output, "results.json"), new
            {
                schema = "cis.healthcheck.child-audit-controls/v2-development",
                childProtocol = Schema,
                scope = "Rebased copies of one actual selected normal child launch; parser rejection controls only, no process execution or mutation/release credit.",
                nativeExecution = false,
                status = failures.Count == 0 && rows.Count == 9 ? "AuditControlsPassed" : "AuditControlsFailed",
                exitCode = failures.Count == 0 && rows.Count == 9 ? 0 : 1,
                originalSessionPath = sessionPath,
                originalSessionSha256 = originals[sessionPath],
                originalLaunchDirectory,
                launch.Id,
                launch.StageOwnerId,
                launch.ActiveMutant,
                kind,
                originalEvidence = originals.Select(item => new { path = item.Key, sha256 = item.Value }).ToArray(),
                rows,
                failures
            });
        }
        return failures.Count == 0 && rows.Count == 9 ? 0 : 1;

        async Task Control(string name, string? expectedFailure, Action<Copy>? mutate)
        {
            string caseRoot = Path.Combine(output, name), copyRoot = Path.Combine(caseRoot, "derived-session");
            Copy copy = Clone(copyRoot);
            mutate?.Invoke(copy);
            string auditRoot = Path.Combine(caseRoot, "audit");
            string[] auditArgs = ["audit", "--session", copy.SessionPath, "--output", auditRoot,
                "--require-kind", kind, "--require-active", launch.ActiveMutant.ToString(CultureInfo.InvariantCulture)];
            int code = await HealthcheckChildMutationVerification.RunAsync(auditArgs);
            string resultPath = Path.Combine(auditRoot, "results.json");
            using JsonDocument result = JsonDocument.Parse(File.ReadAllText(resultPath));
            string[] rejected = result.RootElement.GetProperty("failures").EnumerateArray().Select(item => item.GetString()!).ToArray();
            string status = result.RootElement.GetProperty("status").GetString()!;
            bool passed = expectedFailure is null
                ? code == 0 && status == "QualifiedChildPropagation" && rejected.Length == 0
                    && result.RootElement.GetProperty("launches").GetInt32() == 1
                    && result.RootElement.GetProperty("selectedLaunches").GetInt32() == 1
                : code == 1 && status == "InvalidChildEvidence" && rejected.Any(reason => reason.StartsWith(expectedFailure, StringComparison.Ordinal))
                    && rejected.All(reason => reason.StartsWith(expectedFailure, StringComparison.Ordinal) || reason == "No actual child loaded an instrumented Core.");
            rows.Add(new
            {
                name,
                derivativeParserControl = true,
                expectedFailure,
                passed,
                auditArgs,
                actualAuditExitCode = code,
                status,
                failures = rejected,
                resultPath,
                resultsSha256 = Hash(resultPath),
                copiedSessionSha256 = Hash(copy.SessionPath)
            });
            Write(Path.Combine(caseRoot, "invocation.json"), new { auditArgs, actualAuditExitCode = code, nativeExecution = false, passed });
            Require(passed, "The derivative audit control did not reach its expected result: " + name);
        }

        Copy Clone(string root)
        {
            string copiedSessionPath = Path.Combine(root, "session.json");
            Write(copiedSessionPath, session with { OutputRoot = root });
            string stageDirectory = Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(session.OutputRoot, launch.Stage.Directory)));
            Require(Under(Path.Combine(root, "stages"), stageDirectory), "Copied stage escaped its derivative root.");
            foreach (FilePin pin in launch.Stage.DeployedFiles)
            {
                string destination = Path.GetFullPath(Path.Combine(stageDirectory, pin.Path));
                Require(Under(stageDirectory, destination), "Copied dependency escaped its private stage.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Input(launch.Stage.Directory, pin.Path), destination, overwrite: false);
            }
            VerifyFiles(stageDirectory, launch.Stage.DeployedFiles);
            string launchDirectory = Path.Combine(root, "launches", launch.Id), copiedLaunchPath = Path.Combine(launchDirectory, "launch.json");
            Write(copiedLaunchPath, launch with
            {
                SessionPath = copiedSessionPath,
                SessionSha256 = Hash(copiedSessionPath),
                Stage = launch.Stage with { Directory = stageDirectory }
            });
            string copiedLaunchHash = Hash(copiedLaunchPath);
            Write(Path.Combine(launchDirectory, "receipt.json"), origin with
            {
                LaunchSha256 = copiedLaunchHash,
                EntryAssemblyPath = Rebase(origin.EntryAssemblyPath),
                CorePath = Rebase(origin.CorePath),
                Arguments = origin.Arguments.Select(Rebase).ToArray()
            });
            ParentExitReceipt copiedParent = parent with
            {
                LaunchSha256 = copiedLaunchHash,
                WorkingDirectory = Rebase(parent.WorkingDirectory),
                Arguments = parent.Arguments.Select(Rebase).ToArray()
            };
            Write(Path.Combine(launchDirectory, "parent-exit.json"), copiedParent);
            Write(Path.Combine(launchDirectory, "exit.json"), exit);
            return new Copy(copiedSessionPath, launchDirectory, stageDirectory, copiedParent);

            string Rebase(string value) => !Path.IsPathRooted(value) ? value : Same(value, launch.Stage.Directory) ? stageDirectory
                : Under(launch.Stage.Directory, value)
                    ? Path.GetFullPath(Path.Combine(stageDirectory, Path.GetRelativePath(launch.Stage.Directory, value))) : value;
        }
    }

    private static bool Same(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);
    private static void Replace<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");
    private sealed record Copy(string SessionPath, string LaunchDirectory, string StageDirectory, ParentExitReceipt Parent);
}
