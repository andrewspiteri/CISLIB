using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CIS.Verification;

namespace PrivateCapture72;

internal static class Entry
{
private static void Main(string[] args)
{
const string task = "/data/cis-healthcheck-20261021-01a15a72";
if (args.Length == 1 && args[0] == "controls")
{
    JsonArray controls = ProjectionControls.Run(); Boundary.Controls();
    Console.WriteLine(JsonSerializer.Serialize(new { scope = "synthetic shared-parser controls only", processControls = controls.Count, boundaryControls = 5, passed = true, actual55Read = false })); return;
}
bool pilot = args.Length == 1 && args[0] == "closed61";
Need(OperatingSystem.IsLinux() && (pilot || args.Length == 4 && args[0] == "closed55"), "Use controls, Linux closed61, or reviewed Linux closed55 <resultsSHA> <sinkSHA> <captureSHA>.");
Need(pilot || System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12", "Actual projection runtime10.0.12 required.");
string source = task + (pilot ? "/unit-wire-pilot61" : "/unit-full-nomix55");
string output = task + (pilot ? "/unit-wire-pilot61-process-origin72" : "/unit-full-nomix55-kernel-projection72");
string native = task + (pilot ? "/unit-wire-pilot61" : "/unit-full-nomix55");
string resultHash = pilot ? "30be9c35c0c30841cc789db7386c7b7c07fe6de257101144899fa1997c2198dc" : args[1];
string sinkHash = pilot ? "120b7a32c6f19970cc820c578736b12fcabffff7678e979fade0f2ff49b82337" : args[2];
string captureHash = pilot ? "a29bd4ec30c68e6c1c4ad8ec9d532c81b7e3cd5fd5ecfc3712ee9b4ae1430282" : args[3];
Need(!Directory.Exists(output) && !File.Exists(output) && new DriveInfo(Path.GetPathRoot(Path.GetFullPath(output))!).AvailableFreeSpace >= 3_000_000_000, "New output and3GB actual starting free required.");
NoLinks(source); NoLinks(Path.GetDirectoryName(output)!);
string resultsPath = Path.Combine(source, "results.json"), sinkPath = Path.Combine(source, "wire/sink-result.json"), capturePath = Path.Combine(source, "wire/capture-process.json");
NoLinks(resultsPath); NoLinks(sinkPath); NoLinks(capturePath); NoLinks(Path.Combine(source, "source-inputs.json"));
Need(Hash(resultsPath) == resultHash && Hash(sinkPath) == sinkHash && Hash(capturePath) == captureHash, "Exact supplied closed producer pins differ.");
JsonObject campaign = Read(resultsPath), sink = Read(sinkPath), capture = Read(capturePath);
Need((bool?)campaign["completeExecution"] == true && campaign["failure"] is null && (bool?)campaign["complete41SourceScope"] == !pilot && (int?)campaign["nativeProcess"]?["actualExitCode"] is 0 or 2, "Closed successful producer/native process is required; partial capture is not admitted.");
Need((bool?)sink["complete"] == true && (bool?)sink["observedEof"] == true && sink["failure"] is null && (string?)sink["syntheticFinalizationFault"] == "None" && (long?)sink["rawPrefixBytesPersisted"] == (long?)sink["rawBytesReceived"], "Whole persisted EOF primary is required.");
Need((int?)capture["actualExitCode"] == 0 && (bool?)capture["sinkForcedAfterTracerClosed"] == false && (bool?)capture["failureFlagExists"] == false && (bool?)capture["toolsUnchanged"] == true && (string?)capture["started"]?["controllerAssemblySha256"] == (pilot ? "02e598bb2e6c964446f6b4aa1f6bb04b88a0bdfab777ff7e5a3c8340caffb5d6" : "275a91548d724ea3231723950617ca10762400bddeb93ef0bc35f4919a1a0908"), "Actual reviewed producer/sink closure differs.");
Need((string?)capture["started"]?["fifo"] == native + "/wire/trace.fifo" && (string?)capture["started"]?["dotnet"] == task + "/dotnet/dotnet" && (string?)capture["started"]?["dotnetSha256"] == "01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269" && (string?)capture["started"]?["straceSha256"] == "28f957c227012de0b18d1bd7fff2d396cb693ea60ed8013be68de071e84b5001", "Captured physical tools or owned FIFO origin differs.");
Need(Hash(Path.Combine(source, "source-inputs.json")) == (pilot ? "5629c0e50e917e32d83e85450de53b970c0e0fa983253d24001e2cc80d46e692" : "df5c9e7d2dc4d87605242fe914648eb9251ada1975c55dcad46d76e5d0b54ccf"), "Fixed reviewed source manifest differs.");
JsonObject[] members = sink["members"]!.AsArray().OfType<JsonObject>().ToArray();
Need(members.Length is > 0 and <= 768 && (long?)sink["rawBytesReceived"] is > 0 and <= 100_000_000_000, "Explicit source size/member ceiling exceeded.");
Need(Directory.EnumerateFiles(Path.Combine(source, "wire"), "trace-*.strace.gz").Order(StringComparer.Ordinal).SequenceEqual(members.Select(x => Path.Combine(source, "wire", Path.GetFileName((string)x["path"]!))).Order(StringComparer.Ordinal)), "Primary compressed member inventory has unlisted or missing files.");
Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(output, "process-view"));
var clock = Stopwatch.StartNew(); long lastGuard = -2000; var selected = new Dictionary<int, List<ProcessView.Source>>(); var origins = new Dictionary<(int, long), Origin>();
long total = 0, count = 0, largest = 0;
Scan(false); // All primary bytes and member boundaries verified before deriving any kernel input.
var processPins = new List<HealthcheckMutationNativeKernelEvidence.Pin>();
using var expectedIndex = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
using (var index = new StreamWriter(new FileStream(Path.Combine(output, "process-origins.jsonl"), FileMode.CreateNew), new UTF8Encoding(false)) { NewLine = "\n" })
foreach ((int pid, List<ProcessView.Source> rows) in selected.OrderBy(x => x.Key))
{
    string path = Path.Combine(output, "process-view/actual-process." + pid);
    using (var file = new FileStream(path, FileMode.CreateNew))
    {
        foreach (ProcessView.Line line in ProcessView.Derive(rows.ToArray()))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line.Text + "\n"); long at = file.Position; file.Write(bytes);
            string indexLine = JsonSerializer.Serialize(new { pid, derivedStart = at, derivedBytes = bytes.Length, derivedSha256 = Sha(bytes), line.Rule, origins = line.Sources.Select(x => origins[(pid, x.Offset)]) });
            index.WriteLine(indexLine); expectedIndex.AppendData(Encoding.UTF8.GetBytes(indexLine + "\n"));
        }
        file.Flush(true);
    }
    processPins.Add(new(path, Hash(path), new FileInfo(path).Length)); Guard();
}
Scan(true); // Independent full-source regeneration compares every process-view byte before Kernel.Read.
string indexSha = Convert.ToHexStringLower(expectedIndex.GetHashAndReset()); Need(Hash(Path.Combine(output, "process-origins.jsonl")) == indexSha, "Actual origin index differs from verified source mapping.");
string dotnet = Path.Combine(source, "tool-origins/dotnet"); NoLinks(dotnet); Need(Hash(dotnet) == "01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269", "Retained actual dotnet physical body differs.");
var projectionInput = new HealthcheckMutationNativeProcessProjection.Input(source, native,
    ProjectionPin(resultsPath), ProjectionPin(sinkPath), ProjectionPin(capturePath), ProjectionPin(Path.Combine(source, "source-inputs.json")),
    ProjectionPin(Path.Combine(output, "process-origins.jsonl")),
    members.Select(member => ProjectionPin(Path.Combine(source, "wire", Path.GetFileName((string)member["path"]!)))).ToArray());
