using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Projection = CIS.Verification.HealthcheckMutationNativeProcessProjection;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationNativeProcessProjectionTests
{
    [TestMethod]
    public void SplitGzipRecordsRegenerateTheExactProcessBytesAndBothOrigins()
    {
        using var fixture = new Fixture();
        Projection.OriginValidation proof = fixture.Verify();
        Assert.AreEqual(2, (int)proof.Observation["verificationPasses"]!);
        Assert.AreEqual(2, proof.ProcessFiles.Length);
        Assert.AreEqual(2, proof.PrimaryFiles.Length);
        Assert.IsFalse((bool)proof.Observation["wholeIpcClaim"]!);
        Assert.IsFalse((bool)proof.Observation["normalHostClaim"]!);
        Assert.AreEqual(0, (int)proof.Observation["executionOutcomeCredit"]!);
    }

    [TestMethod]
    [DataRow("member-number")]
    [DataRow("member-range")]
    [DataRow("member-errors")]
    [DataRow("member-unclosed")]
    [DataRow("compressed-pin")]
    [DataRow("raw-hash")]
    [DataRow("whole-hash")]
    [DataRow("newline-count")]
    [DataRow("unlisted-member")]
    [DataRow("swapped-members")]
    public void ChangedOrIncompletePrimaryCannotSupplyAProcessView(string change)
    {
        using var fixture = new Fixture();
        fixture.Change(change);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Verify());
    }

    [TestMethod]
    [DataRow("changed-derived-bytes")]
    [DataRow("changed-origin-bytes")]
    [DataRow("missing-pid")]
    [DataRow("extra-pid")]
    public void RepinningChangedDerivedEvidenceDoesNotBypassSourceRegeneration(string change)
    {
        using var fixture = new Fixture();
        fixture.Change(change);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Verify());
    }

    [TestMethod]
    [DataRow("out-of-order")]
    [DataRow("wrong-resume")]
    [DataRow("intervening-exit-activity")]
    [DataRow("after-terminal")]
    [DataRow("bad-utf8")]
    [DataRow("partial-eof")]
    public void RawFramingFailuresRemainFailuresEvenWhenPhysicalPinsAreFresh(string change)
    {
        using var fixture = new Fixture(change);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Verify());
    }

    [TestMethod]
    public void ReviewedClosedPilotProducerIsEligibleOnlyForTheCapturePrimitive()
    {
        var values = Closure();
        Projection.ValidateClosure(values.Input, values.Result, values.Sink, values.Capture);
        Assert.IsFalse((bool)values.Result["complete41SourceScope"]!);
    }

    [TestMethod]
    public void AByteOnlyPrimitiveCannotBePassedAsClosedProducerEvidence()
    {
        using var fixture = new Fixture(); Projection.OriginValidation bytesOnly = fixture.Verify();
        var entry = typeof(HealthcheckMutationNativeKernelEvidence).GetMethod("ReadProjected", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.ThrowsExactly<ArgumentException>(() => entry.Invoke(null, [bytesOnly, null, "unused", "unused"]));
        Assert.HasCount(0, typeof(Projection.Validation).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        Assert.IsFalse(typeof(Projection.Validation).IsAssignableFrom(typeof(Projection.OriginValidation)));
    }

    [TestMethod]
    public void FullReadRejectsDuplicateJsonBeforeInspectingTheFixedSourceProfile()
    {
        using var fixture = new Fixture();
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => fixture.FullRead("{\"completeExecution\":true,\"completeExecution\":false}"));
        StringAssert.Contains(error.Message, "Duplicate process-origin JSON property");
    }

    [TestMethod]
    public void FullReadRejectsAnOversizedDeclaredReceiptBeforeOpeningIt()
    {
        using var fixture = new Fixture();
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => fixture.FullRead("{}", oversized: true));
        StringAssert.Contains(error.Message, "metadata exceeds supported bounds");
    }

    [TestMethod]
    [DataRow("incomplete")]
    [DataRow("native-nonzero")]
    [DataRow("native-timeout")]
    [DataRow("native-forced")]
    [DataRow("producer-failure")]
    [DataRow("eof-missing")]
    [DataRow("sink-forced")]
    [DataRow("sink-incomplete")]
    [DataRow("tool-changed")]
    [DataRow("wrong-producer")]
    [DataRow("wrong-source")]
    [DataRow("wrong-native-root")]
    [DataRow("wrong-fifo")]
    public void AnIncompleteOrForeignCampaignIsIneligibleRegardlessOfDerivedHashes(string change)
    {
        var values = Closure(); Projection.Input input = values.Input;
        switch (change)
        {
            case "incomplete": values.Result["completeExecution"] = false; break;
            case "native-nonzero": values.Result["nativeProcess"]!["actualExitCode"] = 130; break;
            case "native-timeout": values.Result["nativeProcess"]!["timedOut"] = true; break;
            case "native-forced": values.Result["nativeProcess"]!["forcedStopReason"] = "guard"; break;
            case "producer-failure": values.Result["failure"] = "guard"; break;
            case "eof-missing": values.Sink["observedEof"] = false; break;
            case "sink-forced": values.Capture["sinkForcedAfterTracerClosed"] = true; break;
            case "sink-incomplete": values.Sink["rawPrefixBytesPersisted"] = 9L; break;
            case "tool-changed": values.Capture["toolsUnchanged"] = false; break;
            case "wrong-producer": values.Capture["started"]!["controllerAssemblySha256"] = new string('0', 64); break;
            case "wrong-source": input = input with { SourceManifest = input.SourceManifest with { Sha256 = new string('0', 64) } }; break;
            case "wrong-native-root": input = input with { NativeRoot = input.NativeRoot + "-other" }; break;
            case "wrong-fifo": values.Capture["started"]!["fifo"] = input.NativeRoot + "/other.fifo"; break;
            default: Assert.Fail("Unknown receiving control."); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Projection.ValidateClosure(input, values.Result, values.Sink, values.Capture));
    }

    private static (Projection.Input Input, JsonObject Result, JsonObject Sink, JsonObject Capture) Closure()
    {
        const string root = "/data/cis-healthcheck-20261021-01a15a72";
        var empty = new Projection.Pin("unused", new string('0', 64), 0);
        var manifest = empty with { Sha256 = "5629c0e50e917e32d83e85450de53b970c0e0fa983253d24001e2cc80d46e692" };
        var input = new Projection.Input("unused", root + "/unit-wire-pilot61", empty, empty, empty, manifest, empty, []);
        var result = new JsonObject
        {
            ["completeExecution"] = true,
            ["complete41SourceScope"] = false,
            ["failure"] = null,
            ["nativeProcess"] = new JsonObject { ["actualExitCode"] = 0, ["timedOut"] = false, ["forcedStopReason"] = null }
        };
        var sink = new JsonObject
        {
            ["complete"] = true,
            ["observedEof"] = true,
            ["failure"] = null,
            ["syntheticFinalizationFault"] = "None",
            ["rawPrefixBytesPersisted"] = 10L,
            ["rawBytesReceived"] = 10L
        };
        var capture = new JsonObject
        {
            ["actualExitCode"] = 0,
            ["sinkForcedAfterTracerClosed"] = false,
            ["failureFlagExists"] = false,
            ["toolsUnchanged"] = true,
            ["started"] = new JsonObject
            {
                ["controllerAssemblySha256"] = "02e598bb2e6c964446f6b4aa1f6bb04b88a0bdfab777ff7e5a3c8340caffb5d6",
                ["fifo"] = input.NativeRoot + "/wire/trace.fifo",
                ["dotnet"] = root + "/dotnet/dotnet",
                ["dotnetSha256"] = "01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269",
                ["straceSha256"] = "28f957c227012de0b18d1bd7fff2d396cb693ea60ed8013be68de071e84b5001"
            }
        };
        return (input, result, sink, capture);
    }

    private sealed class Fixture : IDisposable
    {
        private sealed record Origin(long RawStart, int RawBytes, string RawSha256, string PidPrefix,
            long PerPidOffset, int PerPidBytes, string PerPidSha256);
        private readonly string root = Path.Combine(Path.GetTempPath(), "cis-process-origin-" + Guid.NewGuid().ToString("N"));
        private readonly JsonObject sink;
        private Projection.Input input;
        private List<Projection.Pin> processes = [];

        internal Fixture(string? change = null)
        {
            Directory.CreateDirectory(Path.Combine(root, "wire"));
            const string native = "/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61";
            string[] lines =
            [
                "10 1 socket(AF_INET, SOCK_STREAM, 0) = 4",
                "10 2 execve(\"\\x2f\\x64\", [\"\\x2f\\x64\"], 0x0 <unfinished ...>",
                "11 1.5 clone3({flags=CLONE_THREAD}, 88) = 12",
                "10 3 <... execve resumed>) = 0",
                "10 4 exit_group(42 <unfinished ...>",
                "10 5 <... exit_group resumed>) = ?",
                "10 6 +++ exited with 42 +++",
                "11 7 +++ exited with 0 +++"
            ];
            if (change == "out-of-order") { lines[3] = "10 1.9 <... execve resumed>) = 0"; }
            if (change == "wrong-resume") { lines[3] = "10 3 <... execveat resumed>) = 0"; }
            if (change == "intervening-exit-activity") { lines[5] = "10 5 write(1, 0x1, 1) = 1"; }
            if (change == "after-terminal") { lines = [.. lines, "10 8 write(1, 0x1, 1) = 1"]; }
            byte[] raw = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
            if (change == "bad-utf8") { raw[10] = 255; }
            if (change == "partial-eof") { raw = raw[..^1]; }
            int split = lines[0].Length + 15;
            var members = new JsonArray(); var pins = new List<Projection.Pin>();
            long start = 0;
            foreach (byte[] part in new[] { raw[..split], raw[split..] })
            {
                string name = "trace-" + (pins.Count + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".strace.gz";
                string path = Path.Combine(root, "wire", name);
                using (var file = File.Create(path)) { using var gzip = new GZipStream(file, CompressionLevel.Fastest); gzip.Write(part); }
                Projection.Pin pin = Pin(path); pins.Add(pin);
                members.Add(new JsonObject
                {
                    ["number"] = pins.Count,
                    ["path"] = native + "/wire/" + name,
                    ["rawStart"] = start,
                    ["rawEnd"] = start + part.Length,
                    ["rawBytes"] = part.Length,
                    ["rawSha256"] = Sha(part),
                    ["compressedBytes"] = pin.Bytes,
                    ["compressedSha256"] = pin.Sha256,
                    ["gzipClosed"] = true,
                    ["memberFinalizationComplete"] = true,
                    ["errors"] = new JsonArray()
                });
                start += part.Length;
            }
            sink = new JsonObject
            {
                ["members"] = members,
                ["rawBytesReceived"] = raw.Length,
                ["rawSha256Received"] = Sha(raw),
                ["newlineCount"] = lines.Length,
                ["compressedBytes"] = pins.Sum(pin => pin.Bytes)
            };
            // Independent literal expected process transformations, not calls to the producer/parser.
            var origins = new List<Origin>(); var local = new Dictionary<int, long>(); long offset = 0;
            foreach (string line in lines)
            {
                int at = line.IndexOf(' ') + 1; int pid = int.Parse(line[..(at - 1)], System.Globalization.CultureInfo.InvariantCulture);
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n"), body = Encoding.UTF8.GetBytes(line[at..] + "\n");
                long perPid = local.GetValueOrDefault(pid); local[pid] = perPid + body.Length;
                origins.Add(new(offset, bytes.Length, Sha(bytes), line[..at], perPid, body.Length, Sha(body))); offset += bytes.Length;
            }
            string originsPath = Path.Combine(root, "process-origins.jsonl");
            using (var index = new StreamWriter(originsPath, false, new UTF8Encoding(false)))
            {
                WriteProcess(index, 10,
                [
                    ("2 execve(\"/d\", [\"/d\"], 0x0) = 0", "exact-paired-exec-return-and-hex-quoted-bytes", new[] { origins[1], origins[3] }),
                    ("4 exit_group(42) = ?", "exact-paired-nonreturning-exit_group-observation", new[] { origins[4], origins[5] }),
                    ("6 +++ exited with 42 +++", "exact-selected-process-record", new[] { origins[6] })
                ]);
                WriteProcess(index, 11,
                [
                    ("1.5 clone3({flags=CLONE_THREAD}, 88) = 12", "exact-selected-process-record", new[] { origins[2] }),
                    ("7 +++ exited with 0 +++", "exact-selected-process-record", new[] { origins[7] })
                ]);
            }
            Projection.Pin empty = new("unused", new string('0', 64), 0);
            input = new(root, native, empty, empty, empty, empty, Pin(originsPath), pins.ToArray());
        }

        internal Projection.OriginValidation Verify() => Projection.VerifyOrigins(input, JsonNode.Parse(sink.ToJsonString())!.AsObject(), processes);
        internal Projection.Validation FullRead(string results, bool oversized = false)
        {
            string receipt = Path.Combine(root, "results.json"), sinkPath = Path.Combine(root, "sink.json"), capture = Path.Combine(root, "capture.json"), source = Path.Combine(root, "source.json");
            File.WriteAllText(receipt, results); File.WriteAllText(sinkPath, "{}"); File.WriteAllText(capture, "{}"); File.WriteAllText(source, "{}");
            var actual = input with { Results = Pin(receipt), Sink = Pin(sinkPath), Capture = Pin(capture), SourceManifest = Pin(source) };
            if (oversized) { actual = actual with { Results = actual.Results with { Path = Path.Combine(root, "must-not-be-opened"), Bytes = 134_217_729 } }; }
            return Projection.Read(actual, processes);
        }
        internal void Change(string change)
        {
            JsonObject first = sink["members"]![0]!.AsObject();
            switch (change)
            {
                case "member-number": first["number"] = 2; break;
                case "member-range": first["rawStart"] = 1L; break;
                case "member-errors": first["errors"]!.AsArray().Add("close failed"); break;
                case "member-unclosed": first["gzipClosed"] = false; break;
                case "compressed-pin": input = input with { Members = [input.Members[0] with { Sha256 = new string('0', 64) }, input.Members[1]] }; break;
                case "raw-hash": first["rawSha256"] = new string('0', 64); break;
                case "whole-hash": sink["rawSha256Received"] = new string('0', 64); break;
                case "newline-count": sink["newlineCount"] = 1L; break;
                case "unlisted-member": File.WriteAllText(Path.Combine(root, "wire", "trace-999999.strace.gz"), "extra"); break;
                case "swapped-members": input = input with { Members = [input.Members[1], input.Members[0]] }; break;
                case "changed-derived-bytes": File.AppendAllText(processes[0].Path, "999 +++ exited with 0 +++\n"); processes[0] = Pin(processes[0].Path); break;
                case "changed-origin-bytes": File.AppendAllText(input.Origins.Path, "{}\n"); input = input with { Origins = Pin(input.Origins.Path) }; break;
                case "missing-pid": processes.RemoveAt(0); break;
                case "extra-pid": string path = Path.Combine(root, "actual-process.99"); File.WriteAllText(path, "1 +++ exited with 0 +++\n"); processes.Add(Pin(path)); break;
                default: Assert.Fail("Unknown physical receiving control."); break;
            }
        }

        private void WriteProcess(StreamWriter index, int pid, (string Text, string Rule, Origin[] Origins)[] rows)
        {
            string path = Path.Combine(root, "actual-process." + pid); long at = 0;
            using (var file = File.Create(path))
            {
                foreach (var row in rows)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(row.Text + "\n"); file.Write(bytes);
                    index.Write(JsonSerializer.Serialize(new { pid, derivedStart = at, derivedBytes = bytes.Length, derivedSha256 = Sha(bytes), row.Rule, origins = row.Origins }) + "\n");
                    at += bytes.Length;
                }
            }
            processes.Add(Pin(path));
        }
        public void Dispose() => Directory.Delete(root, true);
        private static Projection.Pin Pin(string path) => new(path, Sha(File.ReadAllBytes(path)), new FileInfo(path).Length);
        private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
