using CIS;
using CIS.Logging;
using CIS.Configuration;
public static class InvalidConsumer
{
    public static void Check(CoreConfigurationException failure) { failure.Issues[0] = new ConfigurationIssue("code", "key", "safe"); }
}
