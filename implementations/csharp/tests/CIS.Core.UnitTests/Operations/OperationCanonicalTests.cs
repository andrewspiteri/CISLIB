using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
[DoNotParallelize]
public sealed class OperationCanonicalTests
{
    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("false")]
    [DataRow("integer-negative")]
    [DataRow("decimal-one")]
    [DataRow("decimal-zero")]
    [DataRow("text-nfc")]
    [DataRow("opaque-bytes")]
    [DataRow("ordered-sequence")]
    [DataRow("sorted-map")]
    [DataRow("business-default")]
    [DataRow("business-changed")]
    public void PortableFrameMatchesIndependentPreimageAndDigestVectors(string id)
    {
        JsonObject vector = Vectors()["cases"]!.AsArray().Select(item => item!.AsObject())
            .Single(item => item["id"]!.GetValue<string>() == id);
        var input = new OperationFingerprintInput(new("vector", 1), Value(id));
        string preimage = Convert.ToHexStringLower(OperationCanonicalFrame.Encode(input));
        string digest = Convert.ToHexStringLower(OperationCanonicalFrame.Fingerprint(input).Digest.Span);

        Assert.AreEqual(vector["preimageHex"]!.GetValue<string>(), preimage);
        Assert.AreEqual(vector["sha256"]!.GetValue<string>(), digest);
    }

    [TestMethod]
    public void SemanticDefaultsAndOwnedValuesExcludeTracingButKeepMeaningfulChanges()
    {
        var normalizer = new PaymentNormalizer();
        var first = new PaymentInput(1.00m, null, "e\u0301", "trace-one", [7]);
        var second = new PaymentInput(1m, "EUR", "é", "trace-two", [7]);
        var scope = new OperationScope("account:actual", "account", "actual");

        NormalizedOperationInput<PaymentInput> normalized = normalizer.Normalize(first, scope);
        first.Marker[0] = 99;
        byte[] original = OperationCanonicalFrame.Encode(normalized.Fingerprints.Single());
        byte[] equivalent = OperationCanonicalFrame.Encode(normalizer.Normalize(second, scope).Fingerprints.Single());
        byte[] changed = OperationCanonicalFrame.Encode(normalizer.Normalize(second with
        {
            Amount = 2m
        }, scope).Fingerprints.Single());

        CollectionAssert.AreEqual(original, equivalent);
        Assert.IsFalse(original.SequenceEqual(changed));
        Assert.AreEqual("actual", normalized.Value.Account);
        Assert.AreEqual((byte)7, normalized.Value.Marker[0]);
        Assert.AreNotSame(first.Marker, normalized.Value.Marker);
    }

    [TestMethod]
    public void CanonicalNumbersAreCultureIndependentAndNormalizeDecimalZero()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            byte[] first = Frame(OperationCanonicalValue.Sequence([
                OperationCanonicalValue.Decimal(1.2300m), OperationCanonicalValue.Integer(long.MinValue)]));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            byte[] second = Frame(OperationCanonicalValue.Sequence([
                OperationCanonicalValue.Decimal(1.23m), OperationCanonicalValue.Integer(long.MinValue)]));
            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreEqual(Frame(OperationCanonicalValue.Decimal(decimal.Zero)),
                Frame(OperationCanonicalValue.Decimal(new decimal(0, 0, 0, true, 28))));
            Assert.IsFalse(Frame(OperationCanonicalValue.Decimal(decimal.MaxValue))
                .SequenceEqual(Frame(OperationCanonicalValue.Decimal(decimal.MinValue))));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void TextNormalizationAndUtf8MapOrderAreExplicitWithoutSortingSequences()
    {
        CollectionAssert.AreEqual(Frame(OperationCanonicalValue.Text("é")), Frame(OperationCanonicalValue.Text("e\u0301")));
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalValue.Text("\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalValue.Map([
            new("é", OperationCanonicalValue.Null), new("e\u0301", OperationCanonicalValue.Null)]));

        string encoded = Convert.ToHexStringLower(Frame(OperationCanonicalValue.Map([
            new("\U00010000", OperationCanonicalValue.Null), new("\ue000", OperationCanonicalValue.Null)])));
        Assert.IsTrue(encoded.IndexOf("ee8080", StringComparison.Ordinal) < encoded.IndexOf("f0908080", StringComparison.Ordinal));
        Assert.IsFalse(Frame(OperationCanonicalValue.Sequence([OperationCanonicalValue.Integer(1), OperationCanonicalValue.Integer(2)]))
            .SequenceEqual(Frame(OperationCanonicalValue.Sequence([OperationCanonicalValue.Integer(2), OperationCanonicalValue.Integer(1)]))));
        Assert.IsFalse(Frame(OperationCanonicalValue.Missing).SequenceEqual(Frame(OperationCanonicalValue.Null)));
    }

    [TestMethod]
    public void CanonicalGraphAndWholeFrameLimitsRejectBeforeUnboundedMaterialization()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalValue.Sequence(Enumerable.Repeat(OperationCanonicalValue.Null, 65)));
        var group = OperationCanonicalValue.Sequence(Enumerable.Repeat(OperationCanonicalValue.Null, 63));
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalValue.Sequence(Enumerable.Repeat(group, 4)));
        OperationCanonicalValue depth = OperationCanonicalValue.Null;
        for (int index = 0; index < 7; index++)
        {
            depth = OperationCanonicalValue.Sequence([depth]);
        }
        OperationCanonicalValue deepest = depth;
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalValue.Sequence([deepest]));
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalValue.Bytes(new byte[1_048_577]));
        var input = new OperationFingerprintInput(new("vector", 1), OperationCanonicalValue.Text("payload"));
        byte[] frame = OperationCanonicalFrame.Encode(input);
        CollectionAssert.AreEqual(frame, OperationCanonicalFrame.Encode(input, frame.Length));
        Assert.ThrowsExactly<ArgumentException>(() => OperationCanonicalFrame.Encode(input, frame.Length - 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OperationCanonicalFrame.Encode(input, 1_048_577));
    }

    [TestMethod]
    public void OpaqueBytesAndVersionedFramesAreDetachedAndUnambiguous()
    {
        byte[] supplied = [1, 2, 3];
        OperationCanonicalValue value = OperationCanonicalValue.Bytes(supplied);
        byte[] first = Frame(value);
        supplied[0] = 9;
        CollectionAssert.AreEqual(first, Frame(value));
        byte[] versionTwo = OperationCanonicalFrame.Encode(new(new("vector", 2), value));
        Assert.IsFalse(first.SequenceEqual(versionTwo));
        Assert.IsFalse(first.SequenceEqual(OperationCanonicalFrame.Encode(new(new("other", 1), value))));
        Assert.ThrowsExactly<ArgumentNullException>(() => OperationCanonicalFrame.Fingerprint(null!));
    }

    private static byte[] Frame(OperationCanonicalValue value) => OperationCanonicalFrame.Encode(new(new("vector", 1), value));

    private static OperationCanonicalValue Value(string id) => id switch
    {
        "missing" => OperationCanonicalValue.Missing,
        "null" => OperationCanonicalValue.Null,
        "false" => OperationCanonicalValue.Boolean(false),
        "integer-negative" => OperationCanonicalValue.Integer(-42),
        "decimal-one" => OperationCanonicalValue.Decimal(1.00m),
        "decimal-zero" => OperationCanonicalValue.Decimal(new decimal(0, 0, 0, true, 2)),
        "text-nfc" => OperationCanonicalValue.Text("e\u0301"),
        "opaque-bytes" => OperationCanonicalValue.Bytes(new byte[] { 0, 1, 255 }),
        "ordered-sequence" => OperationCanonicalValue.Sequence([OperationCanonicalValue.Integer(1), OperationCanonicalValue.Null]),
        "sorted-map" => OperationCanonicalValue.Map([
            new("b", OperationCanonicalValue.Integer(2)), new("a", OperationCanonicalValue.Integer(1))]),
        "business-default" => PaymentMeaning(1.00m, null, "e\u0301"),
        "business-changed" => PaymentMeaning(2.00m, "EUR", "é"),
        _ => throw new AssertFailedException("Unknown shared vector.")
    };

    private static OperationCanonicalValue PaymentMeaning(decimal amount, string? currency, string payee)
        => OperationCanonicalValue.Sequence([OperationCanonicalValue.Text(currency ?? "EUR"),
            OperationCanonicalValue.Decimal(amount), OperationCanonicalValue.Text(payee)]);

    private static JsonObject Vectors()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "conformance", "operations", "v1", "request-fingerprints.json");
            if (File.Exists(path))
            {
                return JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            }
        }
        throw new AssertFailedException("The checked-in portable operation vectors were not found.");
    }

    private sealed record PaymentInput(decimal Amount, string? Currency, string Payee, string Trace, byte[] Marker,
        string? Account = null);

    private sealed class PaymentNormalizer : IOperationRequestNormalizer<PaymentInput>
    {
        public NormalizedOperationInput<PaymentInput> Normalize(PaymentInput input, OperationScope scope)
        {
            var owned = input with
            {
                Currency = input.Currency ?? "EUR",
                Marker = input.Marker.ToArray(),
                Account = scope.ResourceIdentifier
            };
            return new(owned, [new(new("vector", 1), PaymentMeaning(owned.Amount, owned.Currency, owned.Payee))]);
        }
    }
}
