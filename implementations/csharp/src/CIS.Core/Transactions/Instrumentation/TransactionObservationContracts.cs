namespace CIS.Transactions;

internal enum TransactionNativePhase
{
    Begin,
    Commit,
    Rollback,
    Dispose
}

internal interface ITransactionObservation
{
    void OwnerPublished();
    void NativeFailure(TransactionNativePhase phase, bool expectedCancellation = false);
    void BeginCleanupFailure(TransactionError error);
    void Completed(TransactionCompletion completion);
}

internal sealed class NullTransactionObservation : ITransactionObservation
{
    internal static NullTransactionObservation Instance { get; } = new();
    public void OwnerPublished()
    {
    }
    public void NativeFailure(TransactionNativePhase phase, bool expectedCancellation = false)
    {
    }
    public void BeginCleanupFailure(TransactionError error)
    {
    }
    public void Completed(TransactionCompletion completion)
    {
    }
}

internal static class TransactionObservation
{
    internal static ITransactionObservation Capture(TransactionInstrumentation instrumentation, string database,
        TransactionIsolation isolation, bool logCalls)
    {
        try
        {
            return instrumentation.Capture(database, isolation, logCalls) ?? NullTransactionObservation.Instance;
        }
        catch (Exception error) when (TransactionText.IsOrdinary(error))
        {
            return NullTransactionObservation.Instance;
        }
    }

    internal static void Attempt(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (TransactionText.IsOrdinary(error))
        {
            // Optional observations never change native admission, cleanup or persistence evidence.
        }
    }
}