var originValidation = HealthcheckMutationNativeProcessProjection.Read(projectionInput,
    processPins.Select(pin => new HealthcheckMutationNativeProcessProjection.Pin(pin.Path, pin.Sha256, pin.Bytes)).ToArray());
Write(Path.Combine(output, "origin-validation.json"), originValidation.Origins.Observation);
var kernel = HealthcheckMutationNativeKernelEvidence.ReadProjected(originValidation, new(new(dotnet, Hash(dotnet), new FileInfo(dotnet).Length), task + "/dotnet/dotnet"), Path.Combine(source, "native/logs"), native + "/native/logs");
var observations = new JsonArray();
foreach (string hostLog in Directory.EnumerateFiles(Path.Combine(source, "native/logs"), "*Discoverer*-log.host.*.txt"))
{
    string[] head = File.ReadLines(hostLog).Take(20).ToArray(); Match pid = Regex.Match(head[0], @": 0 : (\d+),", RegexOptions.CultureInvariant);
    Match parent = Regex.Match(string.Join('\n', head), @"\[--parentprocessid, (\d+)\]", RegexOptions.CultureInvariant);
    Need(pid.Success && parent.Success, "Actual discovery host header has no unique PID/parent.");
    string parentLog = Path.Combine(Path.GetDirectoryName(hostLog)!, Path.GetFileName(hostLog).Split(".host.", StringSplitOptions.None)[0] + ".txt");
    bool proof = kernel.TryConfirmDiscoveryHost(int.Parse(pid.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(parent.Groups[1].Value, CultureInfo.InvariantCulture), hostLog, parentLog, [native + "/source/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll"], task + "/dotnet/shared/Microsoft.NETCore.App/10.0.12", out JsonObject? actual);
    observations.Add(new JsonObject { ["hostLog"] = Pin(hostLog), ["controllerLog"] = Pin(parentLog), ["kernelDiscoveryConfirmed"] = proof, ["actualProof"] = actual });
}
Need(observations.Count > 0, "No actual discovery-host observation was available.");
JsonArray wrapperControls = WrapperControls();
Boundary.Controls(); JsonArray control = ProjectionControls.Run();
Need(Hash(resultsPath) == resultHash && Hash(sinkPath) == sinkHash && Hash(capturePath) == captureHash, "Closed producer receipts changed during projection."); Guard();
Write(Path.Combine(output, "descriptor.json"), new JsonObject
{
    ["schema"] = "cis.healthcheck.unit55-compact-process-projection/v1-private-proposal", ["profile"] = pilot ? "closed61-parser-control-only" : "fixed-closed-current-unit55",
    ["completeProjection"] = true, ["sourceRoot"] = source, ["sourceResults"] = Pin(resultsPath), ["sourceSink"] = Pin(sinkPath), ["sourceCapture"] = Pin(capturePath),
    ["rawBytesVerifiedEachPass"] = total, ["rawRowsVerifiedEachPass"] = count, ["maximumRawRecordBytes"] = largest, ["sourceMembers"] = JsonSerializer.SerializeToNode(members),
    ["processFiles"] = JsonSerializer.SerializeToNode(processPins), ["processOrigins"] = Pin(Path.Combine(output, "process-origins.jsonl")), ["fullSourceRegenerationPasses"] = 2,
    ["discoveryObservations"] = observations, ["processControls"] = control.Count, ["boundaryControls"] = 5, ["elapsedSeconds"] = clock.Elapsed.TotalSeconds,
    ["actualUnionWrapperControls"] = wrapperControls,
    ["bounds"] = "15min,1GB logical new-output ceiling,2GB actual volume reserve;3GB free before start. No primary/fullperPID duplicate created.",
    ["notLegacyRawCapture"] = true, ["wholeIpcClaim"] = false, ["normalExecutionHostClaim"] = false, ["qualificationCredit"] = 0, ["producerOrPrimaryChanged"] = false
});
Console.WriteLine(JsonSerializer.Serialize(new { descriptor = Path.Combine(output, "descriptor.json"), sha256 = Hash(Path.Combine(output, "descriptor.json")), processFiles = processPins.Count, total, count, elapsedSeconds = clock.Elapsed.TotalSeconds }));

JsonArray WrapperControls()
{
    string logInventoryPath = Path.Combine(output, "wrapper-log-root.json");
    Write(logInventoryPath, new JsonObject { ["root"] = Path.Combine(source, "native/logs"), ["scope"] = "ReadKernelInputs primitive only; no ReadLogs or union aggregation claimed." });
    var inventory = new JsonObject
    {
        ["schema"] = "cis.healthcheck.native-kernel-inventory/v2-process-projection", ["root"] = Path.Combine(output, "process-view"),
        ["nativeLogRoot"] = native + "/native/logs", ["dotnet"] = new JsonObject { ["file"] = Pin(dotnet), ["nativePath"] = task + "/dotnet/dotnet" },
        ["files"] = new JsonArray(processPins.Select(pin => (JsonNode)new JsonObject { ["path"] = Path.GetFileName(pin.Path), ["sha256"] = pin.Sha256, ["bytes"] = pin.Bytes }).ToArray()),
        ["projection"] = new JsonObject { ["sourceRoot"] = source, ["nativeRoot"] = native, ["results"] = Pin(resultsPath), ["sink"] = Pin(sinkPath),
            ["capture"] = Pin(capturePath), ["sourceManifest"] = Pin(Path.Combine(source, "source-inputs.json")), ["origins"] = Pin(Path.Combine(output, "process-origins.jsonl")) }
    };
    var result = new JsonArray();
    var wrapped = InvokeWrapper(inventory, "positive", "ubuntu");
    foreach (JsonObject item in observations.OfType<JsonObject>())
    {
        JsonObject original = item["actualProof"]?.AsObject() ?? throw new InvalidDataException("Actual discovery proof did not close.");
        bool valid = wrapped.TryConfirmDiscoveryHost((int)original["hostPid"]!, (int)original["controllerPid"]!,
            (string)item["hostLog"]!["path"]!, (string)item["controllerLog"]!["path"]!,
            [(string)original["requestedSourceDll"]!], task + "/dotnet/shared/Microsoft.NETCore.App/10.0.12", out JsonObject? proof);
        Need(valid && (string?)proof?["processEvidenceProfile"] == "verified-derived-process-origin" && proof?["processOriginVerification"] is JsonObject,
            "Actual v2 wrapper discovery proof lacks its verified origin.");
        result.Add(new JsonObject { ["name"] = "actual-v2-wrapper-closed61-discovery", ["passed"] = true, ["proof"] = proof });
    }
    foreach (string name in new[] { "unknown-field", "projection-approval", "mixed-native-root", "wrong-physical-root", "mixed-results-role", "mixed-origin-role", "changed-process-pin", "downgrade-v1", "missing-projection", "wrong-lane" })
    {
        JsonObject changed = inventory.DeepClone().AsObject();
        switch (name)
        {
            case "unknown-field": changed["approved"] = true; break;
            case "projection-approval": changed["projection"]!["approved"] = true; break;
            case "mixed-native-root": changed["projection"]!["nativeRoot"] = native + "-other"; break;
            case "wrong-physical-root": changed["projection"]!["sourceRoot"] = output; break;
            case "mixed-results-role": changed["projection"]!["results"] = Pin(capturePath); break;
            case "mixed-origin-role": changed["projection"]!["origins"] = Pin(resultsPath); break;
            case "changed-process-pin": changed["files"]![0]!["sha256"] = new string('0', 64); break;
            case "downgrade-v1": changed["schema"] = "cis.healthcheck.native-kernel-inventory/v1-development"; break;
            case "missing-projection": changed.Remove("projection"); break;
        }
        try { InvokeWrapper(changed, name, name == "wrong-lane" ? "windows" : "ubuntu"); throw new InvalidOperationException("Copied wrapper negative accepted: " + name); }
        catch (InvalidDataException error) { result.Add(new JsonObject { ["name"] = name, ["passed"] = true, ["expectedRejection"] = error.Message }); }
    }
    Write(Path.Combine(output, "wrapper-controls.json"), result); return result;

    HealthcheckMutationNativeKernelEvidence InvokeWrapper(JsonObject document, string name, string lane)
    {
        string path = Path.Combine(output, "wrapper-" + name + ".json"); Write(path, document);
        var descriptor = new JsonObject { ["group"] = "unit-runtime", ["kernelManifest"] = Pin(path), ["logsManifest"] = Pin(logInventoryPath) };
        Type owner = typeof(HealthcheckMutationUnionVerification);
        Type pinned = owner.GetNestedType("PinnedInputs", System.Reflection.BindingFlags.NonPublic)!;
        object files = Activator.CreateInstance(pinned, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, [task, Path.Combine(output, "wrapper-consumer-output")], CultureInfo.InvariantCulture)!;
        try
        {
            object? read = owner.GetMethod("ReadKernelInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [files, descriptor, lane]);
            pinned.GetMethod("VerifyAgain", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(files, null);
            return (HealthcheckMutationNativeKernelEvidence)read!;
        }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}

void Scan(bool verify)
{
    var again = new Dictionary<int, List<ProcessView.Source>>(); var boundary = new Boundary(); long offset = 0, lines = 0, compressedBytes = 0;
    using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var partial = new MemoryStream(); byte[] buffer = new byte[65536]; int number = 0;
    foreach (JsonObject member in members)
    {
        number++; string expected = native + "/wire/trace-" + number.ToString("D6", CultureInfo.InvariantCulture) + ".strace.gz";
        Need((string?)member["path"] == expected && (int?)member["number"] == number && (long?)member["rawStart"] == offset + partial.Length && (bool?)member["gzipClosed"] == true && (bool?)member["memberFinalizationComplete"] == true && member["errors"]!.AsArray().Count == 0, "Member order/origin/closure differs.");
        string path = Path.Combine(source, "wire", Path.GetFileName(expected)); NoLinks(path); Need(Hash(path) == (string?)member["compressedSha256"] && new FileInfo(path).Length == (long)member["compressedBytes"]!, "Actual compressed member pin differs."); compressedBytes += new FileInfo(path).Length;
        using var file = File.OpenRead(path); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long memberBytes = 0;
        for (int n; (n = gzip.Read(buffer)) > 0;)
        {
            hash.AppendData(buffer, 0, n); whole.AppendData(buffer, 0, n); memberBytes += n; int pos = 0;
            while (pos < n)
            {
                int lf = buffer.AsSpan(pos, n - pos).IndexOf((byte)'\n'), take = lf < 0 ? n - pos : lf; partial.Write(buffer, pos, take); pos += take;
                Need(partial.Length <= 67_108_864, "Raw record exceeds64MiB explicit framing bound."); if (lf < 0) { continue; }
                partial.WriteByte((byte)'\n'); byte[] raw = partial.ToArray(); largest = Math.Max(largest, raw.Length); int end = 0;
                while (end < raw.Length && raw[end] is >= (byte)'0' and <= (byte)'9') { end++; }
                Need(end is > 0 and < 12 && raw[end] == (byte)' ', "Raw PID prefix absent."); int pid = int.Parse(Encoding.ASCII.GetString(raw, 0, end), CultureInfo.InvariantCulture); Need(pid > 0, "Invalid raw PID.");
                while (end < raw.Length && raw[end] == (byte)' ') { end++; } string body = new UTF8Encoding(false, true).GetString(raw, end, raw.Length - end - 1);
                (bool process, long localOffset) = boundary.Observe(pid, body, raw.Length - end);
                if (process)
                {
                    var row = new ProcessView.Source(localOffset, raw.Length - end, Sha(raw.AsSpan(end)), body); var table = verify ? again : selected;
                    if (!table.TryGetValue(pid, out List<ProcessView.Source>? list)) { list = []; table.Add(pid, list); } list.Add(row);
                    if (!verify) { origins.Add((pid, localOffset), new Origin(offset, raw.Length, Sha(raw), Encoding.ASCII.GetString(raw, 0, end), row.Offset, row.Bytes, row.Sha256)); }
                    else { Need(origins.TryGetValue((pid, localOffset), out Origin? origin) && origin.RawStart == offset && origin.RawBytes == raw.Length && origin.RawSha256 == Sha(raw) && origin.PerPidSha256 == row.Sha256, "Process-origin regeneration differs."); }
                }
                offset += raw.Length; lines++; partial.SetLength(0); pos++; if ((lines & 1023) == 0) { Guard(); }
            }
        }
        Need(memberBytes == (long)member["rawBytes"]! && offset + partial.Length == (long)member["rawEnd"]! && Convert.ToHexStringLower(hash.GetHashAndReset()) == (string?)member["rawSha256"] && Hash(path) == (string?)member["compressedSha256"], "Raw member bytes or source pin changed.");
    }
    boundary.Finish(); Need(partial.Length == 0 && offset == (long)sink["rawBytesReceived"]! && lines == (long)sink["newlineCount"]! && compressedBytes == (long)sink["compressedBytes"]! && Convert.ToHexStringLower(whole.GetHashAndReset()) == (string?)sink["rawSha256Received"], "Whole original raw primary differs or ends midrecord.");
    if (verify)
    {
        Need(again.Keys.ToHashSet().SetEquals(selected.Keys), "Regenerated process PID scope differs.");
        foreach ((int pid, List<ProcessView.Source> values) in again)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(ProcessView.Derive(values.ToArray()).Select(x => x.Text + "\n"))); string path = Path.Combine(output, "process-view/actual-process." + pid);
            Need(Hash(path) == Sha(bytes) && new FileInfo(path).Length == bytes.Length, "Regenerated process projection differs.");
        }
    }
    total = offset; count = lines;
}
void Guard()
{
    if (clock.ElapsedMilliseconds - lastGuard < 2000) { return; } lastGuard = clock.ElapsedMilliseconds;
    Need(clock.Elapsed < TimeSpan.FromMinutes(15) && new DriveInfo(Path.GetPathRoot(Path.GetFullPath(output))!).AvailableFreeSpace >= 2_000_000_000, "Projection time/actual-free guard failed; incomplete output retained.");
    Need(Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length) <= 1_000_000_000, "Projection logical output ceiling exceeded.");
}
static void NoLinks(string path) { for (string? item = Path.GetFullPath(path); item is not null; item = Path.GetDirectoryName(item)) { Need(!File.GetAttributes(item).HasFlag(FileAttributes.ReparsePoint), "Input link/reparsepoint rejected."); } }
static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static HealthcheckMutationNativeProcessProjection.Pin ProjectionPin(string path) => new(path, Hash(path), new FileInfo(path).Length);
static JsonObject Pin(string path) => new() { ["path"] = path, ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };
static void Write(string path, JsonNode value) { using var file = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true); }
static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
}
internal sealed record Origin(long RawStart, int RawBytes, string RawSha256, string PidPrefix, long PerPidOffset, int PerPidBytes, string PerPidSha256);
