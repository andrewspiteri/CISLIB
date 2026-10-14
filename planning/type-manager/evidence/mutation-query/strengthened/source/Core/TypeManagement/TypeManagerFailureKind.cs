namespace CIS.TypeManagement;

/// <summary>Stable categories of operational Type Manager preparation failures.</summary>
public enum TypeManagerFailureKind
{
    /// <summary>The required deployment inventory is missing, malformed or inconsistent.</summary>
    InvalidInventory = 1,
    /// <summary>A selected managed assembly could not be loaded.</summary>
    AssemblyLoadFailure = 2,
    /// <summary>The selected assembly identity conflicts with the actual runtime identity.</summary>
    AssemblyIdentityConflict = 3,
    /// <summary>Complete type or relationship discovery could not be performed.</summary>
    TypeDiscoveryFailure = 4,
    /// <summary>The deployment uses an unsupported runtime or loading model.</summary>
    UnsupportedDeployment = 5
}
