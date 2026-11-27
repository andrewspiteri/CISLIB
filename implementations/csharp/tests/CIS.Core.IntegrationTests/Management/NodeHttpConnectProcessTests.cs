using System.Globalization;
using System.Text.Json;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;

namespace CIS.Tests.IntegrationTests.Management;

// Isolates the Windows native outbound-connect path without portal credentials or infrastructure.
[TestClass]
[TestCategory("RequiresNode")]
[DoNotParallelize]
public sealed class NodeHttpConnectProcessTests
{
    private const int CaptureLimitBytes = 16 * 1024;

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(4096, 8)]
    public async Task ShortLivedNativeHttpConnectionsJoinEveryOwnedResource(int requestCount, int concurrency)
    {
        string node = Environment.GetEnvironmentVariable("CIS_TEST_NODE_EXECUTABLE")
            ?? throw new InvalidOperationException("CIS_TEST_NODE_EXECUTABLE must identify the approved absolute Node executable.");
        Assert.IsTrue(Path.IsPathFullyQualified(node), "The native probe must use the approved absolute Node executable.");
        Assert.IsTrue(File.Exists(node), "The approved Node executable must exist.");

        string root = ManagementPortalHostTests.RepositoryRoot();
        string script = Path.Combine(root, "implementations", "typescript", "applications", "management-portal", "tests",
            "native-node-http-connect-checks.mjs");
        var files = new PrivateContainerDirectory();
        bool cleanupConfirmed = true;
        try
        {
            IReadOnlyDictionary<string, string?> environment = IsolatedEnvironment(files.Path);
            ProcessResult result = await ProcessRunner.RunAsync(node,
                [script, requestCount.ToString(CultureInfo.InvariantCulture), concurrency.ToString(CultureInfo.InvariantCulture)],
                root, TimeSpan.FromSeconds(30), environment, maximumCapturedBytesPerStream: CaptureLimitBytes);

            string diagnostics = Describe(result);
            // Preserve a native failure such as 0xC0000409 before attempting to parse an absent final report.
            Assert.AreEqual(0, result.ExitCode, diagnostics);
            Assert.IsTrue(result.OutputDrained, diagnostics);
            Assert.IsFalse(result.CaptureTruncated, diagnostics);
            Assert.IsTrue(result.StandardOutputTotalBytes <= CaptureLimitBytes, diagnostics);
            Assert.IsTrue(result.StandardErrorTotalBytes <= CaptureLimitBytes, diagnostics);
            Assert.AreEqual(string.Empty, result.StandardError, diagnostics);
            Console.Write(result.StandardOutput);

            string[] lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, lines.Length, "The child must produce its actual startup identity and one final report. " + diagnostics);
            using JsonDocument startup = JsonDocument.Parse(lines[0]);
            AssertStartup(startup.RootElement, requestCount, concurrency);

            using JsonDocument completed = JsonDocument.Parse(lines[1]);
            AssertCompleted(completed.RootElement, requestCount, concurrency);
        }
        catch (ProcessTimeoutException error)
        {
            cleanupConfirmed = error.CleanupCompleted && error.Result.OutputDrained;
            Console.Error.WriteLine(Describe(error.Result));
            if (!cleanupConfirmed)
            {
                Console.Error.WriteLine("NODE_HTTP_CONNECT_CUSTODY_RETAINED directory=" + files.Path);
            }

            throw;
        }
        catch (ProcessOutputLimitException error)
        {
            Console.Error.WriteLine(Describe(error.Result));
            throw;
        }
        finally
        {
            if (cleanupConfirmed)
            {
                files.Dispose();
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> IsolatedEnvironment(string directory)
    {
        var environment = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        // Enumerate names only: no inherited credential, proxy, Node option or other value is read or forwarded.
        foreach (string name in Environment.GetEnvironmentVariables().Keys)
        {
            environment[name] = null;
        }

        RestoreOperatingSystemVariable(environment, "SystemRoot");
        RestoreOperatingSystemVariable(environment, "WINDIR");

        string cache = Path.Combine(directory, "node-compile-cache");
        Directory.CreateDirectory(cache);
        environment["TEMP"] = directory;
        environment["TMP"] = directory;
        environment["NODE_COMPILE_CACHE"] = cache;
        return environment;
    }

    private static void RestoreOperatingSystemVariable(Dictionary<string, string?> environment, string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (value is not null)
        {
            environment[name] = value;
        }
    }

    private static void AssertStartup(JsonElement report, int requestCount, int concurrency)
    {
        Assert.AreEqual("node-http-connect-started", report.GetProperty("kind").GetString());
        Assert.AreEqual(requestCount, report.GetProperty("requestCount").GetInt32());
        Assert.AreEqual(concurrency, report.GetProperty("concurrency").GetInt32());
        foreach (string property in new[] { "node", "uv", "platform", "arch" })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.GetProperty(property).GetString()),
                "The actual native runtime must report " + property + ".");
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual("win32", report.GetProperty("platform").GetString());
        }
    }

    private static void AssertCompleted(JsonElement report, int requestCount, int concurrency)
    {
        Assert.AreEqual("node-http-connect-completed", report.GetProperty("kind").GetString());
        Assert.AreEqual(requestCount, report.GetProperty("requestCount").GetInt32());
        Assert.AreEqual(concurrency, report.GetProperty("concurrency").GetInt32());
        string[] counters =
        [
            "requestsCreated", "requestsClosed", "responsesReceived", "responsesEnded", "responsesClosed",
            "clientSocketsOpened", "clientSocketsClosed", "serverRequestsReceived", "serverRequestsEnded", "serverRequestsClosed",
            "serverResponsesFinished", "serverResponsesClosed", "serverSocketsOpened", "serverSocketsClosed"
        ];
        foreach (string counter in counters)
        {
            Assert.AreEqual(requestCount, report.GetProperty(counter).GetInt32(), counter + " must account for every native request.");
        }

        int maximumClientSockets = report.GetProperty("maximumClientSockets").GetInt32();
        Assert.IsTrue(maximumClientSockets > 0 && maximumClientSockets <= concurrency);
        Assert.AreEqual(0, report.GetProperty("listenersOutstanding").GetInt32());
        Assert.IsTrue(report.GetProperty("agentDestroyed").GetBoolean());
        Assert.IsTrue(report.GetProperty("agentEmpty").GetBoolean());
        Assert.IsTrue(report.GetProperty("listenerClosed").GetBoolean());
        Assert.IsTrue(report.GetProperty("nativeJoined").GetBoolean());
    }

    private static string Describe(ProcessResult result)
    {
        return $"Native HTTP child exit {result.ExitCode} (0x{unchecked((uint)result.ExitCode):X8}); output drained: {result.OutputDrained}."
            + Environment.NewLine + result.StandardOutput + result.StandardError;
    }
}
