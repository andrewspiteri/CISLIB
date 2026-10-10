using CIS;
using CIS.Logging;
using CIS.Configuration;
public static class InvalidConsumer
{
    public static void Check(LoggingOptions logging) { logging.QueueCapacity = 1; }
}
