using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConsoleOwnershipTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Where(row => !row!["physical"]!.GetValue<bool>()).Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task BorrowedStreamsSurviveClosureFaultAndReuseWithSerializedRecords(string id)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, id);
        ConsoleOwnershipExpectations.Validate(catalog, row, await ConsoleOwnershipFixture.ExecuteAsync(row));
    }
    [TestMethod]
    [DataRow("dispose")]
    [DataRow("overlap")]
    [DataRow("ticket")]
    [DataRow("order")]
    [DataRow("bytes")]
    [DataRow("stream")]
    [DataRow("state")]
    [DataRow("fault")]
    [DataRow("held")]
    [DataRow("prefix")]
    [DataRow("catalog-written")]
    [DataRow("catalog-failed")]
    [DataRow("catalog-abandoned")]
    public async Task OwnershipMatcherRejectsDisposalOverlapOrderAndDispositionDamage(string fault)
    {
        bool failingRow = fault is "fault" or "prefix" or "catalog-failed" or "catalog-abandoned";
        JsonObject catalog = Catalog(), row = Row(catalog, failingRow ? "BORROW-FAULT-10" : "BORROW-DRAIN-10"), actual = await ConsoleOwnershipFixture.ExecuteAsync(row);
        if (fault.StartsWith("catalog-", StringComparison.Ordinal)) { string key = fault[8..]; row["expected"]![key] = row["expected"]![key]!.GetValue<int>() + 1; }
        else { ConsoleOwnershipExpectations.Damage(actual, fault); }
        string message = fault switch
        {
            "dispose" => "Borrowed disposedBeforeOwner differs.",
            "overlap" => "Borrowed maxActive differs.",
            "ticket" => "Successful ticket set differs.",
            "order" or "bytes" => "JSON facts/types/ticket order differ.",
            "stream" => "Selected application marker missing.",
            "held" => "Held stream closure/pending/serialization differs.",
            "prefix" => "Failed write prefix/retry differs.",
            _ => "Complete console disposition differs."
        };
        InvalidDataException rejected = Assert.ThrowsExactly<InvalidDataException>(() => ConsoleOwnershipExpectations.Validate(catalog, row, actual));
        Assert.AreEqual(message, rejected.Message);
    }
    private static JsonObject Row(JsonObject catalog, string id) => catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        { string path = Path.Combine(current.FullName, "conformance", "logging", "console-ownership-cases.json"); if (File.Exists(path)) { return JsonNode.Parse(File.ReadAllText(path))!.AsObject(); } }
        throw new FileNotFoundException("Console ownership catalog missing.");
    }
}
