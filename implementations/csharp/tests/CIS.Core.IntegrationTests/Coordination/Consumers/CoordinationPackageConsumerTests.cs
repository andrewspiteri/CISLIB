using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using CIS.Coordination;
using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Tests.IntegrationTests.RestClient;
using CIS.Tests.MessagingCrashFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Coordination.Consumers;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[DoNotParallelize]
public sealed class CoordinationPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ExactPublicPackagesRecoverAfterARealOwnerProcessDiesAndPublishItsOriginalCommittedIntent()
    {
        RestPackage package = await RestPackage.PrepareAsync(TestContext, "Coordination.Processes.Package",
            typeof(CoordinationGrant).Assembly, typeof(RabbitMqOptions).Assembly);
        bool cleanupConfirmed = true;
        MessageRpcProcessFixture? fixture = null;
        try
        {
            string binary = await BuildPublicConsumerAsync(package);
            fixture = await MessageRpcProcessFixture.CreateAsync(coordination: true);
            var delivered = new TaskCompletionSource<EncodedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            await fixture.Broker.Transport.StartAsync(async (delivery, token) =>
            {
                try
                {
                    Assert.IsNotNull(await delivery.GetNativeEvidenceAsync(token));
                    Assert.IsTrue(MessageWire.TryDecode(delivery.Body, out EncodedMessage? message, out _));
                    await delivery.AcknowledgeAsync(token);
                    delivered.TrySetResult(message!);
                }
                catch (Exception error)
                {
                    delivered.TrySetException(error);
                    delivery.RequestConsumerStop("fixture.coordination_package_capture_failed");
                }
            });

            MessageRpcProcessFixture.RpcChild original = await fixture.StartAsync(fixture.Descriptor("coord-owner", "owner"), binary);
            JsonElement committed = await fixture.MarkerAsync(original, "coordination-committed");
            string firstInstance = committed.GetProperty("InstanceId").GetString()!;
            long firstGeneration = Generation(committed.GetProperty("Generation").GetString()!);
            string coreHash = CandidatePackageFixture.Hash(typeof(CoordinationGrant).Assembly.Location);
            Assert.AreEqual(coreHash, committed.GetProperty("CoreHash").GetString());
            Assert.AreNotEqual(fixture.Broker.Local.InstanceId, firstInstance, "An actual child captures its own process identity.");
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT total::bigint FROM {fixture.Server.Table("rpc_effect_counter")}"));
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM \"{fixture.OperationSchema}\".operation_journal"));
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("outbox")} WHERE state=0"));
            Assert.IsFalse(delivered.Task.IsCompleted, "The failed owner's publisher has never been started.");

            MessageRpcProcessFixture.RpcChild contender = await fixture.StartAsync(fixture.Descriptor("coord-contender", "contender"), binary);
            CrashReport busy = await fixture.ReportAsync(contender);
            Assert.AreEqual("Busy", busy.CoordinationOutcome);
            Assert.AreEqual(firstInstance, busy.ObservedOwnerInstanceId);
            Assert.AreNotEqual(firstInstance, busy.InstanceId);
            Assert.AreNotEqual(fixture.Broker.Local.InstanceId, busy.InstanceId);
            Assert.AreEqual(coreHash, busy.CoreHash);
            Assert.IsFalse(delivered.Task.IsCompleted);
            ProcessResult killed = await original.StopAsync(requireCrash: true);
            Assert.IsTrue(original.Killed && killed.OutputDrained && !killed.CaptureTruncated,
                "The parent terminates and joins only its exact acknowledged child before reusing the fixture.");

            MessageRpcProcessFixture.RpcChild successor = await fixture.StartAsync(fixture.Descriptor("coord-successor", "successor"), binary);
            CrashReport recovered = await fixture.ReportAsync(successor);
            Assert.AreEqual("Acquired", recovered.CoordinationOutcome);
            Assert.IsTrue(Generation(recovered.Generation) > firstGeneration);
            Assert.AreEqual(4, new[] { firstInstance, busy.InstanceId, recovered.InstanceId, fixture.Broker.Local.InstanceId }
                .Distinct(StringComparer.Ordinal).Count());
            Assert.AreEqual("Replayed", recovered.Business);
            Assert.AreEqual("Committed", recovered.Completion);
            Assert.AreEqual("guarded:7", recovered.Value);
            Assert.AreEqual(1L, recovered.EffectCount);
            Assert.AreEqual(1L, recovered.JournalCount);
            Assert.AreEqual(coreHash, recovered.CoreHash);
            Assert.AreEqual(committed.GetProperty("MessageId").GetGuid(), recovered.MessageId);
            Assert.AreEqual(committed.GetProperty("OutgoingRecordId").GetGuid(), recovered.OutgoingRecordId);
            Assert.AreEqual(committed.GetProperty("OutgoingDigest").GetString(), recovered.OutgoingDigest);
            EncodedMessage received = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.AreEqual(recovered.MessageId, received.Envelope.MessageId);
            Assert.AreEqual(recovered.OutgoingDigest, Convert.ToHexStringLower(received.Digest.CopyBytes()));
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("outbox")} WHERE state=2"));
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT total::bigint FROM {fixture.Server.Table("rpc_effect_counter")}"));
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM \"{fixture.OperationSchema}\".operation_journal"));
            TestContext.WriteLine("COORDINATION-PACKAGE-PROCESSES " + JsonSerializer.Serialize(new
            {
                FirstInstance = firstInstance,
                FirstGeneration = firstGeneration.ToString(CultureInfo.InvariantCulture),
                ContenderInstance = busy.InstanceId,
                Successor = recovered
            }));
            package.AssertOriginalAssembliesUnchanged();
        }
        catch (ProcessTimeoutException failure)
        {
            cleanupConfirmed &= failure.CleanupCompleted && failure.Result.OutputDrained;
            throw;
        }
        catch (ProcessOutputLimitException failure)
        {
            cleanupConfirmed &= failure.Result.OutputDrained;
            throw;
        }
        finally
        {
            try
            {
                if (fixture is not null)
                {
                    await fixture.DisposeAsync();
                }
            }
            catch
            {
                cleanupConfirmed = false;
                throw;
            }
            finally
            {
                if (cleanupConfirmed)
                {
                    package.Dispose();
                }
                else
                {
                    TestContext.WriteLine("Retained current public package input because owned process cleanup is unconfirmed: " + package.Root);
                }
            }
        }
    }

    private async Task<string> BuildPublicConsumerAsync(RestPackage package)
    {
        await MessagingPackageConsumerTests.ImportPinnedNpgsqlAsync(package);
        using (ZipArchive core = ZipFile.OpenRead(Path.Combine(package.Feed, "CIS.Core." + package.Version + ".nupkg")))
        {
            ZipArchiveEntry resource = core.GetEntry("contentFiles/any/any/CIS.Coordination/PostgreSql/install-v1.sql")!;
            Assert.IsNotNull(resource);
            using Stream input = resource.Open();
            string sourceSql = Path.Combine(CandidatePackageFixture.Repository(),
                "implementations/csharp/src/CIS.Core/Coordination/PostgreSql/install-v1.sql");
            Assert.AreEqual(CandidatePackageFixture.Hash(sourceSql), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input)));
        }

        string app = Directory.CreateDirectory(Path.Combine(package.Root, "coordination")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        const string assemblyName = "CIS.Coordination.PublicPackageConsumer.Processes";
        string source = Path.Combine(CandidatePackageFixture.Repository(),
            "implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/CrashHost");
        string[] inputs = [Path.Combine(source, "Program.cs"), Path.Combine(source, "Contracts.cs")];
        string[] hashes = inputs.Select(CandidatePackageFixture.Hash).ToArray();
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName),
                new XElement("EnableDefaultCompileItems", false)),
            new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.Messaging.RabbitMQ" }.Select(id => new XElement("PackageReference", new XAttribute("Include", id),
                    new XAttribute("Version", "[" + package.Version + "]"))),
                new XElement("PackageReference", new XAttribute("Include", "Npgsql"), new XAttribute("Version", "[10.0.4]")),
                inputs.Select(path => new XElement("Compile", new XAttribute("Include", path), new XAttribute("Link", Path.GetFileName(path))))))).Save(project);
        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        MessagingPackageConsumerTests.AssertRuntimeRestore(package, app);
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        package.AssertDeployed(output, assemblyName);
        Assert.AreEqual(package.DependencyHash("npgsql.10.0.4.nupkg", "Npgsql"), CandidatePackageFixture.Hash(Path.Combine(output, "Npgsql.dll")));
        CollectionAssert.AreEqual(hashes, inputs.Select(CandidatePackageFixture.Hash).ToArray(), "A package test compiles the existing source unchanged.");
        return Path.Combine(output, assemblyName + ".dll");
    }

    private static long Generation(string value) => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
}
