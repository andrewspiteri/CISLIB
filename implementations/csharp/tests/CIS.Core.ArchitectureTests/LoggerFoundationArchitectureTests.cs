using System.Reflection;
using System.Text.Json;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class LoggerFoundationArchitectureTests
{
    [TestMethod]
    public void ImplementedLoggerMethodsHaveRealBodiesAndNamedParameters()
    {
        Assert.IsTrue(typeof(CoreLogger).IsSealed);
        Assert.IsTrue(typeof(CoreLogRuntime).IsSealed);
        Assert.AreEqual(0, typeof(CoreLogger).GetConstructors().Length);
        Assert.AreEqual(0, typeof(CoreLogRuntime).GetConstructors().Length);
        var methods = typeof(CoreLogger).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        CollectionAssert.AreEquivalent(new[] { "IsEnabled", "Write", "WriteLazy" }, methods.Select(method => method.Name).ToArray());
        foreach (MethodInfo method in methods.Append(typeof(CoreLogRuntime).GetMethod("CreateLogger")!))
        {
            Assert.IsNotNull(method.GetMethodBody());
            Assert.IsFalse(method.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall));
        }
        CollectionAssert.AreEqual(new[] { "level", "data" }, typeof(CoreLogger).GetMethod("Write")!.GetParameters().Select(item => item.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "level", "factory" }, typeof(CoreLogger).GetMethod("WriteLazy")!.GetParameters().Select(item => item.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "CreateLogger", "GetDiagnostics" }, typeof(CoreLogRuntime).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name).ToArray());
        Assert.IsNull(typeof(LogEventData).GetProperty("Level"));
        Assert.IsNull(typeof(LogEventData).GetProperty("Severity"));
    }

    [TestMethod]
    public void ResultMembersAreReadonlyAndEnumNumbersMatchThePreservedContract()
    {
        foreach (Type type in new[] { typeof(LogWriteResult), typeof(LogOutputWriteResult) })
        {
            Assert.IsTrue(type.IsSealed);
            Assert.AreEqual(0, type.GetConstructors().Length);
            foreach (PropertyInfo property in type.GetProperties()) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        }
        string root = EventContractTestsRoot();
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/logging/v1/runtime-contract.json")));
        foreach ((Type type, string section) in new[] { (typeof(LogWriteOutcome), "outcomes"), (typeof(LogOutputWriteStatus), "outputStatuses") })
        {
            foreach (JsonProperty property in contract.RootElement.GetProperty(section).EnumerateObject()) { Assert.AreEqual(property.Value.GetInt32(), Convert.ToInt32(Enum.Parse(type, property.Name))); }
        }
    }

    [TestMethod]
    public void OutputAdmissionInterfaceIsInternalAndReceivesOnlyOwnedEncodedEvents()
    {
        Type seam = typeof(CoreLogger).Assembly.GetType("CIS.Logging.Runtime.ILogOutputAdmission")!;
        Assert.IsTrue(seam.IsInterface);
        Assert.IsFalse(seam.IsPublic);
        Type payload = seam.GetMethod("TryAdmit")!.GetParameters()[0].ParameterType;
        Assert.AreEqual("CIS.Logging.Events.EncodedEvent", payload.FullName);
        Assert.IsFalse(payload.IsPublic);
    }
    private static string EventContractTestsRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "contracts/logging/v1/runtime-contract.json"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException("Repository root unavailable.");
    }
}
