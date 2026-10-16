using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("value-qualification-cases.json")["cases"]!.AsArray().Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualScalarErrorsAndMutationVectorsExecuteInFreshProcesses(string id)
    {
        string input = LogValueProcessTests.Find("value-qualification-cases.json");
        JsonObject row = LogValueProcessTests.Read("value-qualification-cases.json")["cases"]!.AsArray().Single(value => (string?)value!["id"] == id)!.AsObject();
        JsonObject actual = await LogValueProcessTests.Run("log-qualification", input, id, TestContext);
        foreach ((string key, JsonNode? expected) in row["expected"]!.AsObject()) { Assert.IsTrue(JsonNode.DeepEquals(expected, actual[key]), key); }
        Assert.IsTrue(actual["rejectedInputNotEchoed"]!.GetValue<bool>());
        Assert.IsTrue(actual["rangeActualValueAbsent"]!.GetValue<bool>());
        Assert.IsTrue(actual["factoryCalls"]!.GetValue<int>() > 0, "No actual Core constructor invocation.");
        if (!actual["valid"]!.GetValue<bool>()) { Assert.IsTrue((string?)actual["nativeException"] is "ArgumentException" or "ArgumentNullException" or "ArgumentOutOfRangeException"); }
    }
}
