using System.Text;
using System.Text.Json;
using CIS.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests.Contracts;

[TestClass]
public sealed class AuditContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly JsonDocument Schema = Read("contracts/audit/v1/contract.schema.json");

    private static readonly JsonDocument Contract = Read("contracts/audit/v1/contract.json");

    private static readonly JsonDocument Records = Read("conformance/audit/v1/record-cases.json");

    private static readonly JsonDocument Wire = Read("conformance/audit/v1/wire-cases.json");

    private static readonly JsonDocument SecurityAuthorizationSchema = Read("contracts/audit/v1/security-authorization-details.schema.json");

    private static readonly JsonDocument SecurityOperationSchema = Read("contracts/audit/v1/security-operation-details.schema.json");

    private static readonly JsonDocument SecurityDetails = Read("conformance/audit/v1/security-details-cases.json");

    public static IEnumerable<object[]> RecordCases => Records.RootElement.GetProperty("cases").EnumerateArray()
        .Select(item => new object[] { item.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> WireCases => Wire.RootElement.GetProperty("cases").EnumerateArray()
        .Select(item => new object[] { item.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> IdentityCases => Wire.RootElement.GetProperty("identityCases").EnumerateArray()
        .Select(item => new object[] { item.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> SecurityDetailCases => SecurityDetails.RootElement.GetProperty("cases").EnumerateArray()
        .Select(item => new object[] { item.GetProperty("id").GetString()! });

    [TestMethod]
    [DynamicData(nameof(SecurityDetailCases))]
    public void IndependentSecurityDetailCasesMatchTheirSelectedSchema(string id)
    {
        JsonElement fixture = Find(SecurityDetails.RootElement.GetProperty("cases"), id);
        JsonElement schema = fixture.GetProperty("schema").GetString() switch
        {
            "authorization" => SecurityAuthorizationSchema.RootElement,
            "operation" => SecurityOperationSchema.RootElement,
            _ => throw new InvalidDataException("Select a declared Security event detail schema.")
        };
        var validator = new FixtureSchemaValidator(schema);
        Assert.AreEqual(fixture.GetProperty("expectedSchemaValid").GetBoolean(),
            validator.Accepts(fixture.GetProperty("details"), producer: true), id);
    }

    [TestMethod]
    [DynamicData(nameof(RecordCases))]
    public void IndependentRecordCasesCheckStructuralAndWireRulesSeparately(string id)
    {
        JsonElement fixture = Find(Records.RootElement.GetProperty("cases"), id);
        JsonElement record = fixture.GetProperty("record");
        var schema = new FixtureSchemaValidator(Schema.RootElement);
        bool expectedSchema = fixture.GetProperty("expectedSchemaValid").GetBoolean();
        Assert.AreEqual(expectedSchema, schema.Accepts(record, producer: false), id + ": receiving schema");
        Assert.AreEqual(expectedSchema, schema.Accepts(record, producer: true), id + ": producer common schema");

        var codec = new AuditRecordCodec();
        byte[] bytes = Encoding.UTF8.GetBytes(record.GetRawText());
        Assert.AreEqual(fixture.GetProperty("expectedWireValid").GetBoolean(),
            codec.TryDecode(codec.Contract, bytes, out _), id + ": complete wire binding");
    }

    [TestMethod]
    [DynamicData(nameof(WireCases))]
    public void IndependentWireCasesRetainExactOriginalBytes(string id)
    {
        JsonElement fixture = Find(Wire.RootElement.GetProperty("cases"), id);
        byte[] original = ReadWireBytes(fixture);
        var codec = new AuditRecordCodec();
        bool accepted = codec.TryDecode(codec.Contract, original, out AuditRecord? record);
        Assert.AreEqual(fixture.GetProperty("expectedValid").GetBoolean(), accepted, id);
        if (!accepted)
        {
            Assert.IsNull(record, id);
            return;
        }

        Assert.IsNotNull(record, id);
        CollectionAssert.AreEqual(original, codec.Encode(record), id + ": forwarding");
        CollectionAssert.AreEqual(original, record.OriginalBytes.ToArray(), id + ": original payload");

        using JsonDocument parsed = JsonDocument.Parse(original);
        byte[] originalDetails = Encoding.UTF8.GetBytes(parsed.RootElement.GetProperty("details").GetRawText());
        CollectionAssert.AreEqual(originalDetails, record.DetailsJson.ToArray(), id + ": opaque details");
    }

    [TestMethod]
    [DynamicData(nameof(IdentityCases))]
    public void IndependentIdentityPairsDeclareFutureReceiverByteObligations(string id)
    {
        JsonElement fixture = Find(Wire.RootElement.GetProperty("identityCases"), id);
        JsonElement cases = Wire.RootElement.GetProperty("cases");
        byte[] left = ReadWireBytes(Find(cases, fixture.GetProperty("left").GetString()!));
        byte[] right = ReadWireBytes(Find(cases, fixture.GetProperty("right").GetString()!));
        var codec = new AuditRecordCodec();
        Assert.IsTrue(codec.TryDecode(codec.Contract, left, out AuditRecord? leftRecord), id);
        Assert.IsTrue(codec.TryDecode(codec.Contract, right, out AuditRecord? rightRecord), id);
        Assert.IsNotNull(leftRecord, id);
        Assert.IsNotNull(rightRecord, id);
        Assert.AreEqual(leftRecord.AuditId, rightRecord.AuditId, id);
        Assert.AreEqual(fixture.GetProperty("expected").GetString() == "repeated", left.AsSpan().SequenceEqual(right), id);
    }

    [TestMethod]
    public void PortableBoundsAndFixtureCatalogsRemainExplicit()
    {
        JsonElement contract = Contract.RootElement;
        var codec = new AuditRecordCodec();
        Assert.AreEqual(contract.GetProperty("contract").GetString(), codec.Contract.Name);
        Assert.AreEqual(contract.GetProperty("contractVersion").GetInt32(), codec.Contract.Version);
        Assert.AreEqual(11, Schema.RootElement.GetProperty("required").GetArrayLength());
        Assert.AreEqual(17, Schema.RootElement.GetProperty("properties").EnumerateObject().Count());
        Assert.AreEqual(262144, contract.GetProperty("bounds").GetProperty("defaultPayloadUtf8Bytes").GetInt32());
        Assert.AreEqual(1048576, contract.GetProperty("bounds").GetProperty("maximumPayloadUtf8Bytes").GetInt32());
        Assert.AreEqual(256, contract.GetProperty("bounds").GetProperty("capabilities").GetInt32());
        Assert.AreEqual(1024, contract.GetProperty("bounds").GetProperty("domainEventIdScalars").GetInt32());
        foreach (JsonDocument catalog in new[] { Records, Wire, SecurityDetails })
        {
            JsonElement[] cases = catalog.RootElement.GetProperty("cases").EnumerateArray().ToArray();
            Assert.AreEqual(catalog.RootElement.GetProperty("caseCount").GetInt32(), cases.Length);
            Assert.AreEqual(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [TestMethod]
    public void FixtureSchemaArrayCountsEnforceCompleteCapabilityBoundsAndItemValidation()
    {
        using JsonDocument schema = JsonDocument.Parse("{\"type\":\"array\",\"minItems\":1,\"maxItems\":256,\"items\":{\"type\":\"string\"}}");
        var validator = new FixtureSchemaValidator(schema.RootElement);
        foreach (int count in new[] { 0, 1, 256, 257 })
        {
            using JsonDocument value = JsonDocument.Parse(JsonSerializer.Serialize(Enumerable.Repeat("audit.receive", count)));
            Assert.AreEqual(count is >= 1 and <= 256, validator.Accepts(value.RootElement, producer: true), count.ToString());
        }

        using JsonDocument wrongItem = JsonDocument.Parse("[\"audit.receive\",1]");
        Assert.IsFalse(validator.Accepts(wrongItem.RootElement, producer: false));
        using JsonDocument unsupported = JsonDocument.Parse("{\"type\":\"array\",\"uniqueItems\":true}");
        Assert.ThrowsExactly<NotSupportedException>(() => new FixtureSchemaValidator(unsupported.RootElement));
    }

    private static byte[] ReadWireBytes(JsonElement fixture) => fixture.TryGetProperty("wireBase64", out JsonElement encoded)
        ? Convert.FromBase64String(encoded.GetString()!)
        : Encoding.UTF8.GetBytes(fixture.GetProperty("wireUtf8").GetString()!);

    private static JsonElement Find(JsonElement cases, string id) => cases.EnumerateArray()
        .Single(item => item.GetProperty("id").GetString() == id);

    private static JsonDocument Read(string relativePath) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, relativePath)));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the Audit contract checks from the CisLib checkout.");
    }
}
