using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class TypeManagerFreshAssemblyTests
{
    private const string ReferenceAttributeName = "System.Runtime.CompilerServices.ReferenceAssemblyAttribute";
    private string directory = null!;
    private string manifest = null!;
    private static Assembly Host => typeof(TypeManagerFreshAssemblyTests).Assembly;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cis-type-manager-fresh", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.Copy(Host.Location, Path.Combine(directory, "Host.dll"));
        manifest = Path.Combine(directory, "host.types.json");
    }

    // Default-context assemblies stay mapped until the test host exits.
    // Files accumulate under transient temp across runs; remove cis-type-manager-fresh
    // only after every test host using those files has exited.

    [TestMethod]
    public async Task NewPhysicalAssetIsPublishedAndThenReusedWithoutActivation()
    {
        var selected = Emit("Fresh", "Fresh.dll");
        AssertNotLoaded(selected.Name);
        WriteInventory(selected);
        var manager = await Create();
        var loaded = manager.Assemblies.Single(assembly => assembly.FullName == selected.Identity);
        Assert.IsFalse(loaded.IsDynamic);
        Assert.AreSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(loaded));
        Assert.AreEqual(selected.Path, loaded.Location);
        var candidate = loaded.GetType("Fixtures.Candidate", throwOnError: true)!;
        Assert.Contains(candidate, manager.GetDefinedTypes(loaded));
        Assert.Contains(candidate, manager.GetImplementations<object>());
        CollectionAssert.AreEqual(new[] { selected.Identity }, manager.PreparationReport.LoadedAssemblyIdentities.ToArray());
        CollectionAssert.AreEqual(new[] { Host.FullName! }, manager.PreparationReport.ReusedAssemblyIdentities.ToArray());
        var repeated = await Create();
        Assert.AreSame(loaded, repeated.Assemblies.Single(assembly => assembly.FullName == selected.Identity));
        Assert.IsEmpty(repeated.PreparationReport.LoadedAssemblyIdentities);
        Assert.HasCount(2, repeated.PreparationReport.ReusedAssemblyIdentities);
    }

    [TestMethod]
    public async Task FileIdentityConflictRejectsTheWholeSelectionBeforeAnyNewLoad()
    {
        var first = Emit("AA_First", "First.dll");
        var last = Emit("ZZ_Last", "Last.dll");
        var expectedName = new AssemblyName(last.Identity) { Version = new Version(2, 0, 0, 0) };
        WriteInventory(first, last with { Identity = expectedName.FullName! });
        AssertNotLoaded(first.Name);
        AssertNotLoaded(last.Name);
        var failure = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(Create);
        AssertIdentityFailure(failure, last.Path, expectedName.FullName!, last.Identity);
        AssertNotLoaded(first.Name);
        AssertNotLoaded(last.Name);
    }

    [TestMethod]
    public async Task ExistingCaseInsensitiveNameConflictIsDetectedBeforeAnyNewLoad()
    {
        var first = Emit("AA_First", "First.dll");
        string name = "ZZ_Case_" + Guid.NewGuid().ToString("N");
        var selected = Emit(name, "Selected.dll", exactName: true);
        var other = Emit(name.ToLowerInvariant(), "Other.dll", exactName: true);
        var existing = AssemblyLoadContext.Default.LoadFromAssemblyPath(other.Path);
        WriteInventory(first, selected);
        AssertNotLoaded(first.Name);
        var failure = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(Create);
        AssertIdentityFailure(failure, selected.Path, selected.Identity, existing.FullName!);
        AssertNotLoaded(first.Name);
    }

    [TestMethod]
    public async Task ResolverSubstitutionMustMatchTheSelectedFullIdentity()
    {
        string name = "Resolver_" + Guid.NewGuid().ToString("N");
        var selected = Emit(name, "Selected.dll", exactName: true);
        var other = Emit(name, "Other.dll", exactName: true, version: 2);
        WriteInventory(selected);
        AssertNotLoaded(name);
        int calls = 0;
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName requested)
        {
            if (requested.Name != name) { return null; }
            calls++;
            return context.LoadFromAssemblyPath(other.Path);
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var failure = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(Create);
            AssertIdentityFailure(failure, selected.Path, selected.Identity, other.Identity);
            Assert.AreEqual(1, calls);
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }

    [TestMethod]
    [DataRow("external")]
    [DataRow("defined")]
    public async Task ReferenceAttributeIsRejectedBeforeAnyNewLoad(string form)
    {
        var first = Emit("AA_First", "First.dll");
        var reference = Emit("ZZ_Reference", "Reference.dll", attribute: form);
        WriteInventory(first, reference);
        AssertNotLoaded(first.Name);
        AssertNotLoaded(reference.Name);
        var failure = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(Create);
        Assert.AreEqual(TypeManagerFailureKind.AssemblyLoadFailure, failure.FailureKind);
        Assert.AreEqual(reference.Path, failure.AssetPath);
        Assert.AreEqual(reference.Identity, failure.AssemblyIdentity);
        Assert.AreEqual(manifest, failure.ManifestPath);
        Assert.IsInstanceOfType<BadImageFormatException>(failure.InnerException);
        AssertNotLoaded(first.Name);
        AssertNotLoaded(reference.Name);
    }

    [TestMethod]
    [DataRow("Other.Namespace.ReferenceAssemblyAttribute")]
    [DataRow("System.Runtime.CompilerServices.OtherAttribute")]
    public async Task SimilarAttributeNamesAreExecutableAndNeverInstantiated(string attribute)
    {
        var selected = Emit("Similar", "Similar.dll", attribute: attribute);
        WriteInventory(selected);
        AssertNotLoaded(selected.Name);
        var manager = await Create();
        var loaded = manager.Assemblies.Single(assembly => assembly.FullName == selected.Identity);
        Assert.Contains(loaded.GetType("Fixtures.Candidate", true)!, manager.Types);
        Assert.Contains(loaded.GetType(attribute, true)!, manager.Types);
    }

    private void AssertIdentityFailure(TypeManagerInitializationException failure, string path, string expectedSelectedIdentity, string expectedObservedIdentity)
    {
        Assert.AreEqual(TypeManagerFailureKind.AssemblyIdentityConflict, failure.FailureKind);
        Assert.AreEqual(manifest, failure.ManifestPath);
        Assert.AreEqual(path, failure.AssetPath);
        Assert.AreEqual(expectedSelectedIdentity, failure.AssemblyIdentity);
        Assert.AreEqual(expectedObservedIdentity, failure.ActualAssemblyIdentity);
        Assert.IsInstanceOfType<FileLoadException>(failure.InnerException);
    }

    private Task<ITypeManager> Create() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { AssemblyManifestPath = manifest });

    private static void AssertNotLoaded(string name) => Assert.IsFalse(AssemblyLoadContext.Default.Assemblies.Any(
        assembly => string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)));

    private void WriteInventory(params Selection[] selected) => File.WriteAllText(manifest, JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        hostAssembly = Host.FullName,
        assemblies = new[] { new { assemblyName = Host.FullName!, relativePath = "Host.dll" } }
            .Concat(selected.Select(item => new { assemblyName = item.Identity, relativePath = Path.GetFileName(item.Path) }))
    }));

    private Selection Emit(string prefix, string filename, bool exactName = false, int version = 1, string? attribute = null)
    {
        string name = exactName ? prefix : prefix + "_" + Guid.NewGuid().ToString("N");
        var identity = new AssemblyName(name) { Version = new Version(version, 0, 0, 0) };
        var builder = new PersistedAssemblyBuilder(identity, typeof(object).Assembly);
        var module = builder.DefineDynamicModule(name);
        var candidate = module.DefineType("Fixtures.Candidate", TypeAttributes.Public | TypeAttributes.Class);
        var constructor = candidate.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, Type.EmptyTypes);
        var body = constructor.GetILGenerator();
        body.Emit(OpCodes.Ldstr, "Candidate activation is forbidden during discovery.");
        body.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        body.Emit(OpCodes.Throw);
        candidate.CreateType();
        ConstructorBuilder? attributeConstructor = null;
        if (attribute == "external")
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(ReferenceAssemblyAttribute).GetConstructor(Type.EmptyTypes)!, []));
        }
        else if (attribute is not null)
        {
            string typeName = attribute == "defined" ? ReferenceAttributeName : attribute;
            var type = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed, typeof(Attribute));
            var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "Attribute activation is forbidden during discovery.");
            il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
            type.CreateType();
            attributeConstructor = ctor;
        }
        string path = Path.Combine(directory, filename);
        var metadata = builder.GenerateMetadata(out var methodBodies, out var fieldData);
        if (attributeConstructor is not null)
        {
            // Persisted member tokens are assigned by GenerateMetadata. Bind the
            // assembly attribute only after its local constructor has a real token.
            metadata.AddCustomAttribute(EntityHandle.AssemblyDefinition,
                MetadataTokens.MethodDefinitionHandle(attributeConstructor.MetadataToken),
                metadata.GetOrAddBlob(new byte[] { 1, 0, 0, 0 }));
        }
        var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), methodBodies, mappedFieldData: fieldData);
        var bytes = new BlobBuilder();
        pe.Serialize(bytes);
        using (var output = File.Create(path)) { bytes.WriteContentTo(output); }
        if (attribute is not null)
        {
            using var checkStream = File.OpenRead(path);
            using var checkExecutable = new PEReader(checkStream);
            var reader = checkExecutable.GetMetadataReader();
            var token = reader.GetCustomAttribute(reader.GetAssemblyDefinition().GetCustomAttributes().Single()).Constructor;
            Assert.AreEqual(attribute == "external" ? HandleKind.MemberReference : HandleKind.MethodDefinition, token.Kind);
            if (attribute != "external")
            {
                var declaringType = reader.GetMethodDefinition((MethodDefinitionHandle)token).GetDeclaringType();
                var definition = reader.GetTypeDefinition(declaringType);
                Assert.AreEqual(attribute == "defined" ? ReferenceAttributeName : attribute,
                    reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name));
            }
        }
        return new Selection(name, AssemblyName.GetAssemblyName(path).FullName!, path);
    }

    private sealed record Selection(string Name, string Identity, string Path);
}
