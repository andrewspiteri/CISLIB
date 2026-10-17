using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.ArchitectureTests.Publications;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class PublicationNativeTests
{
    private static string Repository => CandidateRoot();
    private static string Schemas => Path.Combine(Repository, "compatibility/csharp/publications");

    [TestMethod]
    [DataRow("source", "DuplicateFixture")]
    [DataRow("binary", "DuplicateFixture")]
    [DataRow("execution-target", "UnknownFixtureTarget")]
    [DataRow("original-consumer", "UnknownOriginalExecution")]
    [DataRow("additional-property", "BaselineSchemaInvalid")]
    [DataRow("null-array", "BaselineSchemaInvalid")]
    [DataRow("fractional-bytes", "BaselineSchemaInvalid")]
    [DataRow("excess-bytes", "BaselineSchemaInvalid")]
    public async Task RetainedSchemaAndLinkChecksRejectAdditionalCorruption(string fault, string code)
    {
        using var fixture = new PublicationFixture();
        JsonObject record = fixture.FirstRecord;
        switch (fault)
        {
            case "source": record["sourceFixtures"]!.AsArray().Add(record["sourceFixtures"]![0]!.DeepClone()); break;
            case "binary": record["binaryFixtures"]!.AsArray().Add(record["binaryFixtures"]![0]!.DeepClone()); break;
            case "execution-target": record["executions"]![0]!["targetFramework"] = "net99.0"; break;
            case "original-consumer":
                JsonNode second = record["binaryFixtures"]![0]!.DeepClone(); second["fixtureId"] = "other";
                record["binaryFixtures"]!.AsArray().Add(second); record["executions"]![0]!["consumerFixtureIds"] = new JsonArray("other"); break;
            case "additional-property": record["invented"] = true; break;
            case "null-array": record["targets"] = null; break;
            case "fractional-bytes": record["verifiedCandidatePackage"]!["bytes"] = JsonNode.Parse("1.000000000000000000000000000000000001"); break;
            case "excess-bytes": record["verifiedCandidatePackage"]!["bytes"] = 9007199254740992L; break;
        }
        fixture.SaveRecord();
        JsonObject report = await fixture.Validate(Repository, 2);
        Assert.AreEqual(code, report["issues"]![0]!["code"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("1", true)]
    [DataRow("1.0", true)]
    [DataRow("1e0", true)]
    [DataRow("100e-2", true)]
    [DataRow("0e-99999999999999999999999", true)]
    [DataRow("1e99999999999999999999999", true)]
    [DataRow("1e-99999999999999999999999", false)]
    [DataRow("1.000000000000000000000000000000000001", false)]
    [DataRow("9007199254740990.5", false)]
    [DataRow("true", false)]
    [DataRow("\"1\"", false)]
    public void IntegerSchemaDoesNotRoundFractionsOrRejectEquivalentSpellings(string number, bool valid)
    {
        using JsonDocument value = JsonDocument.Parse(number), schema = JsonDocument.Parse("{\"type\":\"integer\"}");
        if (valid) { PublicationSchema.Validate(value.RootElement, schema.RootElement); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => PublicationSchema.Validate(value.RootElement, schema.RootElement)); }
    }

    [TestMethod]
    public void AppendOnlyComparisonAcceptsEquivalentNumberSpellingsAndObjectOrder()
    {
        using var fixture = new PublicationFixture();
        fixture.FreezePrevious();
        JsonObject previous = JsonNode.Parse(File.ReadAllText(fixture.PreviousPath))!.AsObject();
        previous["publications"]![0]!["sequence"] = JsonNode.Parse("1.0");
        var reordered = new JsonObject(previous.Reverse().Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
        File.WriteAllText(fixture.PreviousPath, reordered.ToJsonString());
        fixture.SaveLedger();
        JsonObject report = PublicationValidation.Validate(fixture.LedgerPath, fixture.Root, Schemas, fixture.PreviousPath,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.PreviousPath))));
        Assert.AreEqual(0, report["exitCode"]!.GetValue<int>(), report.ToJsonString());

    }

    [TestMethod]
    public void UnsupportedSchemaAssertionsFailEvenOnAnUnusedBranch()
    {
        using var schemas = new TemporaryDirectory();
        foreach (string file in Directory.GetFiles(Schemas, "*.schema.json")) { File.Copy(file, Path.Combine(schemas.Path, Path.GetFileName(file))); }
        string ledgerSchema = Path.Combine(schemas.Path, "ledger.schema.json");
        JsonObject definition = JsonNode.Parse(File.ReadAllText(ledgerSchema))!.AsObject();
        definition["$defs"]!["artifact"]!["properties"]!["path"]!["maxLength"] = 1;
        File.WriteAllText(ledgerSchema, definition.ToJsonString());
        using var fixture = new PublicationFixture();
        fixture.Publications.Clear(); fixture.SaveLedger();
        JsonObject report = PublicationValidation.Validate(fixture.LedgerPath, fixture.Root, schemas.Path);
        Assert.AreEqual("LedgerSchemaInvalid", report["issues"]![0]!["code"]!.GetValue<string>());
    }

    [TestMethod]
    public void EscapedDuplicateJsonMembersAreRejectedInsideRecords()
    {
        using var fixture = new PublicationFixture();
        string path = fixture.RecordPath();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schema\\u0056ersion\": 1,", StringComparison.Ordinal), new UTF8Encoding(false));
        fixture.Publications[0]!["record"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        fixture.Publications[0]!["record"]!["bytes"] = new FileInfo(path).Length;
        fixture.SaveLedger();
        JsonObject report = PublicationValidation.Validate(fixture.LedgerPath, fixture.Root, Schemas);
        Assert.AreEqual("DuplicateJsonProperty", report["issues"]![0]!["code"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task LinkedArtifactDirectoryIsRejected()
    {
        using var fixture = new PublicationFixture();
        using var outside = new TemporaryDirectory();
        File.Copy(fixture.PayloadPath, Path.Combine(outside.Path, "format-only.bin"));
        string link = Path.Combine(fixture.Root, "artifacts", "linked");
        if (OperatingSystem.IsWindows())
        {
            ProcessResult result = await ProcessRunner.RunAsync("cmd.exe", ["/c", "mklink", "/J", link, outside.Path], fixture.Root, TimeSpan.FromSeconds(15));
            Assert.AreEqual(0, result.ExitCode, result.StandardError);
        }
        else { Directory.CreateSymbolicLink(link, outside.Path); }
        try
        {
            fixture.FirstRecord["verifiedCandidatePackage"]!["path"] = "artifacts/linked/format-only.bin";
            fixture.SaveRecord();
            JsonObject report = await fixture.Validate(Repository, 2);
            Assert.AreEqual("ArtifactPathEscape", report["issues"]![0]!["code"]!.GetValue<string>());
        }
        finally { Directory.Delete(link); }
    }

    [TestMethod]
    [DataRow("ledger")]
    [DataRow("previous")]
    [DataRow("storage")]
    [DataRow("existing")]
    public void OutputRefusalsPreserveExistingInputBytes(string destination)
    {
        using var fixture = new PublicationFixture();
        fixture.SaveLedger();
        string path = destination switch
        {
            "ledger" => fixture.LedgerPath,
            "previous" => fixture.PreviousPath,
            "storage" => Path.Combine(Schemas, "ledger.schema.json"),
            _ => fixture.PayloadPath
        };
        byte[] original = File.ReadAllBytes(path);
        Assert.ThrowsExactly<InvalidDataException>(() => PublicationValidation.Write(Repository, fixture.LedgerPath, fixture.PreviousPath, path, new JsonObject()));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    private static string CandidateRoot()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        { if (File.Exists(Path.Combine(at.FullName, "implementations/csharp/CisLib.slnx"))) { return at.FullName; } }
        throw new DirectoryNotFoundException("Repository required.");
    }
}

[TestClass]
[DoNotParallelize]
public sealed class PublicationLauncherTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StableLauncherWritesNativeSuccessAndRefusalReports(bool damaged)
    {
        using var fixture = new PublicationFixture();
        if (damaged) { fixture.Publications[0]!["sequence"] = 2; }
        fixture.SaveLedger();
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
        Assert.IsTrue(File.Exists(Path.Combine(repository, "eng/check-publications.ps1")));
        byte[] before = File.ReadAllBytes(fixture.LedgerPath);
        ProcessResult result = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", Path.Combine(repository, "eng/check-publications.ps1"),
            "-LedgerPath", fixture.LedgerPath, "-ArtifactRoot", fixture.Root, "-OutputPath", fixture.ValidationPath], repository, TimeSpan.FromSeconds(90));
        Assert.AreEqual(damaged ? 2 : 0, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonObject report = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!.AsObject();
        Assert.AreEqual(damaged ? 2 : 0, report["exitCode"]!.GetValue<int>());
        Assert.IsFalse(report["compatibilityQualified"]!.GetValue<bool>());
        if (damaged) { Assert.AreEqual("InvalidSequence", report["issues"]![0]!["code"]!.GetValue<string>()); }
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.LedgerPath));
    }
}
