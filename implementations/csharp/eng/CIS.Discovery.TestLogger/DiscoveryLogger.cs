using System.Text.Json;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace CIS.Verification;

// Nonshipping VSTest discovery output. Unlike console names, native IDs retain class and DataRow identity.
[FriendlyName("cis-discovery")]
[ExtensionUri("logger://cis/discovery/v1")]
public sealed class DiscoveryLogger : ITestLoggerWithParameters
{
    public void Initialize(TestLoggerEvents events, string testRunDirectory) => Initialize(events,
        new Dictionary<string, string?> { ["OutputPath"] = Path.Combine(testRunDirectory, "discovery-native.json") });

    public void Initialize(TestLoggerEvents events, Dictionary<string, string?> parameters)
    {
        string output = Path.GetFullPath(parameters["OutputPath"] ?? throw new ArgumentException("Discovery output path is required.", nameof(parameters)));
        var gate = new object();
        var tests = new List<DiscoveredTest>();
        int errors = 0;
        events.DiscoveredTests += (_, args) =>
        {
            lock (gate)
            {
                foreach (TestCase test in args.DiscoveredTestCases ?? [])
                {
                    tests.Add(new(test.Id, test.FullyQualifiedName, test.DisplayName, test.Source));
                }
            }
        };
        events.DiscoveryMessage += (_, args) =>
        {
            if (args.Level == TestMessageLevel.Error)
            {
                lock (gate) { errors++; }
            }
        };
        events.DiscoveryComplete += (_, args) =>
        {
            lock (gate)
            {
                using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new
                {
                    schema = "cis.vstest-discovery/v1",
                    aborted = args.IsAborted,
                    errors,
                    total = args.TotalCount,
                    tests = tests.OrderBy(test => test.Id).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        };
    }

    private sealed record DiscoveredTest(Guid Id, string Method, string Name, string Source);
}
