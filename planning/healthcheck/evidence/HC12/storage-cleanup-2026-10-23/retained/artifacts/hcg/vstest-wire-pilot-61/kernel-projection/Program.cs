using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CIS.Verification;

string repo = Path.GetFullPath(args.Single()), basis = Path.Combine(repo, ".artifacts/hcg/vstest-wire-pilot-61");
string source = Path.Combine(basis, "actual-pilot-import01/imported/unit-wire-pilot61"), output = Path.Combine(basis, "kernel-projection/actual-03");
Need(!Directory.Exists(output), "New immutable derivative output required.");
string kernelSource = Path.Combine(repo, "implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Union/HealthcheckMutationNativeKernelEvidence.cs");
JsonObject kernelPin = Pin(kernelSource);
JsonObject sink = JsonNode.Parse(File.ReadAllBytes(Path.Combine(source, "wire/sink-result.json")))!.AsObject();
Need((bool?)sink["complete"] == true && (bool?)sink["observedEof"] == true && (long?)sink["rawBytesReceived"] == 552913184 && (string?)sink["rawSha256Received"] == "aed05e349962c0dbd5513d5ad2021fd2bc9a315e257d95136b01dc262f35586a", "Exact complete original closed primary required.");
Directory.CreateDirectory(output); string rawRoot = Path.Combine(output, "all-pids"), processRoot = Path.Combine(output, "process-view"); Directory.CreateDirectory(rawRoot); Directory.CreateDirectory(processRoot);
var files = new Dictionary<int, LosslessPidWriter>(); var perPidRows = new Dictionary<int, long>(); var members = new JsonArray();
string indexPath = Path.Combine(output, "raw-line-index.jsonl.gz"); long rawBytes = 0, rows = 0, rawStart = 0;
using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var partial = new MemoryStream(); byte[] buffer = new byte[65536];
using (var indexFile = new FileStream(indexPath, FileMode.CreateNew))
{
    using (var gzipIndex = new GZipStream(indexFile, CompressionLevel.Fastest, true))
    using (var index = new StreamWriter(gzipIndex, new UTF8Encoding(false), 65536, true) { NewLine = "\n" })
    {
        foreach (JsonObject member in sink["members"]!.AsArray().OfType<JsonObject>())
        {
            string path = Path.Combine(source, "wire", Path.GetFileName((string)member["path"]!)); Need(Hash(path) == (string?)member["compressedSha256"] && new FileInfo(path).Length == (long)member["compressedBytes"]! && rawBytes == (long)member["rawStart"]!, "Primary member order/pin differs."); members.Add(Pin(path));
            using var input = File.OpenRead(path); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var memberHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long memberBytes = 0;
            for (int n; (n = gzip.Read(buffer)) > 0;)
            {
                whole.AppendData(buffer, 0, n); memberHash.AppendData(buffer, 0, n); memberBytes += n; rawBytes += n;
                int at = 0;
                while (at < n)
                {
                    int lf = buffer.AsSpan(at, n - at).IndexOf((byte)'\n'), take = lf < 0 ? n - at : lf;
                    partial.Write(buffer, at, take); Need(partial.Length <= 33_554_432, "Physical raw line exceeds bounded supported size."); at += take;
                    if (lf < 0) { continue; }
                    partial.WriteByte((byte)'\n'); byte[] line = partial.ToArray(); int prefixEnd = 0;
                    while (prefixEnd < line.Length && line[prefixEnd] is >= (byte)'0' and <= (byte)'9') { prefixEnd++; }
                    Need(prefixEnd > 0 && prefixEnd < 12 && line[prefixEnd] == (byte)' ', "Raw PID prefix is not a positive decimal PID."); int pid = int.Parse(Encoding.ASCII.GetString(line, 0, prefixEnd), CultureInfo.InvariantCulture); Need(pid > 0, "Invalid PID.");
                    while (prefixEnd < line.Length && line[prefixEnd] == (byte)' ') { prefixEnd++; }
                    Need(prefixEnd < line.Length, "Empty raw PID record.");
                    if (!files.TryGetValue(pid, out LosslessPidWriter? target)) { target = new LosslessPidWriter(Path.Combine(rawRoot, "unified-pid." + pid + ".gz")); files.Add(pid, target); }
                    long targetStart = target.Position; target.Write(line.AsSpan(prefixEnd));
                    index.WriteLine(JsonSerializer.Serialize(new { sourceRawStart = rawStart, sourceBytes = line.Length, sourceSha256 = HashBytes(line), pid, pidPrefix = Encoding.ASCII.GetString(line, 0, prefixEnd), derivedStart = targetStart, derivedBytes = line.Length - prefixEnd, derivedSha256 = HashBytes(line.AsSpan(prefixEnd)) }));
                    perPidRows[pid] = perPidRows.GetValueOrDefault(pid) + 1; rawStart += line.Length; rows++; partial.SetLength(0); at++;
                }
            }
            Need(memberBytes == (long)member["rawBytes"]! && rawBytes == (long)member["rawEnd"]! && Convert.ToHexStringLower(memberHash.GetHashAndReset()) == (string?)member["rawSha256"], "Exact decompressed member differs.");
        }
        index.Flush();
    }
    indexFile.Flush(true);
}
foreach (LosslessPidWriter file in files.Values) { file.Complete(); }
Need(partial.Length == 0 && rawBytes == rawStart && rawBytes == 552913184 && rows == 841444 && Convert.ToHexStringLower(whole.GetHashAndReset()) == (string?)sink["rawSha256Received"], "Whole raw primary/decompression framing differs.");

