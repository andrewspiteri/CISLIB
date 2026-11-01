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
        ProviderReportCapture captured = await ProviderReaderFixture.GetAsync(family, mode, TestContext);
        JsonObject reordered = captured.Report.DeepClone().AsObject();
        Wire(reordered, row =>
        {
            var attributes = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> item in row["attributes"]!.AsObject().Reverse()) { attributes[item.Key] = item.Value?.DeepClone(); }
            row["attributes"] = attributes;
        }, key: family == "provider-host-filter" ? "fileBytes" : "bytes");
        Validate(family, mode, reordered, captured.ConsoleText, captured.ProcessId);
    }
    [TestMethod]
    [DataRow("provider-operation", "protected", "env", false)]
    [DataRow("provider-operation", "protected", "env", true)]
    [DataRow("provider-operation", "protected", "version", false)]
    [DataRow("provider-operation", "protected", "version", true)]
    [DataRow("provider-host-story", "factory-precedence", "env", false)]
    [DataRow("provider-host-story", "factory-precedence", "env", true)]
    [DataRow("provider-host-story", "factory-precedence", "version", false)]
    [DataRow("provider-host-story", "factory-precedence", "version", true)]
    public async Task ProtectedIdentityMustRemainPresentAndNonNull(string family, string mode, string field, bool remove)
    {
        ProviderReportCapture captured = await ProviderReaderFixture.GetAsync(family, mode, TestContext);
        JsonObject changed = captured.Report.DeepClone().AsObject();
        Wire(changed, row => { if (remove) { row.Remove(field); } else { row[field] = null; } });
        string reason = family == "provider-operation" ? "protected identity and Activity" : "protected Activity and identity";
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => Validate(family, mode, changed, captured.ConsoleText, captured.ProcessId)).Message);
    }

}
