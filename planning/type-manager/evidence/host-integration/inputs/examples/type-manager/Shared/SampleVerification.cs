using System.Reflection;
using System.Runtime.Loader;

namespace CIS.TypeManagement.Examples;

// Used only by the examples' explicit --verify mode. This does not enable plugin
// discovery: the unrelated later assembly must remain outside the fixed catalog.
internal static class SampleVerification
{
    internal static void CheckFixedMembership(ITypeManager manager, string lateAssemblyPath)
    {
        var assemblies = manager.Assemblies.ToArray();
        var types = manager.Types.ToArray();
        var candidates = manager.GetImplementations<IDiscoveryExample>();
        var report = manager.PreparationReport;
        var definitions = assemblies.ToDictionary(assembly => assembly, assembly => manager.GetDefinedTypes(assembly).ToArray());
        string path = Path.GetFullPath(lateAssemblyPath);
        string identity = AssemblyName.GetAssemblyName(path).FullName;
        if (AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.FullName == identity))
        {
            throw new InvalidOperationException("Late-load test assembly was already loaded.");
        }
        var later = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var lateType = later.GetType("CIS.TypeManagement.Examples.Late.LateExample", throwOnError: true)!;
        if (!typeof(IDiscoveryExample).IsAssignableFrom(lateType) || !typeof(IDisposable).IsAssignableFrom(lateType))
        {
            throw new InvalidOperationException("Late fixture must be a real matching implementation.");
        }
        if (!manager.Assemblies.SequenceEqual(assemblies) || !manager.Types.SequenceEqual(types) ||
            !ReferenceEquals(report, manager.PreparationReport) ||
            !ReferenceEquals(candidates, manager.GetImplementations<IDiscoveryExample>()) ||
            candidates.Contains(lateType) || manager.GetImplementations<IDisposable>().Contains(lateType) ||
            manager.TryGetType(lateType.AssemblyQualifiedName!, out _) ||
            definitions.Any(pair => !manager.GetDefinedTypes(pair.Key).SequenceEqual(pair.Value)))
        {
            throw new InvalidOperationException("A later assembly changed the completed catalog.");
        }
    }
}
