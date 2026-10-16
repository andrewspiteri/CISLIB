using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.IntegrationTests.ProviderReaderVerifier;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderReaderCatalogTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("provider-scope")]
    [DataRow("provider-operation")]
    [DataRow("provider-ownership")]
    [DataRow("provider-host-filter")]
    [DataRow("provider-host-story")]
    public void NativeModesMatchTheSharedCatalogInOrder(string family)
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string path = Path.Combine(current.FullName, "conformance/logging/" + family + "-cases.json");
            if (!File.Exists(path)) { continue; }
            JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            CollectionAssert.AreEqual(Modes(family), catalog["cases"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToArray());
            return;
        }
        Assert.Fail("Shared provider catalog was not found: " + family);
    }

    [TestMethod]
    [DataRow("provider-scope", "owned")]
    [DataRow("provider-operation", "owned")]
    [DataRow("provider-ownership", "shared")]
    [DataRow("provider-host-filter", "broad")]
    [DataRow("provider-host-story", "async-owned")]
    public async Task EquivalentAttributePropertyOrderIsAccepted(string family, string mode)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync(family, mode, TestContext);
        Validate(family, mode, captured.Report, captured.ConsoleText, captured.ProcessId);
        JsonObject reordered = captured.Report.DeepClone().AsObject();
        Wire(reordered, row =>
        {
            var attributes = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> item in row["attributes"]!.AsObject().Reverse()) { attributes[item.Key] = item.Value?.DeepClone(); }
            row["attributes"] = attributes;
        }, key: family == "provider-host-filter" ? "fileBytes" : "bytes");
        Validate(family, mode, reordered, captured.ConsoleText, captured.ProcessId);
    }
}