// Verify reversibility from derivative files and their exact index before any kernel consumption.
var reads = new Dictionary<int, LosslessPidReader>(); long reconstructed = 0, reconstructedRows = 0;
using (var rebuilt = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
using (var input = File.OpenRead(indexPath))
using (var gzip = new GZipStream(input, CompressionMode.Decompress))
using (var index = new StreamReader(gzip, new UTF8Encoding(false, true), false, 65536))
{
    while (index.ReadLine() is { } text)
    {
        JsonObject row = JsonNode.Parse(text)!.AsObject(); int pid = (int)row["pid"]!; string prefix = (string)row["pidPrefix"]!;
        Need(Regex.IsMatch(prefix, "^" + pid + @" +$", RegexOptions.CultureInvariant) && (long?)row["sourceRawStart"] == reconstructed, "Index PID prefix/source order differs.");
        if (!reads.TryGetValue(pid, out LosslessPidReader? file)) { file = new LosslessPidReader(Path.Combine(rawRoot, "unified-pid." + pid + ".gz")); reads.Add(pid, file); }
        Need(file.Position == (long)row["derivedStart"]!, "Index perPID derivative order differs.");
        int count = (int)row["derivedBytes"]!; Need(count > 0 && count <= 33_554_432, "Index row length invalid."); byte[] body = new byte[count]; file.ReadExactly(body);
        Need(body[^1] == (byte)'\n' && HashBytes(body) == (string?)row["derivedSha256"], "Derived row bytes differ.");
        byte[] prefixBytes = Encoding.ASCII.GetBytes(prefix); byte[] original = new byte[prefixBytes.Length + body.Length]; prefixBytes.CopyTo(original, 0); body.CopyTo(original, prefixBytes.Length);
        Need(original.Length == (int)row["sourceBytes"]! && HashBytes(original) == (string?)row["sourceSha256"], "Reconstructed original row differs.");
        rebuilt.AppendData(original); reconstructed += original.Length; reconstructedRows++;
    }
    Need(reconstructed == rawBytes && reconstructedRows == rows && Convert.ToHexStringLower(rebuilt.GetHashAndReset()) == (string?)sink["rawSha256Received"], "Reconstructed whole original primary differs.");
}
Need(reads.Count == files.Count && reads.All(x => x.Value.Position == files[x.Key].Position && x.Value.AtEof()), "Extra or missing raw perPID contents."); foreach (LosslessPidReader file in reads.Values) { file.Dispose(); }

var processPins = new List<HealthcheckMutationNativeKernelEvidence.Pin>(); var rawPins = new JsonArray(); long processRows = 0;
string processIndexPath = Path.Combine(output, "process-line-index.jsonl");
using (var processIndex = new StreamWriter(new FileStream(processIndexPath, FileMode.CreateNew), new UTF8Encoding(false)) { NewLine = "\n" })
{
    foreach (int pid in files.Keys.Order())
    {
        string rawPath = Path.Combine(rawRoot, "unified-pid." + pid + ".gz"); long localOffset = 0;
        using var rawFile = File.OpenRead(rawPath); using var rawGzip = new GZipStream(rawFile, CompressionMode.Decompress); using var rawReader = new StreamReader(rawGzip, new UTF8Encoding(false, true), false, 65536);
        var sourceRows = new List<ProcessView.Source>();
        while (rawReader.ReadLine() is { } text) { int bytes = Encoding.UTF8.GetByteCount(text) + 1; sourceRows.Add(new ProcessView.Source(localOffset, bytes, HashBytes(Encoding.UTF8.GetBytes(text + "\n")), text)); localOffset += bytes; }
        ProcessView.Source[] inputs = sourceRows.ToArray();
        Need(localOffset == files[pid].Position, "Process derivation lost raw perPID line endings.");
        rawPins.Add(new JsonObject { ["pid"] = pid, ["compressed"] = Pin(rawPath), ["rawBytes"] = files[pid].Position, ["rawSha256"] = files[pid].RawSha256, ["originalReconstructionVerified"] = true });
        ProcessView.Line[] lines = ProcessView.Derive(inputs); string processPath = Path.Combine(processRoot, "actual-process." + pid);
        using (var target = new FileStream(processPath, FileMode.CreateNew))
        {
            foreach (ProcessView.Line line in lines)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line.Text + "\n"); long targetOffset = target.Position; target.Write(bytes); processRows++;
                processIndex.WriteLine(JsonSerializer.Serialize(new { pid, processStart = targetOffset, processBytes = bytes.Length, processSha256 = HashBytes(bytes), line.Rule, sources = line.Sources.Select(x => new { perPidOffset = x.Offset, x.Bytes, x.Sha256 }) }));
            }
            target.Flush(true);
        }
        // A separately regenerated process view must exactly equal the physical file.
        byte[] regenerated = Encoding.UTF8.GetBytes(string.Concat(ProcessView.Derive(inputs).Select(x => x.Text + "\n")));
        Need(HashBytes(regenerated) == Hash(processPath) && regenerated.Length == new FileInfo(processPath).Length, "Origin-verified process derivation differs.");
        processPins.Add(new(processPath, Hash(processPath), new FileInfo(processPath).Length));
    }
}

