namespace CIS.TypeManagement;

/// <summary>Operational failure that prevented publication of a complete Type Manager.</summary>
public sealed class TypeManagerInitializationException : Exception
{
    internal TypeManagerInitializationException(TypeManagerFailureKind kind, string message, Exception innerException,
        string? manifestPath = null, string? assetPath = null, string? assemblyIdentity = null,
        string? actualAssemblyIdentity = null, IEnumerable<Exception>? loaderExceptions = null)
        : base(Describe(message, manifestPath, assetPath, assemblyIdentity, actualAssemblyIdentity), innerException)
    {
        FailureKind = kind;
        ManifestPath = manifestPath;
        AssetPath = assetPath;
        AssemblyIdentity = assemblyIdentity;
        ActualAssemblyIdentity = actualAssemblyIdentity;
        LoaderExceptions = Array.AsReadOnly(loaderExceptions?.ToArray() ?? []);
    }

    /// <summary>Gets the stable operational failure category.</summary>
    public TypeManagerFailureKind FailureKind { get; }
    /// <summary>Gets the inventory location, if known.</summary>
    public string? ManifestPath { get; }
    /// <summary>Gets the relevant asset location, if known. Invalid paths may retain submitted text; discovery failures identify the actual loaded assembly location.</summary>
    public string? AssetPath { get; }
    /// <summary>Gets the selected full assembly identity, if known.</summary>
    public string? AssemblyIdentity { get; }
    /// <summary>Gets the conflicting observed identity, if applicable.</summary>
    public string? ActualAssemblyIdentity { get; }
    /// <summary>Gets an owned, nonmutating collection of underlying loader failures.</summary>
    public IReadOnlyList<Exception> LoaderExceptions { get; }

    private static string Describe(string message, string? manifestPath, string? assetPath,
        string? assemblyIdentity, string? actualAssemblyIdentity)
    {
        var text = new System.Text.StringBuilder(message);
        if (manifestPath is not null) { text.Append(" Manifest: '").Append(manifestPath).Append("'."); }
        if (assetPath is not null) { text.Append(" Asset: '").Append(assetPath).Append("'."); }
        if (assemblyIdentity is not null) { text.Append(" Selected identity: '").Append(assemblyIdentity).Append("'."); }
        if (actualAssemblyIdentity is not null) { text.Append(" Observed identity: '").Append(actualAssemblyIdentity).Append("'."); }
        return text.ToString();
    }
}
