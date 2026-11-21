namespace CIS.Operations.Hosting;

/// <summary>Passive finite metadata for operation selection and one host's observation binding.</summary>
public sealed class OperationsComposition
{
    /// <summary>Gets the maximum number of registered operation aliases.</summary>
    public const int MaximumOperations = 16;

    internal OperationsComposition(object registrationIdentity, IEnumerable<string> names)
    {
        RegistrationIdentity = registrationIdentity;
        OperationNames = Array.AsReadOnly(names.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the immutable in-process host registration identity, never a persisted operation identity.</summary>
    public object RegistrationIdentity
    {
        get;
    }
    /// <summary>Gets detached ordinal registered aliases, without codecs, scoped services or native resources.</summary>
    public IReadOnlyList<string> OperationNames
    {
        get;
    }
}
