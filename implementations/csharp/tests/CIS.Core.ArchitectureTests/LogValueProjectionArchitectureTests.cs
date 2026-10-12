using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class LogValueProjectionArchitectureTests
{
    private static Type Projection => typeof(LogValue).Assembly.GetType("CIS.Logging.Values.LogValueProjection", throwOnError: true)!;

    [TestMethod]
    public void ActualShapeLimitsMatchThePreservedNeutralContract()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null && path is null)
        {
            string candidate = Path.Combine(directory.FullName, "contracts", "logging", "v1", "event-contract.json");
            if (File.Exists(candidate))
            {
                path = candidate;
            }
            directory = directory.Parent;
        }
        Assert.IsNotNull(path);
        JsonNode limits = JsonNode.Parse(File.ReadAllText(path))!["bounds"]!;
        Assert.AreEqual(limits["attributeNodesIncludingRoot"]!.GetValue<int>(), (int)Projection.GetField("MaximumNodes", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!);
        Assert.AreEqual(limits["attributeDepthIncludingRoot"]!.GetValue<int>(), (int)Projection.GetField("MaximumDepth", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!);
        Assert.AreEqual(1, typeof(LogValue).GetProperty("NodeCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(LogValue.Null()));
        Assert.AreEqual(1, typeof(LogValue).GetProperty("Depth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(LogValue.Array([])));
        Assert.AreEqual(1, typeof(LogValue).GetProperty("NodeCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(LogValue.Map(new Dictionary<string, LogValue>())));
    }

    [TestMethod]
    public void StringKeyAndIdentityValidationCallTheSameCompiledScalarHelper()
    {
        foreach ((string typeName, string member) in new[] { ("CIS.Logging.Values.LogValueText", "IsValidUnicode"), ("CIS.Logging.Values.LogValueText", "IsValidKey"), ("CIS.Logging.EventMetadata", "CheckIdentity"), ("CIS.Configuration.EnvironmentOverrides", "Identity") })
        {
            Type type = typeof(LogValue).Assembly.GetType(typeName, throwOnError: true)!;
            MethodInfo method = type.GetMethod(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.IsTrue(ConfigurationAmbientStateTests.Calls(method).Any(called => called.DeclaringType?.FullName == "CIS.Text.UnicodeScalarText" && called.Name == "IsValid"));
        }
    }

    [TestMethod]
    [DataRow("Array")]
    [DataRow("Map")]
    public void EachPublicCollectionFactoryAllocatesANewPrivateBudget(string name)
    {
        MethodInfo method = typeof(LogValue).GetMethod(name)!;
        Assert.AreEqual(1, ConfigurationAmbientStateTests.Calls(method).Count(call => call.IsConstructor && call.DeclaringType == Projection));
        Assert.IsFalse(Projection.IsPublic);
        Assert.IsTrue(Projection.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).All(field => field.IsLiteral));
        foreach (string property in new[] { "NodeCount", "Depth", "IsTruncated" })
        {
            PropertyInfo info = typeof(LogValue).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.IsNull(info.GetSetMethod(nonPublic: true));
        }
    }
}
