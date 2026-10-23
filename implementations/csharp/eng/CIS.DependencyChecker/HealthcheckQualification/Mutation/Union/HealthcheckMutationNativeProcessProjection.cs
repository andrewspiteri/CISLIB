using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CIS.Verification;

// Validates an explicitly derived process view against every byte of a closed
// primary. It supplies no socket/FD-table, execution-outcome or host-normality proof.
internal static class HealthcheckMutationNativeProcessProjection
{
    internal sealed record Pin(string Path, string Sha256, long Bytes);
    internal sealed record Input(string PhysicalRoot, string NativeRoot, Pin Results, Pin Sink, Pin Capture,
        Pin SourceManifest, Pin Origins, Pin[] Members);
    internal sealed record OriginValidation(Pin[] ProcessFiles, Pin[] PrimaryFiles, JsonObject Observation);
    internal sealed class Validation
    {
        private readonly OriginValidation origins;
        private Validation(OriginValidation origins)
        { this.origins = new(origins.ProcessFiles.Select(pin => pin with { }).ToArray(), origins.PrimaryFiles.Select(pin => pin with { }).ToArray(), origins.Observation.DeepClone().AsObject()); }
        internal OriginValidation Origins => new(origins.ProcessFiles.Select(pin => pin with { }).ToArray(), origins.PrimaryFiles.Select(pin => pin with { }).ToArray(), origins.Observation.DeepClone().AsObject());
        internal static Validation ReadClosed(Input input, IReadOnlyList<Pin> processFiles) => new(ReadCore(input, processFiles));
    }
    private sealed record Source(long Offset, int Bytes, string Sha256, string Text, Origin Origin);
    private sealed record Origin(long RawStart, int RawBytes, string RawSha256, string PidPrefix,
        long PerPidOffset, int PerPidBytes, string PerPidSha256);
    private sealed record Line(string Text, Source[] Sources, string Rule);
    private const string TaskRoot = "/data/cis-healthcheck-20261021-01a15a72";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static Validation Read(Input input, IReadOnlyList<Pin> processFiles)
    {
        try { return Validation.ReadClosed(input, processFiles); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Malformed process-origin evidence.", error); }
    }

    private static OriginValidation ReadCore(Input input, IReadOnlyList<Pin> processFiles)
    {
        Need(new[] { input.Results, input.Sink, input.Capture, input.SourceManifest }.All(pin => pin.Bytes is >= 0 and <= 134_217_728)
            && input.Origins.Bytes is >= 0 and <= 1_000_000_000, "Process-origin metadata exceeds supported bounds.");
        Check(input.Results); Check(input.Sink); Check(input.Capture); Check(input.SourceManifest); Check(input.Origins);
        JsonObject result = Document(input.Results), sink = Document(input.Sink), capture = Document(input.Capture);
        ValidateClosure(input, result, sink, capture);
        OriginValidation validation = VerifyOrigins(input, sink, processFiles);
        foreach (Pin pin in new[] { input.Results, input.Sink, input.Capture, input.SourceManifest }) { Check(pin); }
        return validation;
    }

    // Receiving primitive for physical byte/framing controls. Only Read combines
    // it with the mandatory reviewed producer/campaign closure checks.
    internal static OriginValidation VerifyOrigins(Input input, JsonObject sink, IReadOnlyList<Pin> processFiles)
    {
        try { return VerifyOriginsCore(input, sink, processFiles); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Malformed process-origin bytes.", error); }
    }

    private static OriginValidation VerifyOriginsCore(Input input, JsonObject sink, IReadOnlyList<Pin> processFiles)
    {
        Check(input.Origins);
        JsonObject[] members = Array(sink, "members").Select(Item).ToArray();
        Need(members.Length is > 0 and <= 768 && members.Length == input.Members.Length, "Incomplete primary member inventory.");
        Need(Integer(sink, "rawBytesReceived") is > 0 and <= 100_000_000_000, "Primary exceeds the reviewed raw-byte bound.");
        Need(input.Members.Select(pin => Path.GetFullPath(pin.Path)).Distinct(StringComparer.Ordinal).Count() == members.Length,
            "Duplicate primary member path.");
        for (int number = 1; number <= members.Length; number++)
        {
            JsonObject member = members[number - 1]; Pin pin = input.Members[number - 1];
            string name = "trace-" + number.ToString("D6", CultureInfo.InvariantCulture) + ".strace.gz";
            Need(Text(member, "path") == input.NativeRoot + "/wire/" + name
                && Path.GetFullPath(pin.Path) == Path.GetFullPath(Path.Combine(input.PhysicalRoot, "wire", name))
                && pin.Sha256 == Text(member, "compressedSha256") && pin.Bytes == Integer(member, "compressedBytes"),
                "Primary member physical/native path or pin differs.");
        }
        Need(Directory.EnumerateFiles(Path.Combine(input.PhysicalRoot, "wire"), "trace-*.strace.gz").Order(StringComparer.Ordinal)
            .SequenceEqual(input.Members.Select(pin => pin.Path).Order(StringComparer.Ordinal)), "Unlisted/missing primary member.");
        Need(processFiles.Count > 0 && processFiles.Select(pin => Path.GetFullPath(pin.Path)).Distinct(StringComparer.Ordinal).Count() == processFiles.Count,
            "Empty or duplicate derived process inventory.");
        foreach (Pin pin in processFiles) { Check(pin); }
        Dictionary<int, Pin> byPid = processFiles.ToDictionary(pin => PidName(pin.Path));
        long rawBytes = 0, rawRows = 0, processRows = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            Dictionary<int, List<Source>> selected = Scan(input, sink, members, out rawBytes, out rawRows);
            Need(selected.Keys.ToHashSet().SetEquals(byPid.Keys), "Derived PID scope differs from the complete primary.");
            using var indexHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long indexBytes = 0; processRows = 0;
            foreach ((int pid, List<Source> sources) in selected.OrderBy(pair => pair.Key))
            {
                using var processHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long at = 0;
                foreach (Line line in Derive(sources))
                {
                    byte[] bytes = Utf8.GetBytes(line.Text + "\n"); processHash.AppendData(bytes);
                    string record = JsonSerializer.Serialize(new
                    {
                        pid,
                        derivedStart = at,
                        derivedBytes = bytes.Length,
                        derivedSha256 = Sha(bytes),
                        line.Rule,
                        origins = line.Sources.Select(row => row.Origin)
                    });
                    byte[] entry = Utf8.GetBytes(record + "\n"); indexHash.AppendData(entry); indexBytes += entry.Length;
                    at += bytes.Length; processRows++;
                }
                Pin pin = byPid[pid];
                Need(at == pin.Bytes && Hex(processHash.GetHashAndReset()) == pin.Sha256, "Derived process bytes differ from their raw origins.");
                Check(pin);
            }
            Need(indexBytes == input.Origins.Bytes && Hex(indexHash.GetHashAndReset()) == input.Origins.Sha256,
                "The complete selected-origin index differs from exact source regeneration.");
            Check(input.Origins);
        }
        foreach (Pin pin in input.Members) { Check(pin); }
        return new(processFiles.Select(pin => pin with { }).ToArray(), input.Members.Select(pin => pin with { }).ToArray(), new JsonObject
        {
            ["kind"] = "VerifiedDerivedProcessOrigins",
            ["schema"] = "cis.healthcheck.process-origin-validation/v1",
            ["nativeRoot"] = input.NativeRoot,
            ["rawBytesEachPass"] = rawBytes,
            ["rawRowsEachPass"] = rawRows,
            ["verificationPasses"] = 2,
            ["processFiles"] = processFiles.Count,
            ["processRows"] = processRows,
            ["results"] = JsonSerializer.SerializeToNode(input.Results),
            ["sink"] = JsonSerializer.SerializeToNode(input.Sink),
            ["capture"] = JsonSerializer.SerializeToNode(input.Capture),
            ["sourceManifest"] = JsonSerializer.SerializeToNode(input.SourceManifest),
            ["origins"] = JsonSerializer.SerializeToNode(input.Origins),
            ["primaryMembers"] = JsonSerializer.SerializeToNode(input.Members),
            ["notLegacyRawCapture"] = true,
            ["wholeIpcClaim"] = false,
            ["normalHostClaim"] = false,
            ["executionOutcomeCredit"] = 0
        });
    }

    internal static void ValidateClosure(Input input, JsonObject result, JsonObject sink, JsonObject capture)
    {
        bool pilot = input.NativeRoot == TaskRoot + "/unit-wire-pilot61";
        Need(pilot || input.NativeRoot == TaskRoot + "/unit-full-nomix55", "Unreviewed primary producer origin.");
        string assembly = pilot ? "02e598bb2e6c964446f6b4aa1f6bb04b88a0bdfab777ff7e5a3c8340caffb5d6"
            : "275a91548d724ea3231723950617ca10762400bddeb93ef0bc35f4919a1a0908";
        string manifest = pilot ? "5629c0e50e917e32d83e85450de53b970c0e0fa983253d24001e2cc80d46e692"
            : "df5c9e7d2dc4d87605242fe914648eb9251ada1975c55dcad46d76e5d0b54ccf";
        JsonObject native = Item(result["nativeProcess"]);
        Need(result["completeExecution"]?.GetValue<bool>() == true && result["failure"] is null
            && result["complete41SourceScope"]?.GetValue<bool>() == !pilot
            && native["actualExitCode"]?.GetValue<int>() is 0 or 2 && native["timedOut"]?.GetValue<bool>() == false
            && native.ContainsKey("forcedStopReason") && native["forcedStopReason"] is null, "Incomplete/failed native campaign is not eligible.");
        Need(sink["complete"]?.GetValue<bool>() == true && sink["observedEof"]?.GetValue<bool>() == true
            && sink["failure"] is null && Text(sink, "syntheticFinalizationFault") == "None"
            && Integer(sink, "rawPrefixBytesPersisted") == Integer(sink, "rawBytesReceived"), "Primary is not a complete persisted EOF capture.");
        Need(capture["actualExitCode"]?.GetValue<int>() == 0 && capture["sinkForcedAfterTracerClosed"]?.GetValue<bool>() == false
            && capture["failureFlagExists"]?.GetValue<bool>() == false && capture["toolsUnchanged"]?.GetValue<bool>() == true,
            "Producer/sink termination or tool pins are not valid.");
        JsonObject started = Item(capture["started"]);
        Need(Text(started, "controllerAssemblySha256") == assembly && input.SourceManifest.Sha256 == manifest
            && Text(started, "fifo") == input.NativeRoot + "/wire/trace.fifo"
            && Text(started, "dotnet") == TaskRoot + "/dotnet/dotnet"
            && Text(started, "dotnetSha256") == "01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269"
            && Text(started, "straceSha256") == "28f957c227012de0b18d1bd7fff2d396cb693ea60ed8013be68de071e84b5001",
            "Physical producer/tool/source/FIFO binding differs from the reviewed capture.");
    }

    private static Dictionary<int, List<Source>> Scan(Input input, JsonObject sink, JsonObject[] members, out long bytes, out long rows)
    {
        var selected = new Dictionary<int, List<Source>>(); var states = new Dictionary<int, State>();
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var partial = new MemoryStream();
        byte[] buffer = new byte[65536]; long offset = 0, count = 0, compressed = 0, selectedBytes = 0;
        for (int index = 0; index < members.Length; index++)
        {
            JsonObject member = members[index]; Pin pin = input.Members[index]; Check(pin); compressed += pin.Bytes;
            Need(Integer(member, "number") == index + 1 && Integer(member, "rawStart") == offset + partial.Length
                && member["gzipClosed"]?.GetValue<bool>() == true && member["memberFinalizationComplete"]?.GetValue<bool>() == true
                && Array(member, "errors").Count == 0, "Primary member order/range/finalization differs.");
            using var file = File.OpenRead(pin.Path); using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var memberHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long memberBytes = 0;
            for (int length; (length = gzip.Read(buffer)) != 0;)
            {
                memberHash.AppendData(buffer, 0, length); whole.AppendData(buffer, 0, length); memberBytes += length;
                int at = 0;
                while (at < length)
                {
                    int lf = buffer.AsSpan(at, length - at).IndexOf((byte)'\n'); int take = lf < 0 ? length - at : lf;
                    partial.Write(buffer, at, take); at += take; Need(partial.Length <= 67_108_864, "Oversized raw primary record.");
                    if (lf < 0) { continue; }
                    partial.WriteByte((byte)'\n'); byte[] raw = partial.ToArray(); int end = 0;
                    while (end < raw.Length && raw[end] is >= (byte)'0' and <= (byte)'9') { end++; }
                    Need(end is > 0 and < 12 && raw[end] == (byte)' ', "Raw PID prefix missing.");
                    int pid = int.Parse(Encoding.ASCII.GetString(raw, 0, end), NumberStyles.None, CultureInfo.InvariantCulture);
                    Need(pid > 0, "Nonpositive raw PID."); while (end < raw.Length && raw[end] == (byte)' ') { end++; }
                    string body = Utf8.GetString(raw, end, raw.Length - end - 1);
                    if (!states.TryGetValue(pid, out State? state)) { state = new(); states.Add(pid, state); }
                    long local = state.Offset; state.Offset += raw.Length - end;
                    if (state.Observe(body))
                    {
                        selectedBytes += raw.Length; Need(selectedBytes <= 512_000_000, "Process metadata exceeds the supported in-memory bound.");
                        if (!selected.TryGetValue(pid, out List<Source>? list)) { list = []; selected.Add(pid, list); }
                        string perPidHash = Sha(raw.AsSpan(end));
                        list.Add(new(local, raw.Length - end, perPidHash, body,
                            new(offset, raw.Length, Sha(raw), Encoding.ASCII.GetString(raw, 0, end), local, raw.Length - end, perPidHash)));
                    }
                    offset += raw.Length; count++; partial.SetLength(0); at++;
                }
            }
            Need(memberBytes == Integer(member, "rawBytes") && offset + partial.Length == Integer(member, "rawEnd")
                && Hex(memberHash.GetHashAndReset()) == Text(member, "rawSha256"), "Primary decompressed member bytes differ."); Check(pin);
        }
        Need(states.Values.All(state => state.Pending is null && !state.Terminal), "Pending process boundary at complete primary EOF.");
        Need(partial.Length == 0 && offset == Integer(sink, "rawBytesReceived") && count == Integer(sink, "newlineCount")
            && compressed == Integer(sink, "compressedBytes") && Hex(whole.GetHashAndReset()) == Text(sink, "rawSha256Received"),
            "Whole primary byte/line/hash inventory differs or ends in a partial record.");
        bytes = offset; rows = count; return selected;
    }

    private sealed class State
    {
        internal decimal Previous = -1;
        internal string? Pending;
        internal bool Terminal;
        internal bool Ended;
        internal long Offset;
        internal bool Observe(string text)
        {
            Match record = Regex.Match(text, @"^(\d+(?:\.\d+)?) (.*)$", RegexOptions.CultureInvariant);
            Need(record.Success, "Malformed timestamped primary record.");
            Need(!Ended, "Raw content follows the PID terminal.");
            decimal time = decimal.Parse(record.Groups[1].Value, CultureInfo.InvariantCulture); string body = record.Groups[2].Value;
            Need(time >= Previous, "Raw per-PID clock reversed."); Previous = time;
            bool terminal = Regex.IsMatch(body, @"^\+\+\+ (?:exited with -?\d+|killed by .*) \+\+\+$", RegexOptions.CultureInvariant);
            Ended = terminal;
            if (Pending is { } name)
            {
                bool resumed = body.StartsWith("<... " + name + " resumed>", StringComparison.Ordinal);
                Need(resumed || name is "exit" or "exit_group" && terminal, "Intervening primary activity before process return.");
                Pending = null; Terminal = resumed && name is "exit" or "exit_group";
            }
            else if (Terminal) { Need(terminal, "Activity after a nonreturning process operation."); Terminal = false; }
            Match operation = Regex.Match(body, @"^(execve|execveat|exit|exit_group)\(", RegexOptions.CultureInvariant);
            if (operation.Success && body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { Pending = operation.Groups[1].Value; }
            return Regex.IsMatch(body, @"^(?:(?:clone3?|fork|vfork|execve|execveat|exit|exit_group)\(|<\.\.\. (?:clone3?|fork|vfork|execve|execveat|exit|exit_group) resumed>|\+\+\+ )", RegexOptions.CultureInvariant);
        }
    }

    private static IEnumerable<Line> Derive(List<Source> raw)
    {
        Source? pendingExec = null, pendingExit = null; string? execKind = null, exitKind = null; bool terminalRequired = false;
        foreach (Source source in raw)
        {
            Match record = Regex.Match(source.Text, @"^(\d+(?:\.\d+)?) (.*)$", RegexOptions.CultureInvariant);
            Need(record.Success, "Invalid selected raw process framing."); string time = record.Groups[1].Value, body = record.Groups[2].Value;
            if (pendingExit is not null)
            {
                if (body == "<... " + exitKind + " resumed>) = ?")
                {
                    Match first = Regex.Match(pendingExit.Text, @"^(\d+(?:\.\d+)?) ((?:exit|exit_group)\(-?\d+) <unfinished \.\.\.>$", RegexOptions.CultureInvariant);
                    Need(first.Success, "Malformed nonreturning process entry.");
                    yield return new(first.Groups[1].Value + " " + first.Groups[2].Value + ") = ?", [pendingExit, source], "exact-paired-nonreturning-" + exitKind + "-observation");
                    pendingExit = null; exitKind = null; terminalRequired = true; continue;
                }
                Need(IsTerminal(body), "Unfinished process exit has intervening selected activity.");
                yield return new(pendingExit.Text, [pendingExit], "exact-selected-process-record"); pendingExit = null;
            }
            if (terminalRequired) { Need(IsTerminal(body), "Resumed nonreturning exit lacks its terminal."); terminalRequired = false; }
            if (pendingExec is not null)
            {
                Match resumed = Regex.Match(body, @"^<\.\.\. (execve|execveat) resumed>(.*)$", RegexOptions.CultureInvariant);
                Need(resumed.Success && resumed.Groups[1].Value == execKind, "Unbound or wrong-kind exec resume.");
                Match first = Regex.Match(pendingExec.Text, @"^(\d+(?:\.\d+)?) (.*)$", RegexOptions.CultureInvariant);
                string start = first.Groups[2].Value;
                string joined = start[..start.IndexOf(" <unfinished ...>", StringComparison.Ordinal)] + resumed.Groups[2].Value;
                Need(!joined.Contains("<unfinished", StringComparison.Ordinal), "Repeated unfinished exec.");
                yield return new(first.Groups[1].Value + " " + NormalizeExec(joined), [pendingExec, source], "exact-paired-exec-return-and-hex-quoted-bytes");
                pendingExec = null; execKind = null; continue;
            }
            Match exec = Regex.Match(body, @"^(execve|execveat)\(", RegexOptions.CultureInvariant);
            if (exec.Success)
            {
                if (body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { pendingExec = source; execKind = exec.Groups[1].Value; continue; }
                yield return new(time + " " + NormalizeExec(body), [source], "complete-exec-hex-quoted-bytes"); continue;
            }
            Need(!Regex.IsMatch(body, @"^<\.\.\. execve(?:at)? resumed>", RegexOptions.CultureInvariant), "Orphan exec resume.");
            if ((body.StartsWith("exit(", StringComparison.Ordinal) || body.StartsWith("exit_group(", StringComparison.Ordinal)) && body.EndsWith(" <unfinished ...>", StringComparison.Ordinal))
            { pendingExit = source; exitKind = body[..body.IndexOf('(')]; continue; }
            yield return new(source.Text, [source], "exact-selected-process-record");
        }
        Need(pendingExec is null && pendingExit is null && !terminalRequired, "Incomplete selected process operation.");
    }

    private static string NormalizeExec(string body)
    {
        Need(!body.StartsWith("execveat(", StringComparison.Ordinal) || !body.EndsWith(" = 0", StringComparison.Ordinal), "Successful execveat is unsupported.");
        var output = new StringBuilder(); int at = 0;
        while (at < body.Length)
        {
            if (body[at] != '"') { output.Append(body[at++]); continue; }
            at++; using var bytes = new MemoryStream();
            while (at < body.Length && body[at] != '"')
            {
                Need(at + 3 < body.Length && body[at] == '\\' && body[at + 1] == 'x' && Uri.IsHexDigit(body[at + 2]) && Uri.IsHexDigit(body[at + 3]), "Incomplete/nonhex exec bytes.");
                bytes.WriteByte(byte.Parse(body.AsSpan(at + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); at += 4;
            }
            Need(at < body.Length && body[at++] == '"', "Unterminated exec bytes."); output.Append(JsonSerializer.Serialize(Utf8.GetString(bytes.ToArray())));
        }
        Need(!Regex.IsMatch(output.ToString(), @"\[[^\]]*\.\.\.", RegexOptions.CultureInvariant), "Abbreviated exec argv."); return output.ToString();
    }

    private static bool IsTerminal(string body) => Regex.IsMatch(body, @"^\+\+\+ (?:exited with -?\d+|killed by .*) \+\+\+$", RegexOptions.CultureInvariant);
    private static int PidName(string path)
    {
        string name = Path.GetFileName(path); const string prefix = "actual-process.";
        Need(name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int _), "Invalid derived process file name.");
        int pid = int.Parse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture); Need(pid > 0, "Nonpositive derived PID."); return pid;
    }
    private static void Check(Pin pin)
    {
        Need(pin.Bytes >= 0 && pin.Sha256.Length == 64 && pin.Sha256.All(value => char.IsAsciiDigit(value) || value is >= 'a' and <= 'f'), "Invalid physical process-origin pin.");
        for (string? item = Path.GetFullPath(pin.Path); item is not null; item = Path.GetDirectoryName(item))
        { Need(!File.GetAttributes(item).HasFlag(FileAttributes.ReparsePoint), "Linked process-origin input rejected."); }
        using var file = File.OpenRead(pin.Path); Need(file.Length == pin.Bytes && Hex(SHA256.HashData(file)) == pin.Sha256, "Changed/missing process-origin physical input.");
    }
    private static JsonObject Document(Pin pin)
    {
        byte[] bytes = File.ReadAllBytes(pin.Path); using JsonDocument document = JsonDocument.Parse(bytes); Unique(document.RootElement);
        return JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Process-origin JSON object required.");
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject()) { Need(keys.Add(property.Name), "Duplicate process-origin JSON property."); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) { foreach (JsonElement child in element.EnumerateArray()) { Unique(child); } }
    }
    private static JsonObject Item(JsonNode? node) => node as JsonObject ?? throw new InvalidDataException("Process-origin JSON object required.");
    private static JsonArray Array(JsonObject obj, string field) => obj[field] as JsonArray ?? throw new InvalidDataException("Process-origin array required: " + field);
    private static string Text(JsonObject obj, string field) => obj[field]?.GetValue<string>() ?? throw new InvalidDataException("Process-origin string required: " + field);
    private static long Integer(JsonObject obj, string field) => obj[field]?.GetValue<long>() ?? throw new InvalidDataException("Process-origin integer required: " + field);
    private static string Sha(ReadOnlySpan<byte> bytes) => Hex(SHA256.HashData(bytes));
    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
    private static void Need([DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
