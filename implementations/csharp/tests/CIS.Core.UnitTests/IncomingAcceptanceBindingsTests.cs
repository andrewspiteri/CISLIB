using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingAcceptanceBindingsTests
{
    [TestMethod]
    public void AllAcceptanceCasesBindToRealNativeMethods()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "conformance/logging/case-inventory.json"))) { root = root.Parent; }
        Assert.IsNotNull(root);
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/logging/case-inventory.json")));
        using var feature = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/logging/priority-spool-acceptance.json")));
        var cases = inventory.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(row => row.GetProperty("id").GetString()!);
        JsonElement[] acceptance = feature.RootElement.GetProperty("acceptance").EnumerateArray().ToArray();
        JsonElement[] requirements = feature.RootElement.GetProperty("requirements").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(Enumerable.Range(1, 34).Select(number => $"AC{number:00}").ToArray(),
            acceptance.Select(row => row.GetProperty("id").GetString()).ToArray());
        CollectionAssert.AreEquivalent(Enumerable.Range(1, 17).Select(number => $"LOG-{number:00}").ToArray(),
            requirements.Select(row => row.GetProperty("id").GetString()).ToArray());
        foreach (JsonElement row in acceptance.Concat(requirements))
        {
            string[] bindings = row.GetProperty("caseIds").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.IsGreaterThan(0, bindings.Length);
            foreach (string id in bindings)
            {
                Assert.IsTrue(cases.TryGetValue(id, out JsonElement bound), "Unknown native case: " + id);
                Assert.AreEqual("Implemented", bound.GetProperty("implementation").GetString(), id);
                Assert.IsGreaterThan(0, bound.GetProperty("minimumExecutions").GetInt32(), id);
                if (bound.GetProperty("family").GetString() != "unit" || !bound.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String) { continue; }
                string qualified = method.GetString()!; int separator = qualified.LastIndexOf('.');
                Type? type = typeof(IncomingAcceptanceBindingsTests).Assembly.GetType(qualified[..separator]);
                Assert.IsNotNull(type, qualified);
                MethodInfo? target = type.GetMethod(qualified[(separator + 1)..], BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(target, qualified);
                Assert.IsTrue(target.IsDefined(typeof(TestMethodAttribute), inherit: true), qualified);
            }
        }
    }
}
