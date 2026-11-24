using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CIS.TestSupport.Messaging;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresNode")]
[DoNotParallelize]
public sealed class ManagementFetchTransportTests
{
    [TestMethod]
    public async Task DefaultFetchRetainsStructuredRecoveryAfterNativeResponseBodyFailures()
    {
        using var files = new PrivateContainerDirectory();
        TlsFixtureFiles.Write(files.Path);
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "implementations", "csharp", "CisLib.slnx")))
        {
            repository = repository.Parent;
        }
        Assert.IsNotNull(repository);
        string executable = System.Environment.GetEnvironmentVariable("CIS_TEST_NODE_EXECUTABLE") ?? "node";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repository.FullName
        };
        start.ArgumentList.Add(Path.Combine(repository.FullName, "implementations", "typescript", "tests", "native-fetch-consumer.mjs"));
        start.ArgumentList.Add(files.Path);
        start.Environment["NODE_EXTRA_CA_CERTS"] = Path.Combine(files.Path, "root.pem");
        using var process = new Process { StartInfo = start };
        Assert.IsTrue(process.Start());
        Task<string> output = ReadCappedAsync(process.StandardOutput);
        Task<string> errors = ReadCappedAsync(process.StandardError);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
        }
        Assert.AreEqual(0, process.ExitCode, "Actual native fetch failed: " + await errors + await output);
        using JsonDocument report = JsonDocument.Parse(await output);
        Assert.IsTrue(report.RootElement.GetProperty("nativeFetch").GetBoolean());
        Assert.IsTrue(report.RootElement.GetProperty("socketsDrained").GetBoolean());
        JsonElement cases = report.RootElement.GetProperty("cases");
        Assert.AreEqual(12, cases.GetArrayLength());
        var combinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in cases.EnumerateArray())
        {
            Assert.IsTrue(item.GetProperty("headersReceived").GetBoolean());
            Assert.IsTrue(item.GetProperty("originalKeyReceived").GetBoolean());
            Assert.IsTrue(item.GetProperty("responseDrained").GetBoolean());
            Assert.AreEqual(1, item.GetProperty("attempts").GetInt32());
            string operation = item.GetProperty("operation").GetString()!;
            string method = operation switch
            {
                "GET" => "GET",
                "PUT" => "PUT",
                "POST_VALIDATE" or "POST_PUBLISH" => "POST",
                _ => throw new AssertFailedException("The native fixture returned an undeclared operation.")
            };
            Assert.AreEqual(method, item.GetProperty("method").GetString());
            Assert.IsTrue(combinations.Add(operation + ":" + item.GetProperty("failure").GetString()));
            bool mutation = operation is "PUT" or "POST_PUBLISH";
            Assert.AreEqual(mutation, item.GetProperty("mutationUncertain").GetBoolean());
            string expected = item.GetProperty("failure").GetString() == "disconnect"
                ? "transportFailure" : item.GetProperty("failure").GetString()!;
            Assert.AreEqual(expected, item.GetProperty("kind").GetString());
        }
        foreach (string operation in new[] { "GET", "PUT", "POST_VALIDATE", "POST_PUBLISH" })
        {
            foreach (string failure in new[] { "disconnect", "timeout", "canceled" })
            {
                Assert.Contains(operation + ":" + failure, combinations);
            }
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var result = new StringBuilder();
        char[] buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            int admitted = Math.Min(count, 8192 - result.Length);
            result.Append(buffer, 0, admitted);
        }
        return result.ToString();
    }
}
