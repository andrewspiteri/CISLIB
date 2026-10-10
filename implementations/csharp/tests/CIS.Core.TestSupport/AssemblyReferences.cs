using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CIS.TestSupport;

/// <summary>Inspects assembly references without loading or executing the inspected assembly.</summary>
public static class AssemblyReferences
{
    public static IReadOnlyList<string> Read(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            throw new BadImageFormatException("The inspected file has no managed metadata.");
        }

        MetadataReader metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException("The inspected file is not a managed assembly.");
        }

        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindUnexpected(string assemblyPath, IEnumerable<string> allowedNames)
    {
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        return Read(assemblyPath).Where(name => !allowed.Contains(name)).ToArray();
    }
}
