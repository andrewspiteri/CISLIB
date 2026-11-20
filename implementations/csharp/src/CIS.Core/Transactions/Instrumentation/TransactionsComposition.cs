namespace CIS.Transactions;

// Immutable metadata only; no source factory, native resource or live owner is retained here.
internal sealed class TransactionsComposition(object registrationIdentity, IReadOnlyList<TransactionDatabaseMetadata> databases)
{
    internal const int MaximumDatabases = 16;
    internal object RegistrationIdentity { get; } = registrationIdentity;
    internal IReadOnlyList<TransactionDatabaseMetadata> Databases { get; } = Array.AsReadOnly(databases.ToArray());
}

internal sealed class TransactionDatabaseMetadata(string name, TransactionIsolation defaultIsolation,
    IReadOnlyList<TransactionIsolation> supportedIsolationLevels)
{
    internal string Name { get; } = name;
    internal TransactionIsolation DefaultIsolation { get; } = defaultIsolation;
    internal IReadOnlyList<TransactionIsolation> SupportedIsolationLevels
    {
        get;
    } =
        Array.AsReadOnly(supportedIsolationLevels.ToArray());
}
