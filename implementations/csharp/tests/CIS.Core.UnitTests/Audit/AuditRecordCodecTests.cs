using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CIS.Audit;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Audit;

[TestClass]
public sealed class AuditRecordCodecTests
{
    private const string Wire = """{"auditVersion":1,"auditId":"11111111-1111-4111-8111-111111111111","eventType":"future.event","eventVersion":73,"category":"business","occurredAtUtc":"2026-11-23T10:20:30Z","recordedAtUtc":"2026-11-23T10:20:31.1234567Z","producer":{"application":"producer","environment":"test"},"actor":{"kind":"unknown"},"outcome":"unknown","details":{ "amount" : "12345678901234567890.0100", "future":[null,true,{"identifier":"9007199254740993"}] }}""";

    [TestMethod]
    public void UnknownApplicationDetailsAndOriginalFormattingRoundTripExactly()
    {
        byte[] source = Encoding.UTF8.GetBytes(" \n" + Wire + "\t");
        var codec = new AuditRecordCodec();

        Assert.IsTrue(codec.TryDecode(source, out AuditRecord? record, out AuditWireFailure failure));

        Assert.IsNotNull(record);
        Assert.AreEqual(AuditWireFailure.None, failure);
        Assert.AreEqual(new MessageContract("cis.audit.record", 1), codec.Contract);
        Assert.AreEqual("future.event", record.EventType);
        Assert.AreEqual(73, record.EventVersion);
        Assert.AreEqual(AuditActorKind.Unknown, record.Actor.Kind);
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-23T10:20:30Z"), record.OccurredAtUtc);
        CollectionAssert.AreEqual(source, codec.Encode(record));
        StringAssert.Contains(Encoding.UTF8.GetString(record.DetailsJson.Span), "\"12345678901234567890.0100\"");
        StringAssert.Contains(Encoding.UTF8.GetString(record.DetailsJson.Span), "\"9007199254740993\"");

        source.AsSpan().Fill((byte)'!');
        byte[] expected = Encoding.UTF8.GetBytes(" \n" + Wire + "\t");
        CollectionAssert.AreEqual(expected, codec.Encode(record));
        Assert.IsTrue(MemoryMarshal.TryGetArray(record.OriginalBytes, out ArraySegment<byte> exposed));
        exposed.Array![exposed.Offset] = (byte)'!';
        CollectionAssert.AreEqual(expected, codec.Encode(record));
        Assert.IsTrue(MemoryMarshal.TryGetArray(record.DetailsJson, out ArraySegment<byte> details));
        details.Array![details.Offset] = (byte)'!';
        CollectionAssert.AreEqual(expected, codec.Encode(record));
    }

    [TestMethod]
    [DataRow("\"auditVersion\":1", "\"auditVersion\":2", AuditWireFailure.UnsupportedVersion)]
    [DataRow("\"auditVersion\":1", "\"auditVersion\":1.0", AuditWireFailure.Invalid)]
    [DataRow("\"eventVersion\":73", "\"eventVersion\":0", AuditWireFailure.Invalid)]
    [DataRow("\"eventType\":\"future.event\"", "\"eventType\":\"bad name\"", AuditWireFailure.Invalid)]
    [DataRow("11111111-1111-4111-8111-111111111111", "00000000-0000-0000-0000-000000000000", AuditWireFailure.Invalid)]
    [DataRow("2026-11-23T10:20:30Z", "2026-11-23", AuditWireFailure.Invalid)]
    [DataRow("2026-11-23T10:20:30Z", "2026-11-23T10:20:30+00:00", AuditWireFailure.Invalid)]
    [DataRow("2026-11-23T10:20:30Z", "2026-11-23T10:20:30.Z", AuditWireFailure.Invalid)]
    [DataRow("2026-11-23T10:20:30Z", "2026-11-23T10:20:30.12345678Z", AuditWireFailure.Invalid)]
    [DataRow("\"category\":\"business\"", "\"category\":\"Business\"", AuditWireFailure.Invalid)]
    [DataRow("\"auditVersion\":1", "\"extra\":false,\"auditVersion\":1", AuditWireFailure.Invalid)]
    [DataRow("\"application\":\"producer\"", "\"extra\":false,\"application\":\"producer\"", AuditWireFailure.Invalid)]
    [DataRow("\"kind\":\"unknown\"", "\"kind\":\"application\"", AuditWireFailure.Invalid)]
    [DataRow("\"kind\":\"unknown\"", "\"kind\":\"unknown\",\"subject\":{\"issuer\":\"x\",\"identifier\":\"y\"}", AuditWireFailure.Invalid)]
    [DataRow("\"kind\":\"unknown\"", "\"kind\":\"unknown\",\"application\":\"caller\"", AuditWireFailure.Invalid)]
    [DataRow("\"kind\":\"unknown\"", "\"kind\":\"application\",\"subject\":{\"issuer\":\"x\",\"identifier\":\"y\"},\"humanSubject\":{\"issuer\":\"h\",\"identifier\":\"human\"}", AuditWireFailure.Invalid)]
    [DataRow("\"kind\":\"unknown\"", "\"kind\":\"unknown\",\"instance\":{\"kind\":\"verified\",\"reportedIdentifier\":\"replica\"}", AuditWireFailure.Invalid)]
    [DataRow("\"kind\":\"unknown\"", "\"kind\":\"unknown\",\"profile\":null", AuditWireFailure.Invalid)]
    [DataRow("\"details\":{", "\"details\":{\"amount\":1,", AuditWireFailure.Invalid)]
    [DataRow("\"future\":[null,true,", "\"future\":[null,true,{\"a\":1,\"\\u0061\":2},", AuditWireFailure.Invalid)]
    [DataRow("\"amount\" :", "\"amount\":0,\"\\u0061mount\" :", AuditWireFailure.Invalid)]
    [DataRow("\"auditVersion\":1", "/*comment*/\"auditVersion\":1", AuditWireFailure.Invalid)]
    [DataRow("\"identifier\":\"9007199254740993\"", "\"identifier\":\"\\ud800\"", AuditWireFailure.Invalid)]
    public void MalformedOrContradictoryCommonDataNeverProducesARecord(string original, string replacement, AuditWireFailure expected)
    {
        var codec = new AuditRecordCodec();
        byte[] source = Encoding.UTF8.GetBytes(Wire.Replace(original, replacement, StringComparison.Ordinal));

        Assert.IsFalse(codec.TryDecode(source, out AuditRecord? record, out AuditWireFailure failure));

        Assert.IsNull(record);
        Assert.AreEqual(expected, failure);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("\"text\"")]
    [DataRow("42")]
    public void DetailsMustBeAnObjectEvenForUnknownEvents(string details)
    {
        string source = Wire[..Wire.IndexOf("\"details\":", StringComparison.Ordinal)] + "\"details\":" + details + "}";

        Assert.IsFalse(new AuditRecordCodec().TryDecode(Encoding.UTF8.GetBytes(source), out AuditRecord? record, out _));
        Assert.IsNull(record);
    }

