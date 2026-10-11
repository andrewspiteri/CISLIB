using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class LogValueObjectBoundaryTests
{
    [TestMethod]
    public void ActualValueGraphHasNoArbitraryFormattingReflectionOrSerializationCalls()
    {
        Type[] types = typeof(LogValue).Assembly.GetTypes()
            .Where(type => type == typeof(LogValue) || type.Namespace == "CIS.Logging.Values").ToArray();
        int checkedCalls = 0;
        var typedFormatters = new HashSet<Type>();
        foreach (Type type in types)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> methods = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
            if (type.TypeInitializer is { } initializer)
            {
                methods = methods.Append(initializer).DistinctBy(method => method.MetadataToken);
            }
            foreach (MethodBase method in methods)
            {
                foreach (MethodBase called in ConfigurationAmbientStateTests.Calls(method))
                {
                    checkedCalls++;
                    Assert.IsFalse(IsForbidden(called), $"{type.FullName}.{method.Name} calls {called.DeclaringType?.FullName}.{called.Name}");
                    if (called.Name == nameof(object.ToString))
                    {
                        typedFormatters.Add(called.DeclaringType!);
                    }
                }
            }
        }
        Assert.IsTrue(checkedCalls > 0);
        CollectionAssert.AreEquivalent(new[] { typeof(Guid), typeof(DateTime) }, typedFormatters.ToArray());
    }

    [TestMethod]
    public void CompiledObjectGuardRecognizesNeverInvokedNegativeControls()
    {
        MethodInfo probe = typeof(LogValueObjectBoundaryTests).GetMethod(nameof(UnsupportedProbe), BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] names = ConfigurationAmbientStateTests.Calls(probe).Where(IsForbidden)
            .Select(call => call.DeclaringType?.FullName + "." + call.Name).ToArray();
        foreach (string expected in new[] { "System.Object.ToString", "System.Object.GetType", "System.Type.GetProperties", "System.Text.Json.JsonSerializer.Serialize", "System.Convert.ToString", "System.IFormattable.ToString" })
        {
            Assert.IsTrue(names.Contains(expected, StringComparer.Ordinal), expected);
        }
    }

    [TestMethod]
    [DataRow("object")]
    [DataRow("bytes")]
    [DataRow("decimal")]
    [DataRow("reflection-type")]
    [DataRow("exception")]
    [DataRow("factory")]
    [DataRow("objects")]
    [DataRow("object-map")]
    public void ActualTypedApiHasNoFactoryAcceptingUnsupportedObjectGraphs(string shape)
    {
        Type unsupported = shape switch
        {
            "object" => typeof(object),
            "bytes" => typeof(byte[]),
            "decimal" => typeof(decimal),
            "reflection-type" => typeof(Type),
            "exception" => typeof(Exception),
            "factory" => typeof(Func<int>),
            "objects" => typeof(object[]),
            "object-map" => typeof(Dictionary<string, object>),
            _ => throw new InvalidDataException("Unknown negative control.")
        };
        MethodInfo[] factories = typeof(LogValue).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.IsFalse(factories.SelectMany(method => method.GetParameters())
            .Any(parameter => parameter.ParameterType.IsAssignableFrom(unsupported)), unsupported.FullName);
        Assert.IsFalse(factories.Any(method => method.IsGenericMethodDefinition));
        Type scalar = typeof(LogValue).Assembly.GetType("CIS.Logging.Values.LogValueScalar", throwOnError: true)!;
        Assert.IsFalse(scalar.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(object)));
    }

    private static bool IsForbidden(MethodBase called)
    {
        Type? type = called.DeclaringType;
        string? name = type?.FullName;
        if (name?.StartsWith("System.Reflection.", StringComparison.Ordinal) == true || type == typeof(Type))
        {
            return true;
        }
        if (type?.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true ||
            type?.Namespace?.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal) == true ||
            type?.Namespace?.StartsWith("System.Xml.Serialization", StringComparison.Ordinal) == true)
        {
            return true;
        }
        if (type == typeof(object) && called.Name == nameof(GetType))
        {
            return true;
        }
        return called.Name == nameof(ToString) && type != typeof(Guid) && type != typeof(DateTime);
    }

    // Only compiled calls are inspected. Invoking this would break the test's purpose.
    private static void UnsupportedProbe(object value)
    {
        _ = value.ToString();
        _ = value.GetType().GetProperties();
        _ = JsonSerializer.Serialize(value);
        _ = Convert.ToString(value, CultureInfo.InvariantCulture);
        _ = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
    }
}