string logs = Path.Combine(source, "native/logs"), nativeLogs = "/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61/native/logs";
string dotnetPath = Path.Combine(source, "tool-origins/dotnet"), dotnetNative = "/data/cis-healthcheck-20261021-01a15a72/dotnet/dotnet";
Need(Hash(dotnetPath) == "01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269", "Actual physical native dotnet body differs.");
var evidence = HealthcheckMutationNativeKernelEvidence.Read(processPins, new(new(dotnetPath, Hash(dotnetPath), new FileInfo(dotnetPath).Length), dotnetNative), logs, nativeLogs);
bool confirmed = evidence.TryConfirmDiscoveryHost(1028708, 1028659, Path.Combine(logs, "TestDiscoverer-log.host.26-10-23_01-18-46_14622_4.txt"), Path.Combine(logs, "TestDiscoverer-log.txt"), ["/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61/source/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll"], "/data/cis-healthcheck-20261021-01a15a72/dotnet/shared/Microsoft.NETCore.App/10.0.12", out JsonObject? proof);
Need(confirmed && proof is not null, "Unchanged maintained kernel reader did not confirm actual discovery ancestry/natural exits.");
JsonArray controls = ProjectionControls.Run();
foreach ((string name, int testHost, int testParent, string testSource, string runtime) in new[]
{
    ("wrong-controller-cannot-inherit-proof", 1028708, 1028660, "/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61/source/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll", "/data/cis-healthcheck-20261021-01a15a72/dotnet/shared/Microsoft.NETCore.App/10.0.12"),
    ("wrong-host-cannot-inherit-proof", 1029071, 1028659, "/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61/source/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll", "/data/cis-healthcheck-20261021-01a15a72/dotnet/shared/Microsoft.NETCore.App/10.0.12"),
    ("wrong-requested-source-cannot-inherit-proof", 1028708, 1028659, "/data/unrelated.dll", "/data/cis-healthcheck-20261021-01a15a72/dotnet/shared/Microsoft.NETCore.App/10.0.12"),
    ("wrong-runtime-cannot-inherit-proof", 1028708, 1028659, "/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61/source/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll", "/data/cis-healthcheck-20261021-01a15a72/dotnet/shared/Microsoft.NETCore.App/8.0.31")
})
{
    Need(!evidence.TryConfirmDiscoveryHost(testHost, testParent, Path.Combine(logs, "TestDiscoverer-log.host.26-10-23_01-18-46_14622_4.txt"), Path.Combine(logs, "TestDiscoverer-log.txt"), [testSource], runtime, out _), "Copied kernel control was incorrectly accepted.");
    controls.Add(new JsonObject { ["name"] = name, ["passed"] = true, ["expected"] = "reject" });
}
Write(Path.Combine(output, "controls.json"), controls);
Need(Hash(kernelSource) == (string?)kernelPin["sha256"], "Maintained kernel source changed while private read/build was used.");
foreach (JsonObject pin in members.OfType<JsonObject>()) { Need(Hash((string)pin["path"]!) == (string?)pin["sha256"], "Original raw compressed member changed after derivation."); }
var descriptor = new JsonObject
{
    ["schema"] = "cis.healthcheck.unified-kernel-projection/v1-private-explicit-derivation", ["completeProjection"] = true, ["observedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
    ["originalPrimary"] = new JsonObject { ["rawBytes"] = rawBytes, ["rawSha256"] = sink["rawSha256Received"]!.DeepClone(), ["newlineCount"] = rows, ["orderedCompressedMembers"] = members },
    ["losslessPerPid"] = new JsonObject { ["files"] = rawPins, ["index"] = Pin(indexPath), ["reconstructedOriginalByteForByte"] = true },
    ["processView"] = new JsonObject { ["files"] = JsonSerializer.SerializeToNode(processPins), ["lineIndex"] = Pin(processIndexPath), ["rows"] = processRows, ["profile"] = "Only raw creation/exec/exit/terminal records selected. Exact paired execve entry/resume combined, all quoted -xx bytes decoded as strict UTF8 and JSON escaped. All originals stay in lossless perPID files.", ["originRegenerationVerified"] = true },
    ["unchangedMaintainedReaderSource"] = kernelPin, ["actualDiscoveryKernelObservation"] = proof, ["notLegacyRawCapture"] = true,
    ["privateControls"] = new JsonObject { ["count"] = controls.Count, ["allPassed"] = true, ["pin"] = Pin(Path.Combine(output, "controls.json")) },
    ["wholeIpcClaim"] = false, ["executionOutcomeCredit"] = 0, ["wholeUnionQualificationClaim"] = false, ["historicalLoadedImageClaim"] = false
};
Write(Path.Combine(output, "descriptor.json"), descriptor);
Console.WriteLine(JsonSerializer.Serialize(new { descriptor = Path.Combine(output, "descriptor.json"), sha256 = Hash(Path.Combine(output, "descriptor.json")), rawBytes, rows, pidFiles = files.Count, processRows, actualDiscoveryConfirmed = confirmed }));

static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
static string HashBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static JsonObject Pin(string path) => new() { ["path"] = path, ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };
static void Write(string path, JsonNode document) { using var file = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(file, document, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true); }
static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }

internal sealed class LosslessPidWriter
{
    private readonly FileStream file; private readonly GZipStream gzip; private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    internal long Position { get; private set; } internal string? RawSha256 { get; private set; }
    internal LosslessPidWriter(string path) { file = new FileStream(path, FileMode.CreateNew); gzip = new GZipStream(file, CompressionLevel.Fastest, true); }
    internal void Write(ReadOnlySpan<byte> bytes) { gzip.Write(bytes); hash.AppendData(bytes); Position += bytes.Length; }
    internal void Complete() { gzip.Dispose(); file.Flush(true); file.Dispose(); RawSha256 = Convert.ToHexStringLower(hash.GetHashAndReset()); hash.Dispose(); }
}
internal sealed class LosslessPidReader : IDisposable
{
    private readonly FileStream file; private readonly GZipStream gzip; internal long Position { get; private set; }
    internal LosslessPidReader(string path) { file = File.OpenRead(path); gzip = new GZipStream(file, CompressionMode.Decompress); }
    internal void ReadExactly(byte[] bytes) { gzip.ReadExactly(bytes); Position += bytes.Length; }
    internal bool AtEof() => gzip.ReadByte() == -1;
    public void Dispose() { gzip.Dispose(); file.Dispose(); }
}
