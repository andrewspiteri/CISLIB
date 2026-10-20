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
    private static readonly JsonDocument Admissions = Read("conformance/logging/admission-cases.json");
    private static readonly JsonDocument Drains = Read("conformance/logging/drain-result-cases.json");

    public static IEnumerable<object[]> AdmissionCases => Cases(Admissions);
    public static IEnumerable<object[]> DrainCases => Cases(Drains);

    [TestMethod]
    [DynamicData(nameof(AdmissionCases))]
    public void AdmissionFixturesMatchTheSpecifiedDecisionOrder(string id)
    {
        JsonElement fixture = Find(Admissions, id);
        JsonElement observed = AdmissionContractEvaluator.Evaluate(fixture.GetProperty("inputs"));
        Assert.IsTrue(JsonElement.DeepEquals(fixture.GetProperty("expected"), observed), id + ": " + observed.GetRawText());
    }

    [TestMethod]
    [DynamicData(nameof(DrainCases))]
    public void DrainProjectionsHaveTruthfulFixedBoundaryResults(string id)
    {
        JsonElement fixture = Find(Drains, id);
        Assert.AreEqual(fixture.GetProperty("expectedValid").GetBoolean(), DrainProjectionValidator.Accepts(fixture), id);
    }

    [TestMethod]
    public void LifecycleExpectationsPreserveBoundedOwnershipAndDeadlines()
    {
        using JsonDocument lifecycle = Read("conformance/logging/runtime-lifecycle-expectations.json");
        Assert.IsTrue(lifecycle.RootElement.GetProperty("scope").GetString()!.Contains("no asynchronous", StringComparison.Ordinal));
        foreach (JsonDocument catalog in new[] { Admissions, Drains, lifecycle })
        {
            JsonElement[] cases = catalog.RootElement.GetProperty("cases").EnumerateArray().ToArray();
            Assert.AreEqual(catalog.RootElement.GetProperty("caseCount").GetInt32(), cases.Length);
            Assert.AreEqual(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
        }

        JsonElement deadline = Find(lifecycle, "LIFECYCLE-SHARED-DEADLINE");
        Assert.AreEqual(deadline.GetProperty("inputs").GetProperty("shutdownTimeoutMs").GetInt32(), deadline.GetProperty("expected").GetProperty("sharedDeadlineElapsedMs").GetInt32());
        Assert.AreEqual(1, deadline.GetProperty("expected").GetProperty("operationsStarted").GetInt32());
        Assert.IsFalse(Find(lifecycle, "LIFECYCLE-PROVIDER-NONOWNING").GetProperty("expected").GetProperty("runtimeStoppedByProvider").GetBoolean());
        Assert.IsFalse(Find(lifecycle, "LIFECYCLE-DEADLINE-PENDING").GetProperty("expected").GetProperty("resourceDisposedUnderUnresolvedWrite").GetBoolean());
        Assert.AreEqual(0, Find(lifecycle, "LIFECYCLE-COALESCED-CANCELLED-WAITS").GetProperty("expected").GetProperty("retainedCancelledCallerWaiters").GetInt32());
        foreach (JsonProperty rule in Find(lifecycle, "LIFECYCLE-NO-DURABILITY").GetProperty("expected").EnumerateObject())
        {
            Assert.IsFalse(rule.Value.GetBoolean(), rule.Name);
        }
    }

    [TestMethod]
    public void RuntimeSpecificationHasExactMethodsAndImmutableOutputs()
    {
        string relative = Path.Combine(CIS.TestSupport.VerificationCheckpoint.Root(RepositoryRoot), "contracts/startup");
        using JsonDocument report = Read(relative + "/summary.json");
        using JsonDocument surface = Read("implementations/csharp/verification/contracts/startup/runtime-surface.json");
        string reference = Path.Combine(RepositoryRoot, relative, report.RootElement.GetProperty("referenceAssembly").GetProperty("path").GetString()!);
        using var stream = File.OpenRead(reference);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var provider = new ContractTypeNameProvider();
        var types = reader.TypeDefinitions.Select(reader.GetTypeDefinition).ToDictionary(type => reader.GetString(type.Namespace) + '.' + reader.GetString(type.Name));
        VerifyRuntimeMethods(reader, surface.RootElement.GetProperty("methods"), types, provider);
        VerifyImmutableOutputs(reader, surface.RootElement.GetProperty("immutableOutputs"), types, provider);
    }

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

    private static void VerifyRuntimeMethods(MetadataReader reader, JsonElement contracts, Dictionary<string, TypeDefinition> types, ContractTypeNameProvider provider)
    {
        foreach (JsonProperty contract in contracts.EnumerateObject())
        {
            string ns = contract.Name == "CoreLoggerProvider" ? "CIS.Logging.MicrosoftLogger" : "CIS.Logging";
            TypeDefinition type = types[ns + '.' + contract.Name];
            Assert.IsTrue((type.Attributes & TypeAttributes.Sealed) != 0, contract.Name);
            MethodDefinition[] methods = PublicMethods(reader, type).Where(method => reader.GetString(method.Name) != ".ctor").ToArray();
            var expectedMethods = contract.Value.EnumerateObject().SelectMany(method =>
                (method.Value.ValueKind == JsonValueKind.Array ? method.Value.EnumerateArray().ToArray() : [method.Value])
                .Select(value => (method.Name, Value: value))).ToArray();
            CollectionAssert.AreEquivalent(expectedMethods.Select(method => method.Name).ToArray(), methods.Select(method => reader.GetString(method.Name)).ToArray());
            foreach (var expected in expectedMethods)
            {
                JsonProperty[] parameters = expected.Value.GetProperty("parameters").EnumerateObject().ToArray();
                MethodDefinition[] matches = methods.Where(method => reader.GetString(method.Name) == expected.Name &&
                    method.DecodeSignature(provider, null).ParameterTypes.SequenceEqual(parameters.Select(parameter => parameter.Value.GetString()!))).ToArray();
                Assert.AreEqual(1, matches.Length, contract.Name + "." + expected.Name + "(" + string.Join(", ", parameters.Select(parameter => parameter.Value.GetString())) + ")");
                MethodDefinition method = matches[0];
                MethodSignature<string> signature = method.DecodeSignature(provider, null);
                Assert.AreEqual(expected.Value.GetProperty("return").GetString(), signature.ReturnType, expected.Name);
                CollectionAssert.AreEqual(parameters.Select(parameter => parameter.Value.GetString()!).ToArray(), signature.ParameterTypes.ToArray(), expected.Name);
                string[] names = method.GetParameters().Select(reader.GetParameter).Where(parameter => parameter.SequenceNumber > 0).OrderBy(parameter => parameter.SequenceNumber).Select(parameter => reader.GetString(parameter.Name)).ToArray();
                CollectionAssert.AreEqual(parameters.Select(parameter => parameter.Name).ToArray(), names, expected.Name);
                bool expectedStatic = expected.Value.TryGetProperty("static", out JsonElement flag) && flag.GetBoolean();
                Assert.AreEqual(expectedStatic, (method.Attributes & MethodAttributes.Static) != 0, expected.Name);
            }

            VerifyOwnershipInterfaces(reader, type, contract.Name);
        }
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

    private static void VerifyOwnershipInterfaces(MetadataReader reader, TypeDefinition type, string name)
    {
        string[] interfaces = type.GetInterfaceImplementations().Select(reader.GetInterfaceImplementation).Select(item => reader.GetTypeReference((TypeReferenceHandle)item.Interface)).Select(item => reader.GetString(item.Namespace) + '.' + reader.GetString(item.Name)).ToArray();
        string[] expected = name switch
        {
            "CoreLogRuntime" => ["System.IAsyncDisposable"],
            "CoreLoggerProvider" => ["Microsoft.Extensions.Logging.ILoggerProvider", "Microsoft.Extensions.Logging.ISupportExternalScope", "System.IDisposable"],
            _ => []
        };
        CollectionAssert.AreEquivalent(expected, interfaces);
        MethodDefinition[] constructors = PublicMethods(reader, type).Where(method => reader.GetString(method.Name) == ".ctor").ToArray();
        Assert.AreEqual(name == "CoreLoggerProvider" ? 1 : 0, constructors.Length);
        if (constructors.Length == 1)
        {
            MethodDefinition constructor = constructors[0];
            CollectionAssert.AreEqual(new[] { "CoreLogRuntime" }, constructor.DecodeSignature(new ContractTypeNameProvider(), null).ParameterTypes.ToArray());
            CollectionAssert.AreEqual(new[] { "runtime" }, constructor.GetParameters().Select(reader.GetParameter).Where(parameter => parameter.SequenceNumber > 0).Select(parameter => reader.GetString(parameter.Name)).ToArray());
        }
    }

    private static IEnumerable<MethodDefinition> PublicMethods(MetadataReader reader, TypeDefinition type) => type.GetMethods().Select(reader.GetMethodDefinition).Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public);
    private static IEnumerable<object[]> Cases(JsonDocument catalog) => catalog.RootElement.GetProperty("cases").EnumerateArray().Select(item => new object[] { item.GetProperty("id").GetString()! });
    private static JsonElement Find(JsonDocument catalog, string id) => catalog.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
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
