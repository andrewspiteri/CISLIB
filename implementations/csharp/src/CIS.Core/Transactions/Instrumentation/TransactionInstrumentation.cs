using CIS.Metrics;
using Microsoft.Extensions.Logging;

namespace CIS.Transactions;

// Passive registration identity and one complete optional host binding, never an owner registry.
internal sealed class TransactionInstrumentation : IMetricsSource
{
    private readonly MetricsAttachment attachment = new();
    private readonly ILogger? logger;
    private TransactionMetrics? metrics;

    public TransactionInstrumentation(TransactionsComposition composition, ILoggerFactory? loggerFactory = null)
    {
        RegistrationIdentity = composition.RegistrationIdentity;
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Transactions");
        }
        catch (Exception error) when (TransactionText.IsOrdinary(error))
        {
        }
    }

    internal object RegistrationIdentity
    {
        get;
    }

    internal ITransactionObservation Capture(string databaseName, TransactionIsolation isolation, bool logCalls)
        => new TransactionObservationScope(databaseName, isolation, logCalls, logger, Volatile.Read(ref metrics));

    public void AttachMetrics(MetricsRuntime runtime)
    {
        if (attachment.Attach(runtime))
        {
            Volatile.Write(ref metrics, new TransactionMetrics(runtime));
        }
    }
}
