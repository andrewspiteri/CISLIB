using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using CIS.Metrics;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class RestClientArchitectureTests
{
    [TestMethod]
    public void ClientHasExactlyNineTypedAsynchronousVerbSignatures()
    {
        MethodInfo[] methods = typeof(IRestClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        CollectionAssert.AreEquivalent(new[] { "Get", "Post", "Put", "Patch", "Delete", "Options", "Trace", "Head", "Send" },
            methods.Select(method => method.Name).ToArray());
        foreach (MethodInfo method in methods)
        {
            bool head = method.Name == "Head", send = method.Name == "Send";
            Assert.AreEqual(!head, method.IsGenericMethodDefinition, method.Name);
            Type value;
            if (head) { value = typeof(RestNoValue); }
            else
            {
                Type[] arguments = method.GetGenericArguments();
                Assert.HasCount(1, arguments, method.Name);
                Assert.HasCount(0, arguments[0].GetGenericParameterConstraints(), method.Name);
                Assert.AreEqual(GenericParameterAttributes.None,
                    arguments[0].GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask, method.Name);
                value = arguments[0];
            }
            Assert.AreEqual(typeof(Task<>).MakeGenericType(typeof(RestResult<>).MakeGenericType(value)), method.ReturnType, method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Type[] expected = send ? [typeof(string), typeof(string), typeof(RestRequestOptions), typeof(CancellationToken)]
                : [typeof(string), typeof(RestRequestOptions), typeof(CancellationToken)];
            CollectionAssert.AreEqual(expected, parameters.Select(parameter => parameter.ParameterType).ToArray(), method.Name);
            Assert.IsTrue(parameters.Take(send ? 2 : 1).All(parameter => !parameter.IsOptional), method.Name);
            Assert.IsTrue(parameters[^2].IsOptional && parameters[^2].HasDefaultValue, method.Name);
            Assert.IsNull(parameters[^2].DefaultValue, method.Name);
            Assert.AreEqual(NullabilityState.Nullable, new NullabilityInfoContext().Create(parameters[^2]).ReadState, method.Name);
            Assert.IsTrue(parameters[^1].IsOptional && parameters[^1].HasDefaultValue, method.Name);
        }
    }

    [TestMethod]
    public void OrdinaryConsumerSignaturesContainNoNativeHttpResilienceOrCollectorTypes()
    {
        Type[] types = typeof(IRestClient).Assembly.GetExportedTypes().Where(type => type.Namespace == "CIS.RestClient").ToArray();
        Assert.IsNotEmpty(types);
        foreach (Type type in types) { AssertOrdinary(type); }
    }

    [TestMethod]
    public void ResultHasExactlyTheFiveReadOnlyPropertiesWithoutAnInvalidPublicConstructionRoute()
    {
        Type result = typeof(RestResult<>);
        PropertyInfo[] properties = result.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        CollectionAssert.AreEquivalent(new[] { "Success", "ErrorCode", "ErrorMessage", "Value", "Context" }, properties.Select(property => property.Name).ToArray());
        Assert.IsTrue(properties.All(property => property.SetMethod is null));
        Assert.HasCount(0, result.GetConstructors());
        Assert.AreEqual(result.GetGenericArguments()[0], result.GetProperty("Value")!.PropertyType);
        Assert.AreEqual(typeof(int), typeof(RestResult<int>).GetProperty("Value")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(RestResult<string>).GetProperty("Value")!.PropertyType);
        Assert.AreEqual(typeof(bool), result.GetProperty("Success")!.PropertyType);
        Assert.AreEqual(typeof(string), result.GetProperty("ErrorCode")!.PropertyType);
        Assert.AreEqual(typeof(string), result.GetProperty("ErrorMessage")!.PropertyType);
        Assert.AreEqual(typeof(RestResultContext), result.GetProperty("Context")!.PropertyType);
        Assert.IsTrue(typeof(RestResultContext).GetProperties().All(property => property.SetMethod is null));
    }

    [TestMethod]
    public void CoreKeepsApprovedRuntimePackagesAndDoesNotReferenceOptionalAdapters()
    {
        Assembly core = typeof(IRestClient).Assembly;
        Assert.AreEqual("CIS.Core", core.GetName().Name);
        string[] references = core.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.IsFalse(references.Any(name => name.StartsWith("Polly", StringComparison.Ordinal)
            || name.StartsWith("OpenTelemetry", StringComparison.Ordinal) || name.StartsWith("Prometheus", StringComparison.Ordinal)
            || name is "CIS.RestClient.Resilience" or "CIS.Metrics.OpenTelemetry"
            || name.StartsWith("Microsoft.Extensions.Resilience", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions.Http.Resilience", StringComparison.Ordinal)), string.Join(", ", references));
        string language = LanguageRoot();
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(language, "eng/runtime-dependencies.json")));
        string[] expected = policy.RootElement.GetProperty("packages").EnumerateArray()
            .Where(package => package.GetProperty("role").GetString() == "direct")
            .Select(package => package.GetProperty("id").GetString()!).ToArray();
        XDocument project = XDocument.Load(Path.Combine(language, "src/CIS.Core/CIS.Core.csproj"));
        CollectionAssert.AreEquivalent(expected, project.Descendants("PackageReference").Select(item => (string)item.Attribute("Include")!).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Microsoft.AspNetCore.App" },
            project.Descendants("FrameworkReference").Select(item => (string)item.Attribute("Include")!).ToArray());
        Assert.IsTrue(project.Descendants("ProjectReference").All(item => (string?)item.Attribute("ReferenceOutputAssembly") == "false"),
            "Core cannot acquire a runtime project reference to an optional adapter.");
    }

    [TestMethod]
    public void PassiveCompositionMetadataHasNoProviderRuntimeOrFactoryTypes()
        => AssertPassive(typeof(RestClientComposition));

    [TestMethod]
    [DataRow(typeof(ProviderCompositionLeak))]
    [DataRow(typeof(RuntimeCompositionLeak))]
    public void PassiveMetadataBoundaryRejectsProviderAndRuntimeCapture(Type fixture)
        => Assert.ThrowsExactly<InvalidOperationException>(() => AssertPassive(fixture));

    [TestMethod]
    [DataRow(typeof(DirectHttpLeak))]
    [DataRow(typeof(NestedHttpLeak))]
    public void BoundaryDetectsDirectAndNestedNativeMessageLeaks(Type fixture)
        => Assert.ThrowsExactly<InvalidOperationException>(() => AssertOrdinary(fixture));

    private static void AssertOrdinary(Type source)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<Type> signatures = source.GetInterfaces()
            .Concat(source.GetConstructors().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(source.GetMethods(flags).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .Concat(source.GetProperties(flags).Select(property => property.PropertyType))
            .Concat(source.GetFields(flags).Select(field => field.FieldType));
        foreach (Type target in signatures.SelectMany(Contained))
        {
            string space = target.Namespace ?? "";
            if (space.StartsWith("System.Net.Http", StringComparison.Ordinal) || space.StartsWith("Polly", StringComparison.Ordinal)
                || space.StartsWith("OpenTelemetry", StringComparison.Ordinal) || space.StartsWith("System.Diagnostics.Metrics", StringComparison.Ordinal))
            { throw new InvalidOperationException(source.FullName + " leaks " + target.FullName); }
        }
    }
    private static IEnumerable<Type> Contained(Type type)
    {
        yield return type;
        if (type.HasElementType) { foreach (Type item in Contained(type.GetElementType()!)) { yield return item; } }
        if (type.IsGenericType) { foreach (Type item in type.GetGenericArguments().SelectMany(Contained)) { yield return item; } }
    }

    private static void AssertPassive(Type source)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<Type> signatures = source.GetFields(flags).Select(field => field.FieldType)
            .Concat(source.GetProperties(flags).Select(property => property.PropertyType))
            .Concat(source.GetConstructors(flags).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)));
        foreach (Type target in signatures.SelectMany(Contained))
        {
            if (typeof(IServiceProvider).IsAssignableFrom(target) || typeof(IRestClientRuntime).IsAssignableFrom(target)
                || typeof(IMetrics).IsAssignableFrom(target) || typeof(Delegate).IsAssignableFrom(target))
            { throw new InvalidOperationException(source.FullName + " retains live composition dependency " + target.FullName); }
        }
    }

    private static string LanguageRoot()
    {
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx"))) { language = language.Parent; }
        return language?.FullName ?? throw new DirectoryNotFoundException("The actual C# source checkout is required for dependency checks.");
    }

    public sealed class DirectHttpLeak { public System.Net.Http.HttpRequestMessage? Request { get; set; } }
    public sealed class NestedHttpLeak { public Func<Task<System.Net.Http.HttpResponseMessage>>? Callback { get; set; } }
    private sealed class ProviderCompositionLeak { internal Func<IServiceProvider>? Provider { get; set; } }
    private sealed class RuntimeCompositionLeak { internal IRestClientRuntime? Runtime { get; set; } }
}
