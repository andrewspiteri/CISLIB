using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueNormalizationProcessTests
{
    private static readonly string[] OriginalKinds = ["null", "boolean", "string", "number", "integer", "integer-string", "decimal-string", "array", "map"];
    public TestContext TestContext { get; set; } = null!;

    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read()["cases"]!.AsArray()
        .Where(row => !OriginalKinds.Contains(row!["input"]!["kind"]!.GetValue<string>(), StringComparer.Ordinal))
        .Select(row => new object[] { "event-value-cases.json", row!["id"]!.GetValue<string>() })
        .Concat(LogValueProcessTests.Read("value-normalization-cases.json")["cases"]!.AsArray()
            .Select(row => new object[] { "value-normalization-cases.json", row!["id"]!.GetValue<string>() }));

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task NormalizedScalarsAndUnsupportedTransportExecuteInFreshProcesses(string catalog, string id)
    {
        await LogValueProcessTests.Verify(LogValueProcessTests.Find(catalog), id, TestContext);
    }
}