    [TestMethod]
    public void FramingByteDepthContractAndTrailingInputLimitsStayDistinct()
    {
        byte[] source = Encoding.UTF8.GetBytes(Wire);
        var codec = new AuditRecordCodec(new(source.Length, 16));
        Assert.IsTrue(codec.TryDecode(source, out AuditRecord? record, out _));
        Assert.IsNotNull(record);

        Assert.IsFalse(new AuditRecordCodec(new(source.Length - 1, 16)).TryDecode(source, out _, out AuditWireFailure tooLarge));
        Assert.AreEqual(AuditWireFailure.TooLarge, tooLarge);
        Assert.IsFalse(new AuditRecordCodec(new(source.Length, 2)).TryDecode(source, out _, out AuditWireFailure tooDeep));
        Assert.AreEqual(AuditWireFailure.Invalid, tooDeep);
        Assert.IsFalse(codec.TryDecode(new MessageContract("other.event", 1), source, out _));
        Assert.IsFalse(codec.TryDecode(new MessageContract("cis.audit.record", 2), source, out _));
        Assert.IsFalse(new AuditRecordCodec().TryDecode(Encoding.UTF8.GetBytes(Wire + "{}"), out _, out _));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditRecordCodec(new(source.Length - 1)).Encode(record));
    }

    [TestMethod]
    [DataRow("c328")]
    [DataRow("eda080")]
    [DataRow("c0af")]
    [DataRow("80")]
    public void InvalidUtf8NeverBecomesReplacementCharacterIdentity(string invalidHex)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(Wire[..Wire.IndexOf("\"details\":", StringComparison.Ordinal)] + "\"details\":{\"value\":\"");
        byte[] source = [.. prefix, .. Convert.FromHexString(invalidHex), .. Encoding.UTF8.GetBytes("\"}}")];

        Assert.IsFalse(new AuditRecordCodec().TryDecode(source, out AuditRecord? record, out AuditWireFailure failure));
        Assert.IsNull(record);
        Assert.AreEqual(AuditWireFailure.Invalid, failure);
    }

    [TestMethod]
    public void CommonIdentityModelsRetainNativeScalarBoundsWithoutTruncation()
    {
        string application = string.Concat(Enumerable.Repeat("😀", 255));
        string identifier = string.Concat(Enumerable.Repeat("😀", 1024));
        string alias = string.Concat(Enumerable.Repeat("😀", 64));
        string code = string.Concat(Enumerable.Repeat("😀", 128));
        var subject = new AuditSubject(identifier, identifier);
        var actor = new AuditActor(AuditActorKind.User, subject, application, application, alias, alias,
            identifier, new(identifier, identifier), new("another-issuer", identifier));

        Assert.AreEqual(identifier, actor.Subject!.Issuer);
        Assert.AreEqual(alias, actor.AuthenticationMethod);
        Assert.AreEqual(AuditInstanceKind.Verified, actor.Instance!.Kind);
        Assert.AreNotEqual(actor.Subject.Issuer, actor.HumanSubject!.Issuer);
        Assert.AreEqual(code, new AuditReason(code).Code);
        Assert.AreEqual(code, new AuditCaptureOptions(correlationId: code).CorrelationId);
        Assert.ThrowsExactly<ArgumentException>(() => new AuditProducer(application + "x", application));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSubject(identifier + "x", identifier));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditReason(code + "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditTarget("app", "test", "resource"));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditActor(AuditActorKind.Application, subject, application: "caller"));
    }

    [TestMethod]
    public void PortableRecordHasNoPublicPromotionIntoCapturedSourceEligibility()
    {
        Assert.IsEmpty(typeof(CapturedAuditRecord).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.IsEmpty(typeof(CapturedAuditRecord).GetMethods(BindingFlags.Public | BindingFlags.Static));
        Assert.IsFalse(typeof(IAuditCapture).GetMethods().Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AuditRecord))));
        Assert.IsTrue(new AuditRecordCodec().TryDecode(Encoding.UTF8.GetBytes(Wire), out AuditRecord? decoded, out _));
        Assert.IsNotNull(decoded);
        Assert.AreEqual(typeof(AuditRecord), decoded.GetType());
    }
}
