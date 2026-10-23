using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ProbeControls
{
    internal static JsonArray Run(IReadOnlyDictionary<int, EpochValidation.Creation> creations, List<EpochValidation.Call> actual)
    {
        var results = new JsonArray();
        void Positive(string name, Action body) { body(); results.Add(new JsonObject { ["name"] = name, ["expected"] = "accept", ["passed"] = true }); }
        void Negative(string name, Action body, string reason)
        {
            try { body(); throw new InvalidOperationException("Control unexpectedly accepted: " + name); }
            catch (Exception error) when (error is InvalidDataException or JsonException or DecoderFallbackException)
            {
                if (reason.Length > 0 && !error.Message.Contains(reason, StringComparison.Ordinal)) { throw new InvalidDataException("Wrong rejection boundary for " + name + ":" + error.Message); }
                results.Add(new JsonObject { ["name"] = name, ["expected"] = "reject", ["passed"] = true, ["actualErrorType"] = error.GetType().FullName, ["actualReason"] = error.Message });
            }
        }
        const string send = "sendto(80<TCPv6:[fixed]>, \"\\x01\\x02\\x03\", 3, 0, NULL, 0) = 2";
        const string receive = "recvmsg(80<TCPv6:[fixed]>, {msg_name=NULL, msg_namelen=0, msg_iov=[{iov_base=\"\\x01\\x02\", iov_len=3}], msg_iovlen=1, msg_controllen=0, msg_flags=0}, 0) = 2";
        Positive("partial-send-retains-only-actual-return-prefix", () => Need(WireBytes.Transfer(send).SequenceEqual(new byte[] { 1, 2 })));
        Positive("partial-receive-retains-only-actual-returned-buffer", () => Need(WireBytes.Transfer(receive).SequenceEqual(new byte[] { 1, 2 })));
        Negative("send-return-exceeds-buffer", () => WireBytes.Transfer(send.Replace(") = 2", ") = 4", StringComparison.Ordinal)), "prefix");
        Negative("abbreviated-send-buffer", () => WireBytes.Transfer(send.Replace("\\x03\"", "\\x03\"...", StringComparison.Ordinal)), "unsupported");
        Negative("send-nonzero-flags", () => WireBytes.Transfer(send.Replace(", 0, NULL", ", MSG_PEEK, NULL", StringComparison.Ordinal)), "unsupported");
        Negative("recv-captured-bytes-missing", () => WireBytes.Transfer(receive.Replace("\\x01\\x02", "\\x01", StringComparison.Ordinal)), "incomplete");
        Negative("recv-peek-not-consumed", () => WireBytes.Transfer(receive.Replace("}, 0) =", "}, MSG_PEEK) =", StringComparison.Ordinal)), "unsupported");
        Negative("recv-truncation-flag", () => WireBytes.Transfer(receive.Replace("msg_flags=0", "msg_flags=MSG_TRUNC", StringComparison.Ordinal)), "unsupported");
        Positive("canonical-multibyte-frame", () => Need(WireBytes.Frames(Frame("{\"MessageType\":\"Synthetic\",\"Payload\":\"" + new string('a', 200) + "\"}")).Length == 1));
        Negative("unterminated-frame-prefix", () => WireBytes.Frames([0x80]), "prefix");
        Negative("overlong-frame-prefix", () => WireBytes.Frames([0x81, 0]), "Noncanonical");
        Negative("overflowing-frame-prefix", () => WireBytes.Frames([0xff, 0xff, 0xff, 0xff, 0x08]), "Int32");
        Negative("zero-length-frame", () => WireBytes.Frames([0]), "incomplete");
        Negative("short-message-frame", () => WireBytes.Frames([3, (byte)'{', (byte)'}']), "incomplete");
        Negative("invalid-utf8-frame", () => WireBytes.Frames([1, 0xff]), "");
        Negative("invalid-json-frame", () => WireBytes.Frames(Frame("{broken")), "");
        Negative("untyped-json-frame", () => WireBytes.Frames(Frame("{\"Payload\":null}")), "Typed");
        Negative("duplicate-payload-property", () => WireBytes.Frames(Frame("{\"MessageType\":\"Synthetic\",\"Payload\":1,\"Payload\":2}")), "Duplicate");
        Negative("trailing-incomplete-second-frame", () => WireBytes.Frames(Frame("{\"MessageType\":\"Synthetic\"}").Concat(new byte[] { 0x80 }).ToArray()), "prefix");

        Positive("actual-fixed-epoch", () => EpochValidation.Validate(creations, actual));
        EpochValidation.Call transfer = actual.First(x => x.Name == "sendto" && x.Body.Contains("45063->127.0.0.1:54454", StringComparison.Ordinal));
        int at = actual.IndexOf(transfer);
        List<EpochValidation.Call> Inject(params EpochValidation.Call[] extra) { var copy = new List<EpochValidation.Call>(actual); copy.InsertRange(at + 1, extra); return copy; }
        EpochValidation.Call Synthetic(string name, string body) => transfer with { Name = name, Body = body, RawSha256 = "synthetic-control-not-native-evidence" };
        Negative("missing-host-connect", () => EpochValidation.Validate(creations, actual.Where(x => x.Name != "connect").ToArray()), "precedes connect");
        Negative("missing-controller-accept", () => EpochValidation.Validate(creations, actual.Where(x => x.Name != "accept4").ToArray()), "epoch");
        Negative("missing-original-host-close", () => EpochValidation.Validate(creations, actual.Where(x => !(x.Name == "close" && x.Body.StartsWith("close(80<TCP", StringComparison.Ordinal))).ToArray()), "incomplete");
        Negative("unowned-thread-cannot-transfer", () => EpochValidation.Validate(creations, actual.Select(x => x == transfer ? x with { Tid = 2_000_000 } : x).ToArray()), "fd epoch");
        Negative("raw-positive-write-on-target", () => EpochValidation.Validate(creations, Inject(Synthetic("write", "write(0x9f, 0x1234, 0x1) = 0x1"))), "no captured payload");
        Positive("raw-zero-write-does-not-create-payload", () => EpochValidation.Validate(creations, Inject(Synthetic("write", "write(0x9f, 0x1234, 0x1) = 0"))));
        Negative("positive-zero-copy-target-as-source", () => EpochValidation.Validate(creations, Inject(Synthetic("sendfile", "sendfile(0x1, 0x9f, 0, 0x1) = 0x1"))), "no captured payload");
        Negative("positive-io-uring-setup", () => EpochValidation.Validate(creations, Inject(Synthetic("io_uring_setup", "io_uring_setup(0x1, 0x1234) = 0x64"))), "io_uring");
        Negative("positive-unsupported-network-send", () => EpochValidation.Validate(creations, Inject(Synthetic("sendmsg", "sendmsg(159<TCP:[127.0.0.1:45063->127.0.0.1:54454]>, 0x1234, 0) = 1"))), "unsupported");
        Negative("closed-fd-reuse-does-not-inherit-epoch", () => EpochValidation.Validate(creations, Inject(Synthetic("close", "close(159<TCP:[127.0.0.1:45063->127.0.0.1:54454]>) = 0"))), "fd epoch");
        Negative("raw-unreturned-target-call", () => EpochValidation.Validate(creations, Inject(Synthetic("read", "read(0x9f, 0x1234, 0x1 <unfinished ...> @UNRETURNED"))), "unreturned");
        Positive("dup-alias-tracked-and-closed", () => EpochValidation.Validate(creations, Inject(Synthetic("dup", "dup(159<TCP:[127.0.0.1:45063->127.0.0.1:54454]>) = 200<TCP:[127.0.0.1:45063->127.0.0.1:54454]>"), Synthetic("close", "close(200<TCP:[127.0.0.1:45063->127.0.0.1:54454]>) = 0"))));
        Negative("dup-alias-raw-write-detected", () => EpochValidation.Validate(creations, Inject(Synthetic("dup", "dup(159<TCP:[127.0.0.1:45063->127.0.0.1:54454]>) = 200<TCP:[127.0.0.1:45063->127.0.0.1:54454]>"), Synthetic("write", "write(0xc8, 0x1234, 0x1) = 0x1"))), "no captured payload");
        var wrongCreator = creations.ToDictionary(x => x.Key, x => x.Value); wrongCreator[1029071] = wrongCreator[1029071] with { Parent = 2_000_000 };
        Negative("wrong-host-creator", () => EpochValidation.Validate(wrongCreator, actual), "creator");
        return results;
    }
    private static byte[] Frame(string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text); using var result = new MemoryStream(); uint n = (uint)body.Length;
        while (n >= 128) { result.WriteByte((byte)(n | 128)); n >>= 7; } result.WriteByte((byte)n); result.Write(body); return result.ToArray();
    }
    private static void Need(bool value) { if (!value) { throw new InvalidOperationException("Positive control failed."); } }
}
