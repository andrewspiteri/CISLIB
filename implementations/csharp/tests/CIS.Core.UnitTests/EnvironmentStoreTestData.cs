using CIS.Logging;
using CIS.Tests.DiscoveryFixture;

namespace CIS.Tests.UnitTests;

// Controlled input transport only. Real container construction is qualified separately.
internal static class EnvironmentStoreTestData
{
    internal static FixtureEnvironmentValues Values => ConfigurationFixtureAdapter.Facts;
    internal static void AssignValidFacts() => ConfigurationFixtureAdapter.Facts = new FixtureEnvironmentValues();
    internal static PreparedConfiguration Observe()
    {
        Values.Logging.Prepare(Path.GetTempPath());
        return new PreparedConfiguration(Values.CaptureMetadata(), Values.Logging);
    }
}
