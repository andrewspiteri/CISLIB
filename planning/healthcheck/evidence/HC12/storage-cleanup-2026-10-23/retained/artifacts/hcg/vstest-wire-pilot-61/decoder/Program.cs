using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Read-only extraction of exact closed pilot bytes. This is not an IPC acceptance reader.
string repo = Path.GetFullPath(args.Single());
string basis = Path.Combine(repo, ".artifacts/hcg/vstest-wire-pilot-61");
string source = Path.Combine(basis, "actual-pilot-import01/imported/unit-wire-pilot61");
string output = Path.Combine(basis, "decoder/extract-01");
Need(!Directory.Exists(output), "Immutable new output required.");
string sinkPath = Path.Combine(source, "wire/sink-result.json");
JsonObject sink = JsonNode.Parse(File.ReadAllBytes(sinkPath))!.AsObject();
Need((bool?)sink["complete"] == true && (bool?)sink["observedEof"] == true && sink["failure"] is null &&
    (string?)sink["rawSha256Received"] == "aed05e349962c0dbd5513d5ad2021fd2bc9a315e257d95136b01dc262f35586a", "Exact closed original raw primary required.");
Directory.CreateDirectory(output);
using var tail = new FileStream(Path.Combine(output, "selected-window.strace"), FileMode.CreateNew);
using var index = new StreamWriter(new FileStream(Path.Combine(output, "selected-window-index.jsonl"), FileMode.CreateNew), new UTF8Encoding(false));
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
using var current = new MemoryStream();
long rawBytes = 0, rows = 0, start = 0, selectedBytes = 0, selectedRows = 0;
var calls = new Dictionary<string, long>(StringComparer.Ordinal);
var byThread = new Dictionary<int, long>();
var pins = new JsonArray();
byte[] block = new byte[65536];
const decimal Start = 1792718659.047m; // One second before the exact selected request; no inferred missing bytes.
foreach (JsonObject member in sink["members"]!.AsArray().OfType<JsonObject>())
{
    string path = Path.Combine(source, "wire", Path.GetFileName((string)member["path"]!));
    Need(new FileInfo(path).Length == (long)member["compressedBytes"]! && Hash(path) == (string?)member["compressedSha256"], "Compressed primary pin differs.");
    Need(rawBytes == (long)member["rawStart"]!, "Member source offset differs.");
    pins.Add(Pin(path));
    using var input = File.OpenRead(path);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var memberHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    long memberBytes = 0;
    for (int n; (n = gzip.Read(block)) > 0;)
    {
        hash.AppendData(block, 0, n); memberHash.AppendData(block, 0, n); memberBytes += n; rawBytes += n;
        int pos = 0;
        while (pos < n)
        {
            int newline = block.AsSpan(pos, n - pos).IndexOf((byte)'\n');
            int length = newline < 0 ? n - pos : newline;
            current.Write(block, pos, length); Need(current.Length <= 33_554_432, "Bounded raw physical line exceeded."); pos += length;
            if (newline < 0) { continue; }
            byte[] data = current.ToArray();
            string line = new UTF8Encoding(false, true).GetString(data);
            Match prefix = Regex.Match(line, @"^(\d+) +(\d+\.\d+) (.*)$", RegexOptions.CultureInvariant);
            Need(prefix.Success, "Unframed raw primary line.");
            int tid = int.Parse(prefix.Groups[1].Value, CultureInfo.InvariantCulture);
            decimal stamp = decimal.Parse(prefix.Groups[2].Value, CultureInfo.InvariantCulture);
            if (stamp >= Start)
            {
                long localStart = tail.Position;
                tail.Write(data); tail.WriteByte((byte)'\n'); selectedBytes += data.Length + 1; selectedRows++;
                string body = prefix.Groups[3].Value;
                Match call = Regex.Match(body, @"^(?:<\.\.\. )?([A-Za-z0-9_]+)(?:\(| resumed>)", RegexOptions.CultureInvariant);
                string name = call.Success ? call.Groups[1].Value : "nonSyscall";
                calls[name] = calls.GetValueOrDefault(name) + 1; byThread[tid] = byThread.GetValueOrDefault(tid) + 1;
                index.WriteLine(JsonSerializer.Serialize(new { sourceRawStart = start, sourceRawLength = data.Length + 1, selectedStart = localStart, tid, stamp, syscall = name, rawLineSha256 = Convert.ToHexStringLower(SHA256.HashData(data)) }));
            }
            start += data.Length + 1; rows++; current.SetLength(0); pos++;
        }
    }
    Need(memberBytes == (long)member["rawBytes"]! && Convert.ToHexStringLower(memberHash.GetHashAndReset()) == (string?)member["rawSha256"] && rawBytes == (long)member["rawEnd"]!, "Raw member pin differs.");
}
Need(current.Length == 0 && start == rawBytes && rawBytes == (long)sink["rawBytesReceived"]! && rows == (long)sink["newlineCount"]! && Convert.ToHexStringLower(hash.GetHashAndReset()) == (string?)sink["rawSha256Received"], "Whole original raw prefix differs.");
tail.Flush(true); tail.Dispose(); index.Flush(); index.Dispose();
var result = new JsonObject
{
    ["schema"] = "cis.healthcheck.wire-pilot61-selected-window/v1-readonly-extraction",
    ["complete"] = true, ["observedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["primaryRawSha256"] = sink["rawSha256Received"]!.DeepClone(),
    ["primaryRawBytes"] = rawBytes, ["primaryRows"] = rows, ["windowEarliestEpoch"] = Start, ["windowBytes"] = selectedBytes,
    ["windowRows"] = selectedRows, ["members"] = pins, ["window"] = Pin(Path.Combine(output, "selected-window.strace")),
    ["index"] = Pin(Path.Combine(output, "selected-window-index.jsonl")), ["syscalls"] = JsonSerializer.SerializeToNode(calls), ["threadRows"] = JsonSerializer.SerializeToNode(byThread),
    ["noIpcCompletenessClaim"] = true, ["noKernelAcceptanceClaim"] = true, ["qualificationCredit"] = 0
};
using (var file = new FileStream(Path.Combine(output, "results.json"), FileMode.CreateNew)) { JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true); }
Console.WriteLine(JsonSerializer.Serialize(new { result = Path.Combine(output, "results.json"), sha256 = Hash(Path.Combine(output, "results.json")), selectedRows, selectedBytes }));

static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
static JsonObject Pin(string path) => new() { ["path"] = path, ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };
static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
