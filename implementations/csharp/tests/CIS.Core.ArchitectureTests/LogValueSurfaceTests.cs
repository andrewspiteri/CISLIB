using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class LogValueSurfaceTests
{
    [TestMethod]
    public void ActualValueSurfaceHasNineRealTypedFactoriesAndNoPublicMutablePayload()
    {
        Type type = typeof(LogValue);
        Assert.IsTrue(type.IsPublic && type.IsSealed);
        Assert.AreEqual(0, type.GetConstructors().Length);
        CollectionAssert.AreEquivalent(new[] { "Kind", "IsTruncated" }, type.GetProperties().Select(property => property.Name).ToArray());
        Assert.IsTrue(type.GetProperties().All(property => property.GetSetMethod(nonPublic: true) is null));
        MethodInfo[] factories = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        CollectionAssert.AreEquivalent(new[] { "Null", "Boolean", "String", "Number", "Integer", "IntegerString", "DecimalString", "Array", "Map" }, factories.Select(method => method.Name).ToArray());
        Assert.IsTrue(factories.All(method => method.ReturnType == type && method.GetMethodBody() is not null));
        Assert.IsTrue(factories.SelectMany(method => method.GetParameters()).All(parameter => parameter.ParameterType != typeof(object)));
        Assert.IsTrue(type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).All(field => field.IsPrivate && field.IsInitOnly));
        Assert.AreEqual(typeof(bool), type.GetMethod("Boolean")!.GetParameters().Single().ParameterType);
        Assert.AreEqual(typeof(double), type.GetMethod("Number")!.GetParameters().Single().ParameterType);
        Assert.AreEqual(typeof(long), type.GetMethod("Integer")!.GetParameters().Single().ParameterType);
        foreach (string name in new[] { "String", "IntegerString", "DecimalString" })
        {
            Assert.AreEqual(typeof(string), type.GetMethod(name)!.GetParameters().Single().ParameterType);
        }
    }

    [TestMethod]
    public void ActualValueKindNumbersMatchTheUnchangedReferenceSpecification()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null && path is null)
        {
            string candidate = Path.Combine(directory.FullName, "implementations", "csharp", "verification", "contracts", "startup", "surface.json");
            if (File.Exists(candidate))
            {
                path = candidate;
            }
            directory = directory.Parent;
        }
        Assert.IsNotNull(path);
        JsonObject enums = JsonNode.Parse(File.ReadAllText(path))!["enums"]!.AsObject();
        JsonObject members = enums["CIS.Logging.LogValueKind"]!.AsObject();
        CollectionAssert.AreEquivalent(members.Select(pair => pair.Key).ToArray(), Enum.GetNames<LogValueKind>());
        foreach ((string name, JsonNode? number) in members)
        {
            Assert.AreEqual(number!.GetValue<int>(), (int)Enum.Parse<LogValueKind>(name));
        }
    }
}
