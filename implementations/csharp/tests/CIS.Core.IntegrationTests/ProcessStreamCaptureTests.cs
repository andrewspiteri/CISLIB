using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProcessStreamCaptureTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalBytesSurviveWhileTextKeepsItsExistingBomDecoding(bool bom)
    {
        byte[] stdout = Bytes("stdout Ω 😀\n", bom), stderr = Bytes("stderr λ 🎉\r\n", bom);
        ProcessResult child = await Capture(stdout, stderr);
        CollectionAssert.AreEqual(stdout, child.StandardOutputBytes.ToArray());
        CollectionAssert.AreEqual(stderr, child.StandardErrorBytes.ToArray());
        Assert.AreEqual("stdout Ω 😀\n", child.StandardOutput);
        Assert.AreEqual("stderr λ 🎉\r\n", child.StandardError);
    }

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public async Task PhysicalReportExtractionUsesByteOffsetsAndRetainsBothOriginalStreams(string ending)
    {
        byte[] prefix = Bytes("{\"message\":\"Ω 😀\"}\n", true), stderr = Bytes("{\"message\":\"λ 🎉\"}\n", true);
        ProcessResult child = await Capture([.. prefix, .. Encoding.UTF8.GetBytes("{\"probe\":true}" + ending)], stderr);
        JsonObject report = FixtureProcessChecks.ReadPhysicalReport(child);
        Assert.IsTrue(report["probe"]!.GetValue<bool>());
        CollectionAssert.AreEqual(prefix, Convert.FromBase64String(report["stdout"]!.GetValue<string>()));
        CollectionAssert.AreEqual(stderr, Convert.FromBase64String(report["stderr"]!.GetValue<string>()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task JsonlFramingRejectsTheOriginalBomOnEitherPipe(bool stderr)
    {
        byte[] valid = Bytes("{}\n", false);
        ProcessResult baseline = await Capture(stderr ? [] : valid, stderr ? valid : []);
        byte[] clean = (stderr ? baseline.StandardErrorBytes : baseline.StandardOutputBytes).ToArray();
        Assert.AreEqual(1, ProviderReportChecks.Rows(clean, 1).Length);
        byte[] malformed = Bytes("{}\n", true);
        ProcessResult child = await Capture(stderr ? [] : malformed, stderr ? malformed : []);
        byte[] original = (stderr ? child.StandardErrorBytes : child.StandardOutputBytes).ToArray();
        Assert.AreEqual("{}\n", stderr ? child.StandardError : child.StandardOutput);
        Assert.AreEqual("JSONL framing", Assert.ThrowsExactly<InvalidDataException>(() => ProviderReportChecks.Rows(original, 1)).Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MalformedUtf8StillFailsStrictTextDecodingOnEitherPipe(bool stderr)
    {
        byte[] malformed = [0xc3, 0x28];
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(() => Capture(stderr ? [] : malformed, stderr ? malformed : []));
    }

    [TestMethod]
    public async Task BothPipesDrainConcurrentlyBeyondTheirNativeBufferCapacity()
    {
        ProcessResult child = await Run(["process-stream-pressure"]);
        byte[] stdout = child.StandardOutputBytes.ToArray(), stderr = child.StandardErrorBytes.ToArray();
        Assert.AreEqual(1048576, stdout.Length);
        Assert.AreEqual(1048576, stderr.Length);
        Assert.IsTrue(stdout.All(value => value == (byte)'o'));
        Assert.IsTrue(stderr.All(value => value == (byte)'e'));
    }

    private static byte[] Bytes(string text, bool bom) => bom
        ? [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private static Task<ProcessResult> Capture(byte[] stdout, byte[] stderr) =>
        Run(["process-streams", Convert.ToBase64String(stdout), Convert.ToBase64String(stderr)]);

    private static async Task<ProcessResult> Run(string[] arguments)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, .. arguments],
            AppContext.BaseDirectory, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode);
        Assert.IsTrue(child.ProcessId > 0);
        return child;
    }
}
