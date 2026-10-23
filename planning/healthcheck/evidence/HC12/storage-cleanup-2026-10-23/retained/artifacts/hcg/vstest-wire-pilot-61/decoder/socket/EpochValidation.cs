using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// One fixed pilot socket epoch. No other endpoint or campaign is admitted by this probe.
internal static class EpochValidation
{
    internal sealed record Call(int Tid, string Name, string Body, int FirstLine, int LastLine, long RawStart, string RawSha256);
    internal sealed record Creation(int Child, int Parent, bool Thread, bool SharedFiles, Call Source);

    internal static IReadOnlyDictionary<int, Creation> ReadCreations(string source)
    {
        JsonObject sink = JsonNode.Parse(File.ReadAllBytes(Path.Combine(source, "wire/sink-result.json")))!.AsObject();
        var pending = new Dictionary<int, (int Line, long Offset, string Body, string Hash)>();
        var creations = new Dictionary<int, Creation>();
        long offset = 0; int line = 0;
        using var joined = new JoinedGzip(sink["members"]!.AsArray().OfType<JsonObject>().Select(x => Path.Combine(source, "wire", Path.GetFileName((string)x["path"]!))).ToArray());
        using var reader = new StreamReader(joined, new UTF8Encoding(false, true), false, 65536);
        while (reader.ReadLine() is { } text)
        {
            line++; long here = offset; offset += Encoding.UTF8.GetByteCount(text) + 1;
            Match prefix = Regex.Match(text, @"^(\d+) +\d+\.\d+ (.*)$", RegexOptions.CultureInvariant);
            Need(prefix.Success, "Creation raw prefix absent.");
            int tid = int.Parse(prefix.Groups[1].Value, CultureInfo.InvariantCulture); string body = prefix.Groups[2].Value;
            int firstLine = line; long firstOffset = here; string firstHash = HashText(text);
            Match resume = Regex.Match(body, @"^<\.\.\. (clone3?|fork|vfork) resumed>(.*)$", RegexOptions.CultureInvariant);
            if (resume.Success)
            {
                Need(pending.Remove(tid, out var first), "Unbound creation resume.");
                Need(first.Body.StartsWith(resume.Groups[1].Value + "(", StringComparison.Ordinal), "Creation resume kind differs.");
                body = first.Body[..first.Body.IndexOf(" <unfinished ...>", StringComparison.Ordinal)] + resume.Groups[2].Value;
                firstLine = first.Line; firstOffset = first.Offset; firstHash = first.Hash;
            }
            else if (!Regex.IsMatch(body, @"^(clone3?|fork|vfork)\(", RegexOptions.CultureInvariant)) { continue; }
            if (body.EndsWith(" <unfinished ...>", StringComparison.Ordinal))
            {
                Need(pending.TryAdd(tid, (line, here, body, firstHash)), "Duplicate pending creation."); continue;
            }
            Match result = Regex.Match(body, @"\) = (-?\d+)(?: .*)?$", RegexOptions.CultureInvariant); Need(result.Success, "Creation return unsupported.");
            int child = int.Parse(result.Groups[1].Value, CultureInfo.InvariantCulture); if (child < 0) { continue; }
            Need(child > 0 && child != tid, "Invalid creation child.");
            string name = body[..body.IndexOf('(')];
            Need(creations.TryAdd(child, new Creation(child, tid, body.Contains("CLONE_THREAD", StringComparison.Ordinal), body.Contains("CLONE_FILES", StringComparison.Ordinal), new Call(tid, name, body, firstLine, line, firstOffset, firstHash))), "Duplicate child identity.");
        }
        Need(offset == 552913184 && pending.Count == 0, "Whole fixed primary creation input incomplete.");
        return creations;
    }

