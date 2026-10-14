using System.Reflection;

namespace CIS.TypeManagement;

// Compiled only into an isolated instrumented copy, never the shipping project.
internal static class TypeManagerTestProbe
{
    private static readonly Dictionary<Assembly, int> Counts = new();
    internal static Action? BeforePublication { get; set; }

    internal static Type[] Enumerate(Assembly assembly)
    {
        lock (Counts) { Counts[assembly] = Counts.GetValueOrDefault(assembly) + 1; }
        return assembly.GetTypes();
    }

    internal static Dictionary<Assembly, int> Snapshot()
    {
        lock (Counts) { return new Dictionary<Assembly, int>(Counts); }
    }
}
