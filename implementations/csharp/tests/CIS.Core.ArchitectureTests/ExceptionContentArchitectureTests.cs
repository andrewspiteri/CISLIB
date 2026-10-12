using System.Reflection;
using System.Text.Json;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ExceptionContentArchitectureTests
{
    private static readonly string[] AllowedGetters = ["get_Message", "get_StackTrace", "get_InnerException", "get_InnerExceptions", "GetType"];

    [TestMethod]
    public void ActualCaptureReadsOnlyTheDeclaredExceptionProperties()
    {
        MethodBase[] properties = ActualCalls().Where(IsExceptionMember).ToArray();
        Assert.IsTrue(properties.Length >= 4);
        foreach (MethodBase property in properties)
        {
            Assert.IsTrue(AllowedGetters.Contains(property.Name, StringComparer.Ordinal), property.Name);
        }
        foreach (string name in AllowedGetters)
        {
            Assert.IsTrue(properties.Any(property => property.Name == name), name);
        }
    }

    [TestMethod]
    public void ActualCaptureHasNoAmbientHttpConfigurationOrSerializerCalls()
    {
        foreach (MethodBase call in ActualCalls())
        {
            Assert.IsFalse(IsAmbientOrSerializer(call), $"{call.DeclaringType?.FullName}.{call.Name}");
        }
    }

    [TestMethod]
    public void CompiledNeverInvokedControlsDetectExcludedPropertiesAndAmbientReads()
    {
        MethodInfo probe = typeof(ExceptionContentArchitectureTests).GetMethod(nameof(Unsupported), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodBase[] calls = ConfigurationAmbientStateTests.Calls(probe).ToArray();
        string[] forbidden = calls.Where(call => IsExceptionMember(call) && !AllowedGetters.Contains(call.Name, StringComparer.Ordinal) ||
            IsAmbientOrSerializer(call)).Select(call => call.Name).ToArray();
        foreach (string name in new[] { "get_Data", "get_Source", "get_HelpLink", "get_TargetSite", "get_HResult",
            "ToString", "GetBaseException", "GetEnvironmentVariables", "get_Logging", "get_Headers", "get_Content", "Serialize" })
        {
            Assert.IsTrue(forbidden.Contains(name, StringComparer.Ordinal), name);
        }
    }

    private static IEnumerable<MethodBase> ActualCalls()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (Type type in typeof(LogValue).Assembly.GetTypes().Where(type => type.Namespace == "CIS.Logging.Exceptions"))
        {
            foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                foreach (MethodBase call in ConfigurationAmbientStateTests.Calls(method))
                {
                    yield return call;
                }
            }
        }
    }

    private static bool IsExceptionMember(MethodBase call) =>
        call is not ConstructorInfo && call.DeclaringType is { } type &&
        (typeof(Exception).IsAssignableFrom(type) || (type == typeof(object) && call.Name == nameof(ToString)));

    private static bool IsAmbientOrSerializer(MethodBase call)
    {
        Type? type = call.DeclaringType;
        string? space = type?.Namespace;
        return type == typeof(Environment) || type == typeof(EnvironmentOptions) || type == typeof(System.Diagnostics.Process) ||
            space == "CIS.Configuration" || space?.StartsWith("System.Net.Http", StringComparison.Ordinal) == true ||
            space?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true ||
            space?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true ||
            space?.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal) == true;
    }

    private static void Unsupported(Exception source, HttpRequestMessage request)
    {
        _ = source.Data;
        _ = source.Source;
        _ = source.HelpLink;
        _ = source.TargetSite;
        _ = source.HResult;
        _ = source.ToString();
        _ = source.GetBaseException();
        _ = Environment.GetEnvironmentVariables();
        _ = EnvironmentOptions.Logging;
        _ = request.Headers;
        _ = request.Content;
        _ = JsonSerializer.Serialize(source);
    }
}
