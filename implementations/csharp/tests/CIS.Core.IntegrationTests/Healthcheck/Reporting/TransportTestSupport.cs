using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

internal static class TransportTestSupport
{
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    internal static string NewPipeName() => "cis-health-test-" + Guid.NewGuid().ToString("N");

    internal static ProcessStartInfo StartInfo(string subdirectory, string assembly, IEnumerable<string> arguments)
    {
        string path = Path.Combine(AppContext.BaseDirectory, subdirectory, assembly + ".dll");
        Assert.IsTrue(File.Exists(path), "The real fixture must be built and copied: " + path);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true),
        };
        path = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildSupport.Prepare(path, start);
        foreach (string argument in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", path }.Concat(arguments)) { start.ArgumentList.Add(argument); }
        return start;
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellation = default)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        Assert.IsTrue(size is > 0 and <= 16777216, "Response frame bounds.");
        byte[] payload = new byte[checked((int)size)];
        await stream.ReadExactlyAsync(payload, cancellation);
        return payload;
    }

    internal static async Task WriteFrameAsync(Stream stream, byte[] payload, bool fragmented = false, uint? announcedLength = null, CancellationToken cancellation = default)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, announcedLength ?? checked((uint)payload.Length));
        if (fragmented)
        {
            foreach (byte value in header.Concat(payload)) { await stream.WriteAsync(new[] { value }, cancellation); await stream.FlushAsync(cancellation); }
        }
        else { await stream.WriteAsync(header, cancellation); await stream.WriteAsync(payload, cancellation); await stream.FlushAsync(cancellation); }
    }

    internal static async Task<JsonObject> RequestAsync(string pipe, string json, bool fragmented = false)
    {
        using var timeout = new CancellationTokenSource(Limit);
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);
        await WriteFrameAsync(client, Encoding.UTF8.GetBytes(json), fragmented, cancellation: timeout.Token);
        byte[] payload = await ReadFrameAsync(client, timeout.Token);
        Assert.AreNotEqual((byte)0xef, payload[0], "No UTF-8 BOM.");
        return JsonNode.Parse(payload)!.AsObject();
    }

    internal static JsonObject ValidateSnapshotEnvelope(JsonObject envelope, string view, bool healthy, bool ready, int active = 1)
    {
        Assert.AreEqual(1, (int?)envelope["protocolVersion"]);
        Assert.AreEqual(1, (int?)envelope["schemaVersion"]);
        Assert.AreEqual(view, (string?)envelope["view"]);
        Assert.AreEqual("snapshot", (string?)envelope["kind"]);
        Assert.IsFalse(envelope.ContainsKey("error"));
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        ValidateSnapshot(snapshot, view, healthy, ready, active);
        return snapshot;
    }

    internal static void ValidateSnapshot(JsonObject snapshot, string view, bool healthy, bool ready, int active = 1)
    {
        Assert.AreEqual(1, (int?)snapshot["schemaVersion"]);
        Assert.AreEqual(healthy ? "Healthy" : "Unhealthy", (string?)snapshot["healthStatus"]);
        Assert.AreEqual(ready, (bool?)snapshot["ready"]);
        Assert.IsNotNull((string?)snapshot["revision"]);
        Assert.IsTrue(((string)snapshot["capturedAtUtc"]!).EndsWith('Z'));
        Assert.AreEqual(active, (int?)snapshot["monitorCounts"]!["active"]);
        if (view == "details") { Assert.AreEqual(active, snapshot["monitors"]!.AsArray().Count); }
        else { Assert.IsFalse(snapshot.ContainsKey("monitors")); }
    }

    internal static void ValidateError(JsonObject response, string code)
    {
        Assert.AreEqual("error", (string?)response["kind"]);
        Assert.AreEqual(code, (string?)response["error"]!["code"]);
        Assert.IsFalse(response.ContainsKey("snapshot"));
        Assert.IsFalse(response.ToJsonString().Contains("secret", StringComparison.Ordinal));
    }
}
