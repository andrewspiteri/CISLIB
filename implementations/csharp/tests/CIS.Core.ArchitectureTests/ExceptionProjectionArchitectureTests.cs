using System.Reflection;
using System.Text.Json;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ExceptionProjectionArchitectureTests
{
    [TestMethod]
    public void CapturedExceptionIsInternalImmutableAndRetainsNoMutableExceptionGraph()
    {
        Type model = typeof(LogValue).Assembly.GetType("CIS.Logging.Exceptions.CapturedException", true)!;
        Assert.IsFalse(model.IsPublic);
        Assert.IsTrue(model.IsSealed);
        foreach (FieldInfo field in model.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            Assert.IsTrue(field.IsInitOnly, field.Name);
            Assert.IsFalse(typeof(Exception).IsAssignableFrom(field.FieldType));
            Assert.IsFalse(field.FieldType == typeof(object) || field.FieldType == typeof(Type) || typeof(Delegate).IsAssignableFrom(field.FieldType));
        }
        Assert.IsTrue(model.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).All(property => property.SetMethod is null));
    }

    [TestMethod]
    public void ActualExceptionCaptureCallsOnlyDeclaredExceptionFieldsWithoutSerialization()
    {
        Type[] types = typeof(LogValue).Assembly.GetTypes().Where(type => type.Namespace == "CIS.Logging.Exceptions").ToArray();
        var calls = new List<MethodBase>();
        foreach (Type type in types)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                calls.AddRange(ConfigurationAmbientStateTests.Calls(method));
            }
        }
        foreach (MethodBase forbidden in calls.Where(Forbidden))
        {
            Assert.Fail($"Unexpected capture call: {forbidden.DeclaringType?.FullName}.{forbidden.Name}");
        }
        foreach (string required in new[] { "get_Message", "get_StackTrace", "get_InnerException", "get_InnerExceptions" })
        {
            Assert.IsTrue(calls.Any(call => call.Name == required), required);
        }
    }

    [TestMethod]
    public void CompiledCaptureGuardDetectsForbiddenNeverInvokedControls()
    {
        MethodInfo probe = typeof(ExceptionProjectionArchitectureTests).GetMethod(nameof(UnsupportedProbe), BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] calls = ConfigurationAmbientStateTests.Calls(probe).Where(Forbidden).Select(call => call.Name).ToArray();
        foreach (string name in new[] { "get_Data", "get_Source", "get_HelpLink", "ToString", "GetProperties", "Serialize" })
        {
            Assert.IsTrue(calls.Contains(name, StringComparer.Ordinal), name);
        }
    }

    private static bool Forbidden(MethodBase method)
    {
        if (method.DeclaringType == typeof(MemberInfo) && method.Name == "get_Name")
        {
            return false;
        }
        string? space = method.DeclaringType?.Namespace;
        return method.Name is "get_Data" or "get_Source" or "get_HelpLink" or "ToString" ||
            space?.StartsWith("System.Reflection", StringComparison.Ordinal) == true ||
            space?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true ||
            space?.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal) == true ||
            method.DeclaringType == typeof(Type) && method.Name != "get_FullName" && method.Name != "get_Name";
    }

    private static void UnsupportedProbe(Exception source)
    {
        _ = source.Data;
        _ = source.Source;
        _ = source.HelpLink;
        _ = source.ToString();
        _ = source.GetType().GetProperties();
        _ = JsonSerializer.Serialize(source);
    }
}
