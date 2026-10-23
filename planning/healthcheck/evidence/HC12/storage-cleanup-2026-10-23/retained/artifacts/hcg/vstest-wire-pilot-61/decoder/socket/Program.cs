using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Explicitly bounded socket-byte probe, not a qualification or general strace reader.
string repo = Path.GetFullPath(args.Single());
string basis = Path.Combine(repo, ".artifacts/hcg/vstest-wire-pilot-61");
string input = Path.Combine(basis, "decoder/extract-01/selected-window.strace");
string output = Path.Combine(basis, "decoder/socket/actual-06");
Need(!Directory.Exists(output) && Hash(input) == "2c1c336835e776b24e6e51b929ef379d6c3a33e2434e19c4b15e5d08f6712fab", "Exact verified immutable selected window required.");
Directory.CreateDirectory(output);
string original = Path.Combine(basis, "actual-pilot-import01/imported/unit-wire-pilot61");
IReadOnlyDictionary<int, EpochValidation.Creation> creations = EpochValidation.ReadCreations(original);
List<EpochValidation.Call> epochCalls = EpochValidation.ReadWindow(input);
JsonObject epoch = EpochValidation.Validate(creations, epochCalls);
var streams = new Dictionary<string, List<Transfer>>(StringComparer.Ordinal)
{
    ["controllerSent"] = [], ["controllerReceived"] = [], ["hostSent"] = [], ["hostReceived"] = []
};
var pending = new Dictionary<int, Record>();
var calls = new JsonArray();
var ignored = new Dictionary<string, int>(StringComparer.Ordinal);
int number = 0, joined = 0;
long offset = 0;
foreach (string line in File.ReadLines(input, new UTF8Encoding(false, true)))
{
    number++;
    Match prefix = Regex.Match(line, @"^(\d+) +(\d+\.\d+) (.*)$", RegexOptions.CultureInvariant); Need(prefix.Success, "Raw line prefix absent.");
    int tid = int.Parse(prefix.Groups[1].Value, CultureInfo.InvariantCulture);
    string body = prefix.Groups[3].Value;
    var record = new Record(tid, decimal.Parse(prefix.Groups[2].Value, CultureInfo.InvariantCulture), body, number, offset, Encoding.UTF8.GetByteCount(line) + 1, HashText(line));
    offset += record.Bytes;
    Match resume = Regex.Match(body, @"^<\.\.\. ([a-zA-Z0-9_]+) resumed>(.*)$", RegexOptions.CultureInvariant);
    if (resume.Success)
    {
        if (!pending.Remove(tid, out Record? first)) { continue; }
        string call = resume.Groups[1].Value;
        Need(first.Body.StartsWith(call + "(", StringComparison.Ordinal), "Mismatched selected unfinished/resumed syscall.");
        Need(body.IndexOf("<unfinished", StringComparison.Ordinal) < 0, "Repeated unfinished selected transfer unsupported.");
        body = first.Body[..first.Body.IndexOf(" <unfinished ...>", StringComparison.Ordinal)] + resume.Groups[2].Value;
        Observe(first, record, body); joined++;
        continue;
    }
    bool target = body.Contains("45063->127.0.0.1:54454", StringComparison.Ordinal) || body.Contains("54454->[::ffff:127.0.0.1]:45063", StringComparison.Ordinal);
    if (!target) { continue; }
    Need(!pending.ContainsKey(tid), "Selected socket has an unreturned earlier syscall on the same thread.");
    if (body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { pending.Add(tid, record); continue; }
    Observe(record, null, body);
}
Need(pending.Count == 0, "Unfinished target socket records remain.");
var bytes = streams.ToDictionary(x => x.Key, x => x.Value.SelectMany(y => y.Bytes).ToArray(), StringComparer.Ordinal);
Need(bytes["controllerSent"].AsSpan().SequenceEqual(bytes["hostReceived"]), "Controller-send and host-receive bytes differ.");
Need(bytes["hostSent"].AsSpan().SequenceEqual(bytes["controllerReceived"]), "Host-send and controller-receive bytes differ.");
var frames = new JsonObject();
foreach ((string name, byte[] stream) in bytes)
{
    File.WriteAllBytes(Path.Combine(output, name + ".bin"), stream);
    frames[name] = Decode(stream, streams[name], name);
}
string hostLog = Path.Combine(basis, "actual-pilot-import01/imported/unit-wire-pilot61/native/logs/Runner 1-log.host.26-10-23_01-24-20_22741_4.txt");
var sent = new JsonArray(); var received = new JsonArray();
foreach (string line in File.ReadLines(hostLog))
{
    const string send = "TestRequestHandler.SendData: sending data from testhost: ";
    const string receive = "TestRequestHandler.OnMessageReceived: received message: ";
    int at = line.IndexOf(send, StringComparison.Ordinal); if (at >= 0) { sent.Add(JsonNode.Parse(line[(at + send.Length)..])); }
    at = line.IndexOf(receive, StringComparison.Ordinal); if (at >= 0) { received.Add(JsonNode.Parse(line[(at + receive.Length)..])); }
}
JsonArray wireSent = frames["hostSent"]!.AsArray(), wireReceived = frames["hostReceived"]!.AsArray();
// ProtocolVersion send is serialized directly by communication manager and is absent from the host SendData diagnostic.
JsonArray sentWithoutHandshake = new(wireSent.OfType<JsonObject>().Where(x => (string?)x["message"]?["MessageType"] != "ProtocolVersion").Select(x => x["message"]!.DeepClone()).ToArray());
JsonArray receivedMessages = new(wireReceived.OfType<JsonObject>().Select(x => x["message"]!.DeepClone()).ToArray());
Need(JsonNode.DeepEquals(sentWithoutHandshake, sent) && JsonNode.DeepEquals(receivedMessages, received), "Wire and exact host diagnostic message sequences differ.");
JsonArray controls = ProbeControls.Run(creations, epochCalls);
Write(Path.Combine(output, "controls.json"), controls);
var result = new JsonObject
{
    ["schema"] = "cis.healthcheck.wire-pilot61-target-socket/v1-private-byte-and-message-probe",
    ["completeBoundedProbe"] = true, ["observedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["rawWindow"] = Pin(input),
    ["targetHostPid"] = 1029071, ["targetControllerPid"] = 1028660, ["endpointPair"] = "127.0.0.1:45063 <->127.0.0.1:54454",
    ["targetSocketDescriptorController"] = 159, ["targetSocketDescriptorHost"] = 80, ["actualJoinedUnfinishedCalls"] = joined,
    ["socketTransfers"] = calls, ["otherTargetSocketCalls"] = JsonSerializer.SerializeToNode(ignored), ["frames"] = frames,
    ["epoch"] = epoch,
    ["pairedTransferredBytesEqual"] = true, ["exactHostLoggedMessagesEqual"] = true, ["hostLog"] = Pin(hostLog),
    ["limitations"] = new JsonArray("Socket endpoint/FD observations only; a full process/thread/descriptor-epoch and unsupported-transfer closure remains required.",
        "No accepted kernel derivative is emitted and no missing log record is reconstructed.", "This probe validates one target socket, not the preceding full coverage/discovery streams or the current full55 capture."),
    ["wholeCaptureIpcCompletenessClaim"] = false, ["normalHostClaim"] = false, ["qualificationCredit"] = 0
};
result["controls"] = new JsonObject { ["count"] = controls.Count, ["allPassed"] = true, ["physical"] = Pin(Path.Combine(output, "controls.json")), ["syntheticParserControlsOnly"] = true };
Write(Path.Combine(output, "results.json"), result);
Console.WriteLine(JsonSerializer.Serialize(new { result = Path.Combine(output, "results.json"), sha256 = Hash(Path.Combine(output, "results.json")), joined, controllerBytes = bytes["controllerSent"].Length, hostBytes = bytes["hostSent"].Length, controllerFrames = wireReceived.Count, hostFrames = wireSent.Count }));

void Observe(Record first, Record? last, string body)
{
    Match endpoint = Regex.Match(body, @"^(\w+)\((\d+)<(TCP(?:v6)?):\[(.*?)\]>", RegexOptions.CultureInvariant);
    Need(endpoint.Success, "Target socket syscall framing unsupported.");
    string call = endpoint.Groups[1].Value, fd = endpoint.Groups[2].Value, address = endpoint.Groups[4].Value;
    if (call is not ("sendto" or "recvmsg")) { ignored[call + ":" + address] = ignored.GetValueOrDefault(call + ":" + address) + 1; return; }
    bool controller = address == "127.0.0.1:45063->127.0.0.1:54454";
    Need(controller || address == "[::ffff:127.0.0.1]:54454->[::ffff:127.0.0.1]:45063", "Target endpoint changed.");
    Need(fd == (controller ? "159" : "80"), "Target descriptor changed.");
    Match returned = Regex.Match(body, @"\) = (-?\d+)(?: [A-Z][A-Z0-9_]* \([^\r\n]*\))?$", RegexOptions.CultureInvariant);
    Need(returned.Success, "Selected transfer return code absent or unsupported.");
    int count = int.Parse(returned.Groups[1].Value, CultureInfo.InvariantCulture);
    if (count < 0) { Need(body.EndsWith("EAGAIN (Resource temporarily unavailable)", StringComparison.Ordinal), "Selected syscall failed unexpectedly."); return; }
    byte[] data = WireBytes.Transfer(body);
    string name = (controller ? "controller" : "host") + (call == "sendto" ? "Sent" : "Received");
    List<Transfer> transfers = streams[name];
    decimal ended = last?.Stamp ?? first.Stamp;
    Need(transfers.Count == 0 || transfers[^1].End <= first.Stamp, "Concurrent target stream operations require an explicit ordering proof.");
    long streamStart = transfers.Sum(x => (long)x.Bytes.Length);
    transfers.Add(new Transfer(first, last, ended, data, streamStart));
    calls.Add(new JsonObject { ["direction"] = name, ["syscall"] = call, ["returnedBytes"] = count, ["streamStart"] = streamStart,
        ["bytesSha256"] = Convert.ToHexStringLower(SHA256.HashData(data)), ["start"] = JsonSerializer.SerializeToNode(first), ["resumed"] = last is null ? null : JsonSerializer.SerializeToNode(last) });
}
JsonArray Decode(byte[] data, List<Transfer> transfers, string name)
{
    var result = new JsonArray();
    foreach (WireBytes.Frame frame in WireBytes.Frames(data))
    {
        int end = frame.Start + frame.Prefix + frame.Length;
        var contributors = transfers.Where(x => x.StreamStart < end && x.StreamStart + x.Bytes.Length > frame.Start).Select(x => new { x.StreamStart, x.First.Line, x.First.RawStart, x.First.RawSha256, resumedLine = x.Last?.Line, resumedRawStart = x.Last?.RawStart, resumedRawSha256 = x.Last?.RawSha256 }).ToArray();
        result.Add(new JsonObject { ["number"] = result.Count + 1, ["streamStart"] = frame.Start, ["lengthPrefixBytes"] = frame.Prefix, ["utf8Bytes"] = frame.Length,
            ["utf8Sha256"] = Convert.ToHexStringLower(SHA256.HashData(data.AsSpan(frame.Start + frame.Prefix, frame.Length))), ["sourceTransfers"] = JsonSerializer.SerializeToNode(contributors), ["message"] = frame.Message });
    }
    Write(Path.Combine(output, name + "-frames.json"), result); return result;
}
static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
static string HashText(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static JsonObject Pin(string path) => new() { ["path"] = path, ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };
static void Write(string path, JsonNode value) { using var file = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true); }
static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
internal sealed record Record(int Tid, decimal Stamp, string Body, int Line, long RawStart, int Bytes, string RawSha256);
internal sealed record Transfer(Record First, Record? Last, decimal End, byte[] Bytes, long StreamStart);
