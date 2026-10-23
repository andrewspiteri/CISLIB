using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CIS.Verification;

// Optional raw Linux strace evidence for a standard discovery host whose
// asynchronous VSTest exit callback was not retained. No execution host or
// mutation outcome is qualified here. Physical pins never imply loaded images.
internal sealed class HealthcheckMutationNativeKernelEvidence
{
    internal sealed record Pin(string Path, string Sha256, long Bytes);
    internal sealed record Executable(Pin File, string NativePath);
    private sealed record Edge(int Creator, int Child, bool Thread, decimal Started);
    private sealed record Exec(decimal Time, string File, string[] Arguments);
    private sealed record ExitGroup(decimal Time, int Code);
    private sealed record Trace(int Pid, Pin File, Exec[] Executions, ExitGroup[] ExitGroups, int? ExitCode, decimal? ExitTime);
    private static readonly Regex Record = new(@"^(?<time>[0-9]+(?:\.[0-9]+)?) (?<body>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Creation = new(@"^(?:clone3?\(|fork\(|vfork\(|<\.\.\. (?:clone3?|fork|vfork) resumed>).*\s=\s(?<pid>\d+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Exited = new(@"^\+\+\+ exited with (?<code>-?\d+) \+\+\+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ExitGrouping = new(@"^exit_group\((?<code>-?\d+)(?:\)(?:\s+=\s+(?:\?|0))?|(?<unfinished> <unfinished \.\.\.>))$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly Dictionary<int, Trace> traces;
    private readonly Edge[] edges;
    private readonly Executable dotnet;
    private readonly string physicalLogRoot;
    private readonly string nativeLogRoot;
    private readonly JsonObject? processProjectionOrigin;

    private HealthcheckMutationNativeKernelEvidence(Dictionary<int, Trace> traces, Edge[] edges, Executable dotnet, string physicalLogRoot, string nativeLogRoot, JsonObject? processProjectionOrigin = null)
    { this.traces = traces; this.edges = edges; this.dotnet = dotnet; this.physicalLogRoot = physicalLogRoot; this.nativeLogRoot = nativeLogRoot; this.processProjectionOrigin = processProjectionOrigin?.DeepClone().AsObject(); }

    internal static HealthcheckMutationNativeKernelEvidence ReadProjected(HealthcheckMutationNativeProcessProjection.Validation projection,
        Executable dotnet, string physicalLogRoot, string nativeLogRoot)
    {
        var pins = projection.Origins.ProcessFiles.Select(pin => new Pin(pin.Path, pin.Sha256, pin.Bytes)).ToArray();
        HealthcheckMutationNativeKernelEvidence read = Read(pins, dotnet, physicalLogRoot, nativeLogRoot);
        return new(read.traces, read.edges, read.dotnet, read.physicalLogRoot, read.nativeLogRoot, projection.Origins.Observation);
    }

    internal static HealthcheckMutationNativeKernelEvidence Read(IReadOnlyList<Pin> inputs, Executable dotnet, string physicalLogRoot, string nativeLogRoot)
    {
        Require(inputs.Count > 0 && nativeLogRoot.StartsWith('/') && dotnet.NativePath.StartsWith('/'), "Linux native trace/log/executable origins are required.");
        Require(!nativeLogRoot.Split('/').Any(part => part is "." or "..") && !dotnet.NativePath.Split('/').Any(part => part is "." or ".."), "Native origins cannot contain traversal components.");
        Check(dotnet.File); var traces = new Dictionary<int, Trace>(); var edges = new List<Edge>();
        foreach (Pin pin in inputs)
        {
            Check(pin); Require(pin.Bytes <= 64L * 1024 * 1024, "Native process trace exceeds supported size.");
            string name = System.IO.Path.GetFileName(pin.Path);
            Require(name.StartsWith("actual-process.", StringComparison.Ordinal), "Expected actual-process.<positive PID> trace name.");
            Require(int.TryParse(name["actual-process.".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && pid > 0,
                "Expected actual-process.<positive PID> trace name.");
            Require(!traces.ContainsKey(pid), "Duplicate kernel process identity.");
            var execs = new List<Exec>(); var exitGroups = new List<ExitGroup>(); int? exitCode = null; decimal? exitTime = null;
            string? pending = null; decimal pendingTime = 0, previous = -1; bool unfinishedExit = false;
            foreach (string line in File.ReadLines(pin.Path))
            {
                Require(line.Length <= 128 * 1024 && exitTime is null, "Oversized trace record or content after process terminal.");
                Match record = Record.Match(line); Require(record.Success, "Malformed timestamped kernel process record.");
                decimal time = decimal.Parse(record.Groups["time"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                Require(time >= previous, "Kernel records are out of order."); previous = time; string body = record.Groups["body"].Value;
                if (unfinishedExit)
                {
                    // strace can leave a nonreturning exit_group unfinished.
                    // Only its immediate process terminal is supported; it must
                    // not resume or conceal additional process activity.
                    Require(Exited.IsMatch(body) || body.StartsWith("+++ killed by ", StringComparison.Ordinal),
                        "An unfinished exit_group must be followed directly by its process terminal.");
                    unfinishedExit = false;
                }
                if (body.StartsWith("execve(", StringComparison.Ordinal))
                {
                    // Only successful, complete execs establish process identity.
                    if (body.EndsWith(" = 0", StringComparison.Ordinal)) { execs.Add(ParseExec(time, body)); }
                    continue;
                }
                if (Regex.IsMatch(body, @"^(?:clone3?|fork|vfork)\(.*<unfinished \.\.\.>$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                { Require(pending is null, "Overlapping unfinished process creation."); pending = body; pendingTime = time; continue; }
                Match created = Creation.Match(body);
                if (created.Success)
                {
                    bool resumed = body.StartsWith("<... ", StringComparison.Ordinal);
                    Require(resumed == (pending is not null), "Process creation resume is missing or ambiguous.");
                    string complete = pending is null ? body : pending + body;
                    edges.Add(new(pid, int.Parse(created.Groups["pid"].Value, CultureInfo.InvariantCulture), complete.Contains("CLONE_THREAD", StringComparison.Ordinal), resumed ? pendingTime : time)); pending = null; continue;
                }
                if (body.Contains(" resumed>", StringComparison.Ordinal)) { Require(pending is not null && body.Contains(" = -1 ", StringComparison.Ordinal), "Unsupported process creation resume."); pending = null; }
                if (body.StartsWith("exit_group(", StringComparison.Ordinal))
                {
                    Match exiting = ExitGrouping.Match(body); Require(exiting.Success, "Malformed exit_group observation.");
                    exitGroups.Add(new(time, int.Parse(exiting.Groups["code"].Value, CultureInfo.InvariantCulture)));
                    unfinishedExit = exiting.Groups["unfinished"].Success;
                }
                Match ended = Exited.Match(body);
                if (ended.Success) { exitCode = int.Parse(ended.Groups["code"].Value, CultureInfo.InvariantCulture); exitTime = time; }
                else if (body.StartsWith("+++ killed by ", StringComparison.Ordinal)) { exitTime = time; }
            }
            Require(pending is null && !unfinishedExit, "Unfinished kernel process creation or exit_group at EOF.");
            traces.Add(pid, new(pid, pin with { }, execs.ToArray(), exitGroups.ToArray(), exitCode, exitTime)); Check(pin);
        }
        Check(dotnet.File);
        return new(traces, edges.ToArray(), dotnet with { File = dotnet.File with { } }, System.IO.Path.GetFullPath(physicalLogRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar), nativeLogRoot.TrimEnd('/'));
    }

    internal bool TryConfirmDiscoveryHost(int hostPid, int controllerPid, string hostLogPath, string controllerLogPath,
        IReadOnlyCollection<string> sources, string runtimeLocation, out JsonObject? proof)
    {
        proof = null;
        try
        {
            Require(hostPid != controllerPid && sources.Count == 1, "Unique single-source discovery process traces are required.");
            Require(traces.TryGetValue(hostPid, out Trace? host), "Discovery host trace is missing.");
            Require(traces.TryGetValue(controllerPid, out Trace? controller), "Discovery controller trace is missing.");
            string nativeHostLog = NativeLog(hostLogPath), nativeControllerLog = NativeLog(controllerLogPath);
            Require(dotnet.NativePath.EndsWith("/dotnet", StringComparison.Ordinal) && runtimeLocation == dotnet.NativePath[..^"/dotnet".Length] + "/shared/Microsoft.NETCore.App/10.0.12", "Kernel executable differs from observed subject runtime origin.");
            Require(host.Executions.Length == 1 && controller.Executions.Length == 1, "Host/controller successful exec identity is ambiguous or reused.");
            Exec hostExec = host.Executions[0], controllerExec = controller.Executions[0]; string source = sources.Single().Replace('\\', '/');
            Require(source.StartsWith('/') && source.EndsWith(".dll", StringComparison.Ordinal), "Linux requested source DLL origin is required.");
            string directory = source[..source.LastIndexOf('/')], stem = source[..^4]; string[] argv = hostExec.Arguments;
            Require(hostExec.File == dotnet.NativePath && argv.Length >= 7 && argv[0] == dotnet.NativePath && argv[1] == "exec"
                && argv[2] == "--runtimeconfig" && argv[3] == stem + ".runtimeconfig.json" && argv[4] == "--depsfile" && argv[5] == stem + ".deps.json" && argv[6] == directory + "/testhost.dll",
                "Kernel discovery host exec does not bind the requested project/config/deps.");
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 7; index < argv.Length; index += 2)
            { Require(index + 1 < argv.Length && new[] { "--port", "--endpoint", "--role", "--parentprocessid", "--diag", "--tracelevel", "--telemetryoptedin" }.Contains(argv[index], StringComparer.Ordinal) && arguments.TryAdd(argv[index], argv[index + 1]), "Unsupported/duplicate host exec argument."); }
            Require(arguments.GetValueOrDefault("--parentprocessid") == controllerPid.ToString(CultureInfo.InvariantCulture) && arguments.GetValueOrDefault("--diag") == nativeHostLog && arguments.GetValueOrDefault("--role") == "client", "Kernel host parent/diagnostic origin differs.");
            string[] startup = File.ReadLines(hostLogPath).Where(line => line.Contains("Testhost process started with args :", StringComparison.Ordinal)).ToArray();
            Require(startup.Length == 1 && startup[0].Contains("[--parentprocessid, " + controllerPid + "]", StringComparison.Ordinal) && startup[0].Contains("[--diag, " + nativeHostLog + "]", StringComparison.Ordinal), "Retained host startup does not bind kernel parent/diag arguments.");
            Require(controllerExec.File == dotnet.NativePath && controllerExec.Arguments.Length >= 2 && controllerExec.Arguments[0] == dotnet.NativePath
                && controllerExec.Arguments[1].EndsWith("/vstest.console.dll", StringComparison.Ordinal)
                && controllerExec.Arguments.Count(arg => arg == "/diag:" + nativeControllerLog + ";tracelevel=Verbose") == 1,
                "Kernel controller exec does not bind the actual controller diagnostic.");
            Edge[] creators = edges.Where(edge => edge.Child == hostPid && !edge.Thread).ToArray(); Require(creators.Length == 1 && creators[0].Started <= hostExec.Time, "Kernel host process creation is missing/ambiguous or late.");
            int creator = creators[0].Creator; var used = new HashSet<int> { hostPid }; var chain = new JsonArray();
            while (creator != controllerPid)
            {
                Require(used.Add(creator) && traces.ContainsKey(creator), "Kernel creator ancestry is cyclic or missing.");
                Edge[] parent = edges.Where(edge => edge.Child == creator).ToArray();
                Require(parent.Length == 1 && parent[0].Thread && parent[0].Started <= creators[0].Started, "Discovery creator does not belong to its declared controller thread group.");
                chain.Add(new JsonObject { ["threadPid"] = creator, ["creatorPid"] = parent[0].Creator }); creator = parent[0].Creator;
            }
            decimal hostClosed = GroupExit(hostPid, out int[] hostGroup), controllerClosed = GroupExit(controllerPid, out int[] controllerGroup);
            Require(controllerExec.Time < hostExec.Time && hostExec.Time < hostClosed && hostClosed < controllerClosed, "Kernel discovery process start/exit chronology differs.");
            int[] evidencePids = hostGroup.Concat(controllerGroup).Concat(chain.OfType<JsonObject>().Select(row => (int)row["threadPid"]!)).Distinct().Order().ToArray();
            foreach (int pid in evidencePids) { Check(traces[pid].File); }
            Check(dotnet.File);
            proof = new JsonObject
            {
                ["kind"] = "NativeKernelDiscoveryExit",
                ["hostPid"] = hostPid,
                ["controllerPid"] = controllerPid,
                ["creatorThreadPid"] = creators[0].Creator,
                ["threadAncestry"] = chain,
                ["nativeHostDiagnostic"] = nativeHostLog,
                ["nativeControllerDiagnostic"] = nativeControllerLog,
                ["requestedSourceDll"] = source,
                ["hostExecArguments"] = JsonSerializer.SerializeToNode(argv),
                ["hostNaturalExitUtcEpoch"] = hostClosed,
                ["controllerNaturalExitUtcEpoch"] = controllerClosed,
                ["dotnetPhysicalPin"] = PinJson(dotnet.File),
                ["nativeDotnetPath"] = dotnet.NativePath,
                ["tracePins"] = new JsonArray(evidencePids.Select(pid => (JsonNode)PinJson(traces[pid].File)).ToArray()),
                ["callbackFabricated"] = false,
                ["executionOutcomeCredit"] = 0,
                ["loadedImageClaim"] = false
            };
            if (processProjectionOrigin is not null)
            {
                proof["processEvidenceProfile"] = "verified-derived-process-origin";
                proof["processOriginVerification"] = processProjectionOrigin.DeepClone();
            }
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or OverflowException or ArgumentException)
        { return false; }
    }

    private decimal GroupExit(int leader, out int[] members)
    {
        Require(!edges.Any(edge => edge.Child == leader && edge.Thread), "A declared process leader cannot also be an observed thread.");
        var group = new HashSet<int> { leader }; bool changed;
        do { changed = false; foreach (Edge edge in edges.Where(edge => edge.Thread && group.Contains(edge.Creator))) { if (group.Add(edge.Child)) { changed = true; } } } while (changed);
        Require(group.All(pid => traces.TryGetValue(pid, out Trace? trace) && trace.ExitCode == 0 && trace.ExitTime.HasValue), "Discovery process thread group lacks complete natural exit0.");
        foreach (int pid in group.Where(pid => pid != leader))
        {
            Edge[] incoming = edges.Where(edge => edge.Child == pid).ToArray();
            Require(incoming.Length == 1 && incoming[0].Thread && group.Contains(incoming[0].Creator) && incoming[0].Creator != pid && incoming[0].Started < traces[pid].ExitTime, "Kernel thread ownership is ambiguous, reused or out of order.");
        }
        ExitGroup[] exits = group.SelectMany(pid => traces[pid].ExitGroups).ToArray();
        Require(exits.Length == 1 && exits[0].Code == 0 && exits[0].Time <= group.Max(pid => traces[pid].ExitTime), "Discovery process has no unique actual exit_group0.");
        members = group.Order().ToArray(); return members.Max(pid => traces[pid].ExitTime!.Value);
    }

    private string NativeLog(string path)
    {
        string full = System.IO.Path.GetFullPath(path); string relative = System.IO.Path.GetRelativePath(physicalLogRoot, full);
        Require(relative.Length > 0 && relative == System.IO.Path.GetFileName(relative) && relative is not ("." or ".."), "Kernel proof log is outside the explicitly mapped physical log root.");
        return nativeLogRoot + "/" + relative;
    }

    private static Exec ParseExec(decimal time, string body)
    {
        int index = "execve(".Length; string file = Quoted(body, ref index); Require(body.AsSpan(index).StartsWith(", [", StringComparison.Ordinal), "Unsupported kernel exec argument array."); index += 3;
        var argv = new List<string>();
        while (index < body.Length && body[index] != ']')
        { argv.Add(Quoted(body, ref index)); Require(index < body.Length, "Truncated kernel argv."); if (body[index] == ']') { break; } Require(body.AsSpan(index).StartsWith(", ", StringComparison.Ordinal), "Truncated/unsupported kernel argv."); index += 2; }
        Require(index < body.Length && body[index] == ']' && argv.Count > 0 && body[(index + 1)..].StartsWith(", ", StringComparison.Ordinal), "Missing complete kernel exec environment boundary."); return new(time, file, argv.ToArray());
    }

    private static string Quoted(string text, ref int index)
    {
        Require(index < text.Length && text[index] == '"', "Kernel exec requires a quoted complete argument."); int start = index++; bool escaped = false;
        while (index < text.Length)
        { char value = text[index++]; if (!escaped && value == '"') { return JsonSerializer.Deserialize<string>(text[start..index]) ?? throw new InvalidDataException("Invalid kernel string."); } if (!escaped && value == '\\') { escaped = true; } else { escaped = false; } }
        throw new InvalidDataException("Unterminated kernel exec string.");
    }

    private static void Check(Pin pin)
    {
        Require(pin.Bytes >= 0 && pin.Sha256.Length == 64 && pin.Sha256.All(value => char.IsAsciiDigit(value) || value is >= 'a' and <= 'f'), "Malformed kernel physical input pin.");
        using FileStream file = File.OpenRead(pin.Path); Require(file.Length == pin.Bytes && Convert.ToHexStringLower(SHA256.HashData(file)) == pin.Sha256, "Missing/changed kernel physical input.");
    }
    private static JsonObject PinJson(Pin pin) => new() { ["path"] = pin.Path, ["sha256"] = pin.Sha256, ["bytes"] = pin.Bytes };
    private static void Require([DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
