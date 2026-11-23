using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementCrossLanguageTests
{
    [TestMethod]
    public async Task BuiltCSharpAndNodePackagesUseTheSameActualAuthenticatedServiceAndRetainTheirOriginalResults()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        ManagementClientOptions management = fixture.RegisterBoundClient(builder.Services, "operator-a", "actual-management");
        ManagementClientOptions workload = fixture.RegisterBoundClient(builder.Services, "workload-a", "actual-management-control");
        IHost clientHost = builder.Build();
        try
        {
            await clientHost.StartAsync();
            IManagementClient client = clientHost.Services.GetRequiredKeyedService<IManagementClient>(management.RestClientName);
            IManagementClient workloadClient = clientHost.Services.GetRequiredKeyedService<IManagementClient>(workload.RestClientName);
            string controlAttempt = Guid.NewGuid().ToString("D");
            ManagementCallResult<ManagementControlConfirmation> control = await workloadClient.GetControlAsync(fixture.RegistrationA, controlAttempt);
            Assert.IsTrue(control.Exchange.Success, control.Problem?.Code);
            Assert.AreEqual(200, control.Exchange.Context.HttpStatusCode);
            Assert.AreEqual(fixture.RegistrationA, control.Exchange.Value!.RegistrationId);
            Assert.AreEqual(controlAttempt, control.Exchange.Value.AttemptId);
            Assert.IsTrue(ManagementWire.IsUuid(control.Exchange.Value.AuthorityId));
            Assert.IsTrue(control.Exchange.Value.AdmissionAllowed);
            Assert.IsTrue(control.Exchange.Value.ValidForMs is >= 1 and <= ManagementWire.MaximumControlValidForMs);
            ManagementCallResult<ManagementApplication> read = await client.GetApplicationAsync(fixture.ApplicationA);
            Assert.IsTrue(read.Exchange.Success, read.Problem?.Code);
            Assert.AreEqual(200, read.Exchange.Context.HttpStatusCode);
            const string csharpKey = "csharp-original";
            ManagementCallResult<ManagementMetadataResult> applied = await client.ReplaceMetadataAsync(fixture.ApplicationA,
                csharpKey, read.Exchange.Value!.MetadataETag, new(1, "CSharp authenticated consumer", "Exact Cafe\u0301"));
            Assert.IsTrue(applied.Exchange.Success, applied.Problem?.Code);
            Assert.AreEqual(csharpKey, applied.OperationKey);
            Assert.AreEqual("committed", applied.Exchange.Value!.OriginalCompletion);
            Assert.AreEqual("2", applied.Exchange.Value.Result!.MetadataRevision);

            string bearer = await fixture.WriteConsumerBearerAsync("operator-a");
            string workloadBearer = await fixture.WriteConsumerBearerAsync("workload-a");
            await RunNodeAsync(fixture, bearer, workloadBearer);
            using JsonDocument report = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.ConsumerReportFile));
            foreach (string fact in new[] { "authenticatedRead", "metadataApplied", "originalResultFound", "replayRetainedOriginal", "authoritativeControl" })
            {
                Assert.IsTrue(report.RootElement.GetProperty(fact).GetBoolean(), fact);
            }
            Assert.AreEqual("node-original", report.RootElement.GetProperty("operationKey").GetString());
            Assert.AreEqual(fixture.ApplicationA, report.RootElement.GetProperty("applicationId").GetString());
            Assert.AreEqual("3", report.RootElement.GetProperty("revision").GetString());
            Assert.AreEqual(fixture.RegistrationA, report.RootElement.GetProperty("registrationId").GetString());
            Assert.AreEqual(control.Exchange.Value.AuthorityId, report.RootElement.GetProperty("authorityId").GetString(),
                "Both actual authenticated SDKs must observe the same persisted authority across service replicas.");
            Assert.IsFalse(report.RootElement.TryGetProperty("token", out _));

            ManagementCallResult<ManagementMetadataResult> original = await client.GetMetadataOperationAsync(fixture.ApplicationA, csharpKey);
            Assert.IsTrue(original.Exchange.Success, original.Problem?.Code);
            Assert.AreEqual("found", original.Exchange.Value!.ResultSource);
            Assert.AreEqual(applied.Exchange.Value.Result, original.Exchange.Value.Result,
                "A later edit by the other SDK cannot replace this operation's original result.");
            Assert.AreEqual("committed", original.Exchange.Value.OriginalCompletion);
            ManagementCallResult<ManagementApplication> current = await client.GetApplicationAsync(fixture.ApplicationA);
            Assert.AreEqual("3", current.Exchange.Value!.MetadataRevision);
        }
        finally
        {
            try
            {
                await clientHost.StopAsync();
            }
            finally
            {
                await ((IAsyncDisposable)clientHost).DisposeAsync();
            }
        }
    }

    private static async Task RunNodeAsync(ManagementHostFixture fixture, string bearer, string workloadBearer)
    {
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
        foreach (string argument in new[]
        {
            Path.Combine(repository.FullName, "implementations", "typescript", "tests", "real-service-consumer.mjs"),
            "--base-url", fixture.Origins[1] + "/platform", "--ca-file", fixture.ServerAuthorityFile,
            "--bearer-file", bearer, "--application-id", fixture.ApplicationA, "--operation-key", "node-original",
            "--control-bearer-file", workloadBearer, "--registration-id", fixture.RegistrationA,
            "--report-file", fixture.ConsumerReportFile
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = start };
        Assert.IsTrue(process.Start());
        Task<string> output = ReadCappedAsync(process.StandardOutput);
        Task<string> errors = ReadCappedAsync(process.StandardError);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            throw;
        }
        await Task.WhenAll(output, errors);
        Assert.AreEqual(0, process.ExitCode, "Actual Node consumer failed: " + await errors + await output);
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
