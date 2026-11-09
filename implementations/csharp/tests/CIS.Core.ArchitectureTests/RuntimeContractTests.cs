using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using CIS.Tests.ArchitectureTests.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class RuntimeContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    [TestMethod]
    public void ImplementedOutputDiagnosticsMatchTheUnpublishedReferenceCatalog()
        => VerifyImplementedOutputDiagnostics(null);

    [TestMethod]
    [DataRow("missing")]
    [DataRow("wrong-type")]
    public void ShippingOutputCatalogCheckRejectsActualMissingOrWrongRecoveryDeclarations(string mutation)
        => Assert.ThrowsExactly<AssertFailedException>(() => VerifyImplementedOutputDiagnostics(mutation));

    [TestMethod]
    public void ImplementedFlushResultsAndMethodMatchTheUnpublishedCatalog()
    {
        using JsonDocument surface = Read("implementations/csharp/verification/contracts/startup/runtime-surface.json");
        var selected = new System.Text.Json.Nodes.JsonObject();
        foreach (string name in new[] { "LogFlushResult", "LogOutputDrainResult", "LogOutputRejectionSnapshot", "LogIncomingSnapshot" })
        { selected[name] = System.Text.Json.Nodes.JsonNode.Parse(surface.RootElement.GetProperty("immutableOutputs").GetProperty(name).GetRawText()); }
        using JsonDocument contracts = JsonDocument.Parse(selected.ToJsonString());
        using var stream = File.OpenRead(typeof(CIS.Logging.LogFlushResult).Assembly.Location); using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Where(type => reader.GetString(type.Namespace) == "CIS.Logging" && selected.ContainsKey(reader.GetString(type.Name)))
            .ToDictionary(type => reader.GetString(type.Namespace) + '.' + reader.GetString(type.Name));
        VerifyImmutableOutputs(reader, contracts.RootElement, types, new ContractTypeNameProvider());
        MethodInfo method = typeof(CIS.Logging.CoreLogRuntime).GetMethod("FlushAsync")!;
        Assert.IsNotNull(method.GetMethodBody()); Assert.IsFalse(method.IsStatic);
        Assert.AreEqual(typeof(Task<CIS.Logging.LogFlushResult>), method.ReturnType);
        CollectionAssert.AreEqual(new[] { "timeout", "cancellationToken" }, method.GetParameters().Select(parameter => parameter.Name).ToArray());
        CollectionAssert.AreEqual(new[] { typeof(TimeSpan), typeof(CancellationToken) }, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        foreach (Type type in new[] { typeof(CIS.Logging.LogCompletion), typeof(CIS.Logging.LogWriterFlushStatus) })
        {
            JsonElement expected = surface.RootElement.GetProperty("enums").GetProperty(type.Name);
            CollectionAssert.AreEquivalent(expected.EnumerateObject().Select(item => item.Name).ToArray(), Enum.GetNames(type));
            foreach (JsonProperty item in expected.EnumerateObject()) { Assert.AreEqual(item.Value.GetInt32(), Convert.ToInt32(Enum.Parse(type, item.Name))); }
        }
    }

    private static void VerifyImplementedOutputDiagnostics(string? mutation)
    {
        using JsonDocument surface = Read("implementations/csharp/verification/contracts/startup/runtime-surface.json");
        var expected = System.Text.Json.Nodes.JsonNode.Parse(surface.RootElement.GetProperty("immutableOutputs").GetProperty("LogOutputDiagnostics").GetRawText())!.AsObject();
        if (mutation == "missing") { expected.Remove("RecoveredPartialSegment"); }
        if (mutation == "wrong-type") { expected["RecoveredPartialSegment"] = "long"; }
        using JsonDocument contracts = JsonDocument.Parse(new System.Text.Json.Nodes.JsonObject { ["LogOutputDiagnostics"] = expected }.ToJsonString());
        using var stream = File.OpenRead(typeof(CIS.Logging.LogOutputDiagnostics).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition output = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(type =>
            reader.GetString(type.Namespace) == "CIS.Logging" && reader.GetString(type.Name) == "LogOutputDiagnostics");
        var types = new Dictionary<string, TypeDefinition> { ["CIS.Logging.LogOutputDiagnostics"] = output };
        VerifyImmutableOutputs(reader, contracts.RootElement, types, new ContractTypeNameProvider());
    }

    private static void VerifyImmutableOutputs(MetadataReader reader, JsonElement contracts, Dictionary<string, TypeDefinition> types, ContractTypeNameProvider provider)
    {
        foreach (JsonProperty contract in contracts.EnumerateObject())
        {
            TypeDefinition type = types["CIS.Logging." + contract.Name];
            Assert.IsTrue((type.Attributes & TypeAttributes.Sealed) != 0);
            Assert.IsFalse(PublicMethods(reader, type).Any(method => reader.GetString(method.Name) == ".ctor"));
            PropertyDefinition[] properties = type.GetProperties().Select(reader.GetPropertyDefinition).ToArray();
            CollectionAssert.AreEquivalent(contract.Value.EnumerateObject().Select(property => property.Name).ToArray(), properties.Select(property => reader.GetString(property.Name)).ToArray());
            foreach (PropertyDefinition property in properties)
            {
                Assert.IsTrue(property.GetAccessors().Setter.IsNil);
                string expected = contract.Value.GetProperty(reader.GetString(property.Name)).GetString()!;
                if (expected is "string?" or "LogStopResult?" or "LogIncomingSnapshot?" or "LogFrontendBatchSnapshot?")
                {
                    expected = expected[..^1]; // Nullable reference annotations are additionally checked by the compiler consumers.
                }

                Assert.AreEqual(expected, property.DecodeSignature(provider, null).ReturnType);
            }
        }
    }

    private static IEnumerable<MethodDefinition> PublicMethods(MetadataReader reader, TypeDefinition type) => type.GetMethods().Select(reader.GetMethodDefinition).Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public);
    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, path)));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the contract checks from the CisLib checkout.");
    }
}
