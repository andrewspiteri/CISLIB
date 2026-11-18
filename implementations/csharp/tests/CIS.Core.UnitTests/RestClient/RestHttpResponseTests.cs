using System.Text.Json;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestHttpResponseTests
{
    [TestMethod]
    [DataRow("JSON-ZERO-BYTES")]
    [DataRow("JSON-ZERO")]
    [DataRow("JSON-FALSE")]
    [DataRow("JSON-EMPTY-STRING")]
    [DataRow("JSON-EMPTY-ARRAY")]
    [DataRow("JSON-PRESENT-NULL")]
    [DataRow("JSON-UNREPRESENTABLE-NULL")]
    [DataRow("JSON-MALFORMED")]
    [DataRow("TEXT-ZERO-BYTES")]
    [DataRow("TEXT-LITERAL-NULL")]
    [DataRow("BYTES-ZERO-BYTES")]
    [DataRow("BYTES-PRESENT")]
    [DataRow("REQUIRED-ABSENCE")]
    [DataRow("ERROR-DOES-NOT-DECODE-T")]
    [DataRow("HEAD-NO-DECODE")]
    public async Task LiteralPortableResponseCasesExerciseTheRealDefaultReaderWithoutInventingLogicalResults(string id)
    {
        using JsonDocument fixture = Fixture();
        JsonElement row = fixture.RootElement.GetProperty("responseCases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        byte[] bytes = row.TryGetProperty("bodyHex", out JsonElement hex) ? Convert.FromHexString(hex.GetString()!) : RestHttpTestSupport.Utf8(row.GetProperty("bodyUtf8").GetString()!);
        await using var response = new RestHttpTestResponse(row.GetProperty("httpStatusCode").GetInt32(), new MemoryStream(bytes));
        var observation = new RestHttpObservation();
        var reader = new RestHttpResponseReader(response, new(16 * 1024 * 1024, 16 * 1024, 1024), observation);
        switch (row.GetProperty("resultType").GetString())
        {
            case "int32": await Check<int>(reader, row); break;
            case "boolean": await Check<bool>(reader, row); break;
            case "string": case "nullableString": await Check<string?>(reader, row); break;
            case "int32Array": await Check<int[]>(reader, row); break;
            case "byteArray": await Check<byte[]>(reader, row); break;
            case "noValue": await Check<RestNoValue>(reader, row); break;
            default: Assert.Fail("Unknown response fixture type."); break;
        }
        Assert.AreEqual(id == "HEAD-NO-DECODE" ? 0L : bytes.LongLength, observation.Downloaded);
    }

    [TestMethod]
    [DataRow(4, false)]
    [DataRow(5, true)]
    public async Task SuccessLimitCountsOnlyActualReadsAndRejectsTheFirstByteBeyondItsLimit(int length, bool exceeds)
    {
        await using var response = new RestHttpTestResponse(200, new MemoryStream(new byte[length]));
        var observed = new RestHttpObservation();
        var reader = new RestHttpResponseReader(response, new(4, 3, 1024), observed);
        if (exceeds) { await Assert.ThrowsExactlyAsync<RestResponseLimitException>(() => reader.ReadAsync<byte[]>(RestResponseFormat.Bytes, false, null, default).AsTask()); }
        else { Assert.HasCount(4, (await reader.ReadAsync<byte[]>(RestResponseFormat.Bytes, false, null, default)).Decoded.Value!); }
        Assert.AreEqual((long)length, observed.Downloaded);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync<byte[]>(RestResponseFormat.Bytes, false, null, default).AsTask());
    }

    [TestMethod]
    [DataRow(4, false)]
    [DataRow(5, true)]
    public async Task ErrorPrefixKeepsAnExactCapAndItsProbeIsObservedWithoutDecodingSuccessT(int length, bool truncated)
    {
        await using var response = new RestHttpTestResponse(503, new MemoryStream(new byte[length]), "application/problem+json");
        var observed = new RestHttpObservation();
        RestReadResult<int> result = await new RestHttpResponseReader(response, new(10, 4, 1024), observed).ReadAsync<int>(RestResponseFormat.Json, false, null, default);
        Assert.IsFalse(result.Decoded.HasValue); Assert.HasCount(4, result.ErrorBody!.Bytes);
        Assert.AreEqual(truncated, result.ErrorBodyTruncated); Assert.AreEqual((long)length, observed.Downloaded);
    }

    [TestMethod]
    public async Task ActualReadIoFailureIsIdentifiedButAnArbitraryCustomCodecJsonExceptionStaysTheSameFault()
    {
        var original = new JsonException("Custom codec programming detail.");
        await using var normal = new RestHttpTestResponse(200, new MemoryStream(RestHttpTestSupport.Utf8("0")));
        JsonException actual = await Assert.ThrowsExactlyAsync<JsonException>(() => new RestHttpResponseReader(normal, new(10, 4, 1024))
            .ReadAsync<int>(RestResponseFormat.Json, false, new FaultCodec(original), default).AsTask());
        Assert.AreSame(original, actual);
        await using var broken = new RestHttpTestResponse(200, new BrokenRead());
        await Assert.ThrowsExactlyAsync<RestResponseReadException>(() => new RestHttpResponseReader(broken, new(10, 4, 1024))
            .ReadAsync<byte[]>(RestResponseFormat.Bytes, false, null, default).AsTask());
    }

    [TestMethod]
    public async Task ExplicitTextCharsetIsDecodedByNativeEncodingAndNoValueNeverTouchesTheBody()
    {
        await using var text = new RestHttpTestResponse(200, new MemoryStream([0xe9]), "text/plain; charset=iso-8859-1");
        Assert.AreEqual("é", (await new RestHttpResponseReader(text, new(10, 4, 1024)).ReadAsync<string>(RestResponseFormat.Text, true, null, default)).Decoded.Value);
        await using var ignored = new RestHttpTestResponse(200, new BrokenRead());
        Assert.IsFalse((await new RestHttpResponseReader(ignored, new(10, 4, 1024)).ReadAsync<RestNoValue>(RestResponseFormat.NoValue, false, null, default)).Decoded.HasValue);
    }

    private static async Task Check<T>(RestHttpResponseReader reader, JsonElement row)
    {
        JsonElement expected = row.GetProperty("expected");
        RestResponseFormat format = Enum.Parse<RestResponseFormat>(row.GetProperty("format").GetString()!);
        if (expected.GetProperty("failureKind").GetString() == "InvalidResponse")
        { await Assert.ThrowsExactlyAsync<RestDecodingException>(() => reader.ReadAsync<T>(format, row.GetProperty("requireValue").GetBoolean(), null, default).AsTask()); return; }
        RestReadResult<T> result = await reader.ReadAsync<T>(format, row.GetProperty("requireValue").GetBoolean(), null, default);
        Assert.AreEqual(expected.GetProperty("hasValue").GetBoolean(), result.Decoded.HasValue);
        if (expected.GetProperty("failureKind").GetString() == "Http") { Assert.IsNotNull(result.ErrorBody); return; }
        if (expected.TryGetProperty("valueHex", out JsonElement hex)) { CollectionAssert.AreEqual(Convert.FromHexString(hex.GetString()!), (byte[])(object)result.Decoded.Value!); }
        else if (expected.TryGetProperty("value", out JsonElement value))
        {
            if (typeof(T) == typeof(int[])) { Assert.HasCount(0, (int[])(object)result.Decoded.Value!); }
            else { Assert.AreEqual(JsonSerializer.Deserialize<T>(value.GetRawText()), result.Decoded.Value); }
        }
    }
    private static JsonDocument Fixture()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "conformance", "rest-client", "v1", "contract-cases.json");
            if (File.Exists(path)) { return JsonDocument.Parse(File.ReadAllBytes(path)); }
        }
        throw new AssertFailedException("The preserved response fixture was not found.");
    }
    private sealed class FaultCodec(Exception failure) : IRestContentCodec
    {
        public bool CanDecode(Type type, RestResponseFormat format) => true;
        public ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token) => throw failure;
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw failure;
    }
    private sealed class BrokenRead : MemoryStream
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => throw new IOException("Owned body read failed."); }
}
