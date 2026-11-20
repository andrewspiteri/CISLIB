namespace CIS.Transactions;

// One concrete host-local attachment point. The observations outcome adds logging and metrics here.
internal sealed class TransactionInstrumentation
{
    public TransactionInstrumentation(TransactionsComposition composition) => RegistrationIdentity = composition.RegistrationIdentity;
    internal object RegistrationIdentity
    {
        get;
    }
    internal ITransactionObservation Capture(string databaseName, TransactionIsolation isolation, bool logCalls)
        => NullTransactionObservation.Instance;
}
