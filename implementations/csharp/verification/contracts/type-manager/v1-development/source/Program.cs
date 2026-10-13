using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using CIS.TypeManagement;

var host = typeof(Program).Assembly;
var options = new TypeManagerOptions(host)
{
    AssemblyManifestPath = args[0],
    PrecomputedContracts = new[] { typeof(IContract) }
};
ITypeManager manager = await TypeManager.CreateAsync(options);
if (manager.Assemblies.Count != 2 || manager.GetDefinedTypes(host).Count == 0) { throw new InvalidOperationException("Wrong catalog membership."); }
if (manager.GetImplementations<IContract>().Single() != typeof(Implementation) || manager.GetImplementations(typeof(IContract)).Single() != typeof(Implementation))
{
    throw new InvalidOperationException("Generic/non-generic queries disagree.");
}
if (!manager.TryGetType(typeof(Implementation).AssemblyQualifiedName!, out Type? found) || found != typeof(Implementation))
{
    throw new InvalidOperationException("Nullable exact lookup failed.");
}
TypeManagerPreparationReport report = manager.PreparationReport;
if (report.Elapsed < TimeSpan.Zero || report.SelectedAssemblyCount != 2 || report.TypeCount != manager.Types.Count ||
    report.PreparedContractCount != 1 || report.SelectedAssemblyIdentities.Count != 2 ||
    report.LoadedAssemblyIdentities.Count + report.ReusedAssemblyIdentities.Count != 2)
{
    throw new InvalidOperationException("Inconsistent preparation diagnostics.");
}
try
{
    await TypeManager.CreateAsync(new TypeManagerOptions(host) { AssemblyManifestPath = args[0] + ".missing" }, CancellationToken.None);
    throw new InvalidOperationException("Missing inventory unexpectedly succeeded.");
}
catch (TypeManagerInitializationException error)
{
    if (error.FailureKind != TypeManagerFailureKind.InvalidInventory || error.ManifestPath is null || error.InnerException is null ||
        error.AssetPath is not null || error.AssemblyIdentity is not null || error.ActualAssemblyIdentity is not null || error.LoaderExceptions.Count != 0)
    {
        throw new InvalidOperationException("Failure contract changed.", error);
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { contractConsumer = "passed", typeCount = manager.Types.Count, selectedAssemblyCount = manager.Assemblies.Count }));

internal interface IContract;
internal sealed class Implementation : IContract
{
    private Implementation() => throw new InvalidOperationException("Discovery must not construct this type.");
}

// Compile the complete interface from a consumer. Added required members would break this fixture.
internal sealed class ConsumerManager : ITypeManager
{
    public IReadOnlyList<Assembly> Assemblies => Array.Empty<Assembly>();
    public IReadOnlyList<Type> Types => Array.Empty<Type>();
    public TypeManagerPreparationReport PreparationReport => throw new NotSupportedException();
    public IReadOnlyList<Type> GetDefinedTypes(Assembly assembly) => Array.Empty<Type>();
    public IReadOnlyList<Type> GetImplementations(Type contractType) => Array.Empty<Type>();
    public bool TryGetType(string assemblyQualifiedTypeName, [NotNullWhen(true)] out Type? type)
    {
        type = null;
        return false;
    }
}
