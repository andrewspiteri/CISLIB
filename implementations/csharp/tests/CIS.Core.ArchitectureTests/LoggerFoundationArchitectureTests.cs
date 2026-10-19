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
        var expected = new[] { "IsEnabled", "Write", "Write", "Write", "Write", "Write", "WriteLazy" }.Concat(new[] { "Trace", "Debug", "Info", "Warn", "Error", "Critical" }
            .SelectMany(name => new[] { name, name, name, name, name, name, name + "Lazy" })).ToArray();
        CollectionAssert.AreEquivalent(expected, methods.Select(method => method.Name).ToArray());
        var runtimeMethods = typeof(CoreLogRuntime).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (MethodInfo method in methods.Concat(runtimeMethods))
        {
            Assert.IsNotNull(method.GetMethodBody());
            Assert.IsFalse(method.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall));
        }
        CollectionAssert.AreEqual(new[] { "level", "data" }, typeof(CoreLogger).GetMethod("Write", [typeof(CoreLogLevel), typeof(LogEventData)])!.GetParameters().Select(item => item.Name).ToArray());
        Assert.AreEqual(22, methods.Count(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(LogInterpolatedStringHandler).MakeByRefType())));
        Assert.IsTrue(typeof(LogInterpolatedStringHandler).IsValueType);
        Assert.IsFalse(typeof(LogInterpolatedStringHandler).IsByRefLike);
        Assert.IsNotNull(typeof(LogInterpolatedStringHandler).GetCustomAttribute<System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute>());
        CollectionAssert.AreEqual(new[] { "level", "factory" }, typeof(CoreLogger).GetMethod("WriteLazy")!.GetParameters().Select(item => item.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Create", "Create", "Create", "TryCreate", "TryCreate", "TryCreate", "get_Completion", "CreateLogger", "BeginScope", "GetDiagnostics", "FlushAsync", "StopAsync", "DisposeAsync" }, runtimeMethods.Select(method => method.Name).ToArray());
        foreach (MethodInfo start in runtimeMethods.Where(method => method.Name == "TryCreate"))
        {
            Assert.AreEqual(typeof(LogStartResult), start.ReturnType);
            ParameterInfo handle = start.GetParameters().Single(parameter => parameter.IsOut);
            Assert.AreEqual("runtime", handle.Name); Assert.AreEqual(typeof(CoreLogRuntime).MakeByRefType(), handle.ParameterType);
        }
        Assert.IsNotNull(typeof(CoreLogRuntime).GetMethod("Create", Type.EmptyTypes));
        MethodInfo registered = typeof(CoreLogRuntime).GetMethod("Create", [typeof(IEnumerable<LogWriterRegistration>)])!;
        Assert.AreEqual("writers", registered.GetParameters().Single().Name);
        Assert.AreEqual(typeof(Task), typeof(CoreLogRuntime).GetProperty("Completion")!.PropertyType);
        Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ILogWriter)));
        CollectionAssert.AreEquivalent(new[] { "WriteAsync", "FlushAsync" }, typeof(ILogWriter).GetMethods().Select(method => method.Name).ToArray());
        foreach (PropertyInfo property in typeof(LogWriterRegistration).GetProperties()) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        MethodInfo scope = typeof(CoreLogRuntime).GetMethod("BeginScope")!;
        Assert.AreEqual(typeof(IDisposable), scope.ReturnType);
        CollectionAssert.AreEqual(new[] { "context" }, scope.GetParameters().Select(item => item.Name).ToArray());
        Assert.AreEqual(typeof(OperationContextData), scope.GetParameters().Single().ParameterType);
        CollectionAssert.AreEqual(new[] { "cancellationToken" }, typeof(CoreLogRuntime).GetMethod("StopAsync")!.GetParameters().Select(item => item.Name).ToArray());
        Assert.AreEqual(typeof(CancellationToken), typeof(CoreLogRuntime).GetMethod("StopAsync")!.GetParameters()[0].ParameterType);
        Assert.AreEqual(typeof(Task<LogStopResult>), typeof(CoreLogRuntime).GetMethod("StopAsync")!.ReturnType);
        Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(typeof(CoreLogRuntime)));
        Assert.AreEqual(typeof(ValueTask), typeof(CoreLogRuntime).GetMethod("DisposeAsync")!.ReturnType);
        Assert.AreEqual(0, typeof(CoreLogRuntime).GetMethod("DisposeAsync")!.GetParameters().Length);
        Assert.IsNull(typeof(LogEventData).GetProperty("Level"));
        Assert.IsNull(typeof(LogEventData).GetProperty("Severity"));
    }

    [TestMethod]
    public void ResultMembersAreReadonlyAndEnumNumbersMatchThePreservedContract()
    {
        foreach (Type type in new[] { typeof(LogWriteResult), typeof(LogOutputWriteResult), typeof(LogStopResult),
            typeof(LogStartResult), typeof(LogFrontendBatchSnapshot), typeof(LogIncomingSnapshot), typeof(LogFlushResult) })
        {
            Assert.IsTrue(type.IsSealed);
            Assert.AreEqual(0, type.GetConstructors().Length);
            foreach (PropertyInfo property in type.GetProperties()) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        }
        CollectionAssert.AreEquivalent(new[] { "Completion", "Succeeded", "LossObserved", "BoundaryCapturedAtUtc", "ObservedAtUtc", "InvalidEventCount", "TruncatedEventCount", "Outputs", "Incoming", "ErrorCode" },
            typeof(LogStopResult).GetProperties().Select(property => property.Name).ToArray());
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