    internal static List<Call> ReadWindow(string input)
    {
        var pending = new Dictionary<int, (string Name, string Body, int Line, long Offset, string Hash)>();
        var calls = new List<Call>(); int line = 0; long offset = 0;
        foreach (string text in File.ReadLines(input, new UTF8Encoding(false, true)))
        {
            line++; long here = offset; offset += Encoding.UTF8.GetByteCount(text) + 1;
            Match prefix = Regex.Match(text, @"^(\d+) +\d+\.\d+ (.*)$", RegexOptions.CultureInvariant); Need(prefix.Success, "Window prefix absent.");
            int tid = int.Parse(prefix.Groups[1].Value, CultureInfo.InvariantCulture); string body = prefix.Groups[2].Value;
            Match resume = Regex.Match(body, @"^<\.\.\. (\w+) resumed>(.*)$", RegexOptions.CultureInvariant);
            if (resume.Success)
            {
                if (!pending.Remove(tid, out var first)) { continue; } // Window starts mid-call; fixed target socket opens later.
                Need(first.Name == resume.Groups[1].Value, "Window syscall resume differs.");
                body = first.Body[..first.Body.IndexOf(" <unfinished ...>", StringComparison.Ordinal)] + resume.Groups[2].Value;
                calls.Add(new Call(tid, first.Name, body, first.Line, line, first.Offset, first.Hash)); continue;
            }
            Match kind = Regex.Match(body, @"^(\w+)\(", RegexOptions.CultureInvariant); if (!kind.Success) { continue; }
            string name = kind.Groups[1].Value;
            if (body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { Need(pending.TryAdd(tid, (name, body, line, here, HashText(text))), "Duplicate pending window call."); continue; }
            calls.Add(new Call(tid, name, body, line, line, here, HashText(text)));
        }
        Need(!pending.Values.Any(x => x.Body.Contains("45063", StringComparison.Ordinal) || x.Body.Contains("54454", StringComparison.Ordinal)), "Target window transfer remains unfinished.");
        foreach ((int tid, var value) in pending) { calls.Add(new Call(tid, value.Name, value.Body + " @UNRETURNED", value.Line, line, value.Offset, value.Hash)); }
        return calls;
    }

    internal static JsonObject Validate(IReadOnlyDictionary<int, Creation> creations, IReadOnlyList<Call> calls)
    {
        int Leader(int tid, int depth = 0)
        {
            Need(depth < 100, "Creation ancestry cycle.");
            return creations.TryGetValue(tid, out Creation? entry) && entry.Thread ? Leader(entry.Parent, depth + 1) : tid;
        }
        int Table(int tid, int depth = 0)
        {
            Need(depth < 100, "File-table ancestry cycle.");
            return creations.TryGetValue(tid, out Creation? entry) && entry.SharedFiles ? Table(entry.Parent, depth + 1) : tid;
        }
        Need(creations.TryGetValue(1029071, out Creation? host) && Leader(host.Parent) == 1028149 && !host.Thread, "Exact target host launch creator differs.");
        int controllerTable = Table(1028660), hostTable = Table(1029071);
        Need(controllerTable != hostTable && Leader(1028660) == 1028660 && Leader(1029071) == 1029071, "Target process/file-table identities differ.");
        var aliases = new Dictionary<(int Table, int Fd), string>();
        bool listenerSocket = false, listenerBound = false, listenerListening = false, hostSocket = false, hostConnect = false, hostSoError = false;
        int accepted = 0, closeHost = 0, closeController = 0, successfulTransfers = 0, rawZeroTransfers = 0;
        var evidence = new JsonArray();
        foreach (Call call in calls)
        {
            int table = Table(call.Tid); string body = call.Body;
            Match result = Regex.Match(body, @"\) = (0x[0-9a-f]+|-?\d+)(?:<[^\r\n]*>| [^\r\n]*)?$", RegexOptions.CultureInvariant);
            if (!result.Success)
            {
                Match unreturned = Regex.Match(body, @"^\w+\((0x[0-9a-f]+|\d+)(?:<.*?>)?(?:,|\))", RegexOptions.CultureInvariant);
                Need(!body.EndsWith("@UNRETURNED", StringComparison.Ordinal) || !unreturned.Success || !aliases.ContainsKey((table, checked((int)Number(unreturned.Groups[1].Value)))), "Target descriptor has an unreturned syscall.");
                continue;
            }
            long returned = Number(result.Groups[1].Value);
            Match firstFd = Regex.Match(body, @"^\w+\((0x[0-9a-f]+|\d+)(?:<.*?>)?(?:,|\))", RegexOptions.CultureInvariant);
            long firstNumber = firstFd.Success ? Number(firstFd.Groups[1].Value) : -1;
            int fd = firstNumber is >= 0 and <= int.MaxValue ? (int)firstNumber : -1;
            bool target = aliases.TryGetValue((table, fd), out string? side);
            bool annotation = body.Contains("45063->127.0.0.1:54454", StringComparison.Ordinal) || body.Contains("54454->[::ffff:127.0.0.1]:45063", StringComparison.Ordinal);
            if (call.Name == "socket" && returned >= 0)
            {
                if (table == controllerTable && returned == 158 && body.Contains("AF_INET, SOCK_STREAM|SOCK_CLOEXEC, IPPROTO_TCP", StringComparison.Ordinal)) { Need(!listenerSocket, "Listener fd epoch reopened."); listenerSocket = true; evidence.Add(Source("listenerSocket", call)); }
                if (table == hostTable && returned == 80 && body.Contains("AF_INET6, SOCK_STREAM|SOCK_CLOEXEC, IPPROTO_TCP", StringComparison.Ordinal)) { Need(!hostSocket, "Host fd epoch reopened."); hostSocket = true; evidence.Add(Source("hostSocket", call)); }
            }
            if (table == controllerTable && fd == 158 && returned == 0 && call.Name == "bind") { Need(listenerSocket && body.Contains("htons(0)", StringComparison.Ordinal), "Listener bind origin differs."); listenerBound = true; }
            if (table == controllerTable && fd == 158 && returned == 0 && call.Name == "listen") { Need(listenerBound && body.Contains("127.0.0.1:45063", StringComparison.Ordinal), "Listener listen epoch differs."); listenerListening = true; }
            if (table == hostTable && fd == 80 && call.Name == "connect")
            {
                Need(hostSocket && !hostConnect && body.Contains("sin6_port=htons(45063)", StringComparison.Ordinal) && body.Contains("\\x3a\\x3a\\x66\\x66\\x66\\x66\\x3a\\x31\\x32\\x37\\x2e\\x30\\x2e\\x30\\x2e\\x31", StringComparison.Ordinal) && (returned == 0 || returned == -1 && body.EndsWith("EINPROGRESS (Operation now in progress)", StringComparison.Ordinal)), "Host connect epoch differs.");
                hostConnect = true; aliases.Add((hostTable, 80), "host"); evidence.Add(Source("hostConnect", call));
            }
            if (table == controllerTable && fd == 158 && call.Name == "accept4" && returned >= 0 && annotation)
            {
                Need(listenerListening && returned == 159 && ++accepted == 1 && body.Contains("sin_port=htons(54454)", StringComparison.Ordinal), "Target accept epoch differs.");
                aliases.Add((controllerTable, 159), "controller"); evidence.Add(Source("controllerAccept", call));
            }
            if (table == hostTable && fd == 80 && call.Name == "getsockopt" && returned == 0 && body.Contains("SOL_SOCKET, SO_ERROR, [0]", StringComparison.Ordinal)) { Need(hostConnect, "Connection completion precedes connect."); hostSoError = true; evidence.Add(Source("hostConnected", call)); }
            if (annotation && call.Name is "sendto" or "recvmsg") { Need(target, "Endpoint transfer has no proven fd epoch."); }
            if (call.Name.StartsWith("io_uring_", StringComparison.Ordinal) && returned >= 0) { throw new InvalidDataException("Successful io_uring is unsupported by the bounded capture decoder."); }
            if (aliases.Count > 0 && body.Contains("SCM_RIGHTS", StringComparison.Ordinal) && returned > 0) { throw new InvalidDataException("Descriptor transfer while the target epoch is active is unsupported."); }
            if (aliases.Count > 0 && call.Name is "clone" or "clone3" or "fork" or "vfork" && returned > 0 && creations.TryGetValue(checked((int)returned), out Creation? child) && !child.SharedFiles) { throw new InvalidDataException("A non-shared child may inherit an active target descriptor."); }
            if (call.Name is "dup" or "dup2" or "dup3" && returned >= 0)
            {
                if (target) { aliases[(table, checked((int)returned))] = side!; }
                else if (aliases.ContainsKey((table, checked((int)returned)))) { throw new InvalidDataException("Target descriptor overwritten by dup."); }
            }
            if (target && call.Name == "fcntl" && returned >= 0 && body.Contains("F_DUPFD", StringComparison.Ordinal)) { aliases[(table, checked((int)returned))] = side!; }
            if (call.Name == "close_range" && returned == 0 && aliases.Keys.Any(x => x.Table == table)) { throw new InvalidDataException("close_range during target epoch is unsupported."); }
            if (returned > 0 && call.Name is "sendfile" or "splice" or "tee" or "copy_file_range")
            {
                string[] arguments = body[(body.IndexOf('(') + 1)..body.LastIndexOf(')')].Split(',');
                int[] positions = call.Name is "splice" or "copy_file_range" ? [0, 2] : [0, 1];
                foreach (int position in positions)
                {
                    Need(arguments.Length > position && Regex.IsMatch(arguments[position].Trim(), @"^(?:0x[0-9a-f]+|\d+)$", RegexOptions.CultureInvariant), "Raw zero-copy descriptor cannot be decoded.");
                    Need(!aliases.ContainsKey((table, checked((int)Number(arguments[position].Trim())))), "Positive zero-copy source/destination has no captured payload.");
                }
            }
            if (!target) { continue; }
            if (call.Name is "read" or "readv" or "write" or "writev" or "pread64" or "preadv" or "preadv2" or "pwrite64" or "pwritev" or "pwritev2" or "sendfile" or "splice" or "tee" or "vmsplice" or "copy_file_range")
            {
                Need(returned <= 0, "Positive raw/zero-copy target transfer has no captured payload."); rawZeroTransfers++; continue;
            }
            if (call.Name is "sendto" or "recvmsg") { if (returned > 0) { Need(hostSoError && accepted == 1, "Positive target transfer precedes connected epoch."); successfulTransfers++; } continue; }
            if (call.Name is "recvfrom" or "sendmsg" or "sendmmsg" or "recvmmsg") { Need(returned <= 0, "Positive target transfer syscall is unsupported."); }
            if (call.Name == "close" && returned == 0)
            {
                aliases.Remove((table, fd));
                if (side == "host" && fd == 80) { closeHost++; }
                if (side == "controller" && fd == 159) { closeController++; }
                evidence.Add(Source(side + "Closed", call));
            }
        }
        Need(listenerSocket && listenerBound && listenerListening && hostSocket && hostConnect && hostSoError && accepted == 1 && closeHost == 1 && closeController == 1 && aliases.Count == 0 && successfulTransfers > 0, "Target socket epoch incomplete.");
        return new JsonObject { ["fixedConnectionEpochValidated"] = true, ["controllerTable"] = controllerTable, ["hostTable"] = hostTable, ["creationsInWholePrimary"] = creations.Count,
            ["successfulPayloadTransferCalls"] = successfulTransfers, ["nonpositiveRawTargetTransfers"] = rawZeroTransfers, ["remainingTargetAliases"] = aliases.Count, ["evidence"] = evidence,
            ["scope"] = "Only this fixed accepted TCP connection; ancestry comes from the entire closed primary. No whole-capture or qualification claim." };
    }

    private static JsonObject Source(string role, Call call) => new() { ["role"] = role, ["tid"] = call.Tid, ["firstLine"] = call.FirstLine, ["lastLine"] = call.LastLine, ["windowRawStart"] = call.RawStart, ["rawFirstLineSha256"] = call.RawSha256 };
    private static long Number(string value) => value.StartsWith("0x", StringComparison.Ordinal) ? long.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : long.Parse(value, CultureInfo.InvariantCulture);
    private static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
    private sealed class JoinedGzip(string[] paths) : Stream
    {
        private int next; private Stream? file, gzip;
        public override int Read(byte[] bytes, int offset, int count)
        {
            while (true)
            {
                if (gzip is null) { if (next == paths.Length) { return 0; } file = File.OpenRead(paths[next++]); gzip = new GZipStream(file, CompressionMode.Decompress); }
                int read = gzip.Read(bytes, offset, count); if (read != 0) { return read; } gzip.Dispose(); file!.Dispose(); gzip = null;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { gzip?.Dispose(); file?.Dispose(); } base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); } public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] bytes, int offset, int count) => throw new NotSupportedException();
    }
}
