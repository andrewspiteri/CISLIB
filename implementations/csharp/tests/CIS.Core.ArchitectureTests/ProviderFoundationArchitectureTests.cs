using System.Reflection;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ProviderFoundationArchitectureTests
{
    [TestMethod]
    public void ProviderHasConcreteMethodsInTheSingleCoreAssembly()
    {
        Type type = typeof(CoreLoggerProvider); Assert.IsTrue(type.IsSealed);
        Assert.AreSame(typeof(CoreLogRuntime).Assembly, type.Assembly); Assert.AreEqual("CIS.Logging.MicrosoftLogger", type.Namespace);
        Assert.IsTrue(typeof(ILoggerProvider).IsAssignableFrom(type)); Assert.IsTrue(typeof(ISupportExternalScope).IsAssignableFrom(type));
        ConstructorInfo[] constructors = type.GetConstructors(); Assert.AreEqual(1, constructors.Length);
        Assert.AreEqual(typeof(CoreLogRuntime), constructors[0].GetParameters().Single().ParameterType); Assert.AreEqual("runtime", constructors[0].GetParameters().Single().Name);
        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        CollectionAssert.AreEquivalent(new[] { "CreateLogger", "SetScopeProvider", "Dispose" }, methods.Select(x => x.Name).ToArray());
        Assert.AreEqual(typeof(ILogger), type.GetMethod("CreateLogger")!.ReturnType); Assert.AreEqual("categoryName", type.GetMethod("CreateLogger")!.GetParameters().Single().Name);
        foreach (MethodBase method in methods.Cast<MethodBase>().Concat(constructors)) { Assert.IsNotNull(method.GetMethodBody()); Assert.IsFalse(method.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall)); }
    }
    [TestMethod]
    public void DirectCoreLoggingSurfaceDoesNotExposeMicrosoftTypes()
    {
        foreach (Type type in typeof(CoreLogger).Assembly.GetExportedTypes().Where(t => t.Namespace is not null && t.Namespace.StartsWith("CIS.Logging", StringComparison.Ordinal) && !t.Namespace.StartsWith("CIS.Logging.MicrosoftLogger", StringComparison.Ordinal)))
        {
            IEnumerable<Type> signatures = type.GetInterfaces().Concat(type.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
                .Concat(type.GetProperties().Select(p => p.PropertyType));
            Assert.IsFalse(signatures.SelectMany(Types).Any(t => t.Namespace?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true), type.FullName);
        }
    }
    private static IEnumerable<Type> Types(Type type)
    {
        yield return type;
        if (type.HasElementType) { foreach (Type element in Types(type.GetElementType()!)) { yield return element; } }
        if (type.IsGenericType) { foreach (Type argument in type.GetGenericArguments().SelectMany(Types)) { yield return argument; } }
    }
}
