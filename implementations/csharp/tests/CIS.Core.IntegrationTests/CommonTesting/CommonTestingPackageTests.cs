using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.CommonTesting;
using CIS.RestClient.Http;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CommonTesting;

[TestClass]
[DoNotParallelize]
public sealed class CommonTestingPackageTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualOptionalPackageContainsTheCurrentPublicHelpersAndOnlyCoreAsItsPackageDependency()
    {
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "CommonTesting.Package",
            typeof(HttpRestClient).Assembly, typeof(ManualHeartbeat).Assembly);
        string archive = Path.Combine(package.Feed, "CIS.CommonTesting." + package.Version + ".nupkg");
        using ZipArchive zip = ZipFile.OpenRead(archive);
        using Stream manifest = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        XDocument nuspec = XDocument.Load(manifest);
        XElement[] dependencies = nuspec.Descendants().Where(element => element.Name.LocalName == "dependency").ToArray();
        Assert.HasCount(1, dependencies);
        Assert.AreEqual("CIS.Core", (string?)dependencies[0].Attribute("id"));
        Assert.IsTrue(nuspec.Descendants().Any(element => element.Name.LocalName == "frameworkReference"
            && (string?)element.Attribute("name") == "Microsoft.AspNetCore.App"));
        Assert.IsNotNull(zip.GetEntry("lib/net10.0/CIS.CommonTesting.dll"));
        Assert.IsNotNull(zip.GetEntry("lib/net10.0/CIS.CommonTesting.xml"));
        Assert.IsNotNull(zip.GetEntry("README.md"));
        Assert.IsFalse(typeof(ScriptedRestClient).Assembly.GetReferencedAssemblies()
            .Any(name => name.Name is { } id && (id.Contains("TestFramework", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Moq", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
                || id.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase))));
        Assert.IsFalse(typeof(CIS.Transactions.ILocalTransactionFactory).Assembly.GetReferencedAssemblies()
            .Any(name => name.Name is { } id && (id.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                || id.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase))));

        string app = Directory.CreateDirectory(Path.Combine(package.Root, "app")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        const string assemblyName = "CIS.CommonTesting.PublicPackageConsumer";
        File.WriteAllText(Path.Combine(app, "Program.cs"), """
            using System.Text.Json;
            using CIS.CommonTesting;
            using CIS.RestClient;
            using CIS.Transactions;
            using CIS.Transactions.AdoNet;
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            clock.Advance(TimeSpan.FromSeconds(1));
            var scripts = new ScriptedLocalTransactionFactory(scriptCapacity: 2, recordingCapacity: 3);
            ILocalTransactionFactory substituted = scripts;
            scripts.Enqueue();
            var business = new FakeStore();
            var outgoing = new FakeStore();
            TransactionCompletion refused;
            bool sharedContext;
            string? firstReason;
            await using (ILocalTransaction owner = await substituted.BeginAsync("Ledger"))
            {
                await business.WriteAsync(owner.Context);
                await outgoing.WriteAsync(owner.Context);
                sharedContext = ReferenceEquals(business.Received, owner.Context)
                    && ReferenceEquals(outgoing.Received, owner.Context);
                owner.Context.MarkRollbackOnly("app.refused");
                owner.Context.MarkRollbackOnly("app.later");
                firstReason = owner.Context.RollbackReasonCode;
                refused = await owner.CommitAsync();
            }

            var cleanup = new[]
            {
                new TransactionError(TransactionErrorPhase.Rollback, "transaction.rollback_failed", "Scripted rollback failure."),
                new TransactionError(TransactionErrorPhase.TransactionDispose, "transaction.transaction_disposal_failed", "Scripted transaction disposal failure."),
                new TransactionError(TransactionErrorPhase.ConnectionDispose, "transaction.connection_disposal_failed", "Scripted connection disposal failure.")
            };
            scripts.Enqueue(new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
                commitAttempted: true, rollbackAttempted: true, rollbackConfirmed: false,
                new TransactionError(TransactionErrorPhase.Commit, "transaction.commit_outcome_unknown", "Scripted uncertain commit."), cleanup));
            TransactionCompletion uncertain;
            await using (ILocalTransaction owner = await substituted.BeginAsync("Ledger"))
            {
                uncertain = await owner.CommitAsync();
            }

            scripts.Enqueue();
            bool nativeRejected = false;
            int nativeCalls = 0;
            TransactionCompletion rolledBack;
            await using (ILocalTransaction owner = await substituted.BeginAsync("Ledger"))
            {
                try
                {
                    await owner.Context.UseAdoNetAsync((_, _, _) =>
                    {
                        nativeCalls++;
                        return Task.CompletedTask;
                    });
                }
                catch (NotSupportedException)
                {
                    nativeRejected = true;
                }
                rolledBack = await owner.RollbackAsync();
            }
            scripts.Enqueue();
            bool recordingExhausted = false;
            try
            {
                await substituted.BeginAsync("Ledger");
            }
            catch (InvalidOperationException)
            {
                recordingExhausted = true;
            }
            var queueBound = new ScriptedLocalTransactionFactory(scriptCapacity: 1);
            queueBound.Enqueue();
            bool queueExhausted = false;
            try
            {
                queueBound.Enqueue();
            }
            catch (InvalidOperationException)
            {
                queueExhausted = true;
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                elapsed = clock.GetElapsedTime(0).TotalSeconds,
                testing = typeof(ManualHeartbeat).Assembly.Location,
                core = typeof(IRestClient).Assembly.Location,
                sharedContext,
                rollbackRefused = refused.Outcome == TransactionOutcome.NotCommitted && !refused.CommitAttempted
                    && refused.EndReason == TransactionEndReason.RollbackOnly,
                firstReason,
                unknown = uncertain.Outcome == TransactionOutcome.Unknown && uncertain.CommitAttempted,
                cleanupCount = uncertain.CleanupErrors.Count,
                rollbackNormalized = rolledBack.Outcome == TransactionOutcome.NotCommitted
                    && rolledBack.EndReason == TransactionEndReason.RollbackRequested && !rolledBack.CommitAttempted,
                nativeRejectedBeforeCallback = nativeRejected && nativeCalls == 0,
                recordingExhaustedWithoutConsumption = recordingExhausted && scripts.PendingScripts == 1,
                queueExhausted,
                recordings = scripts.CreatedTransactions.Count,
                allDisposed = scripts.CreatedTransactions.All(owner => owner.DisposeRequested),
                optionalLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is { } name
                    && (name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase)))
            }));

            sealed class FakeStore
            {
                public ILocalTransactionContext? Received { get; private set; }
                public Task WriteAsync(ILocalTransactionContext context)
                {
                    Received = context;
                    return Task.CompletedTask;
                }
            }
            """);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.CommonTesting" }.Select(id => new XElement("PackageReference",
                    new XAttribute("Include", id), new XAttribute("Version", "[" + package.Version + "]")))))).Save(project);
        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        string[] approved = ["CIS.Core/" + package.Version, "CIS.CommonTesting/" + package.Version,
            "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        Assert.IsTrue(RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json")))
            .All(dependency => approved.Contains(dependency, StringComparer.Ordinal)));
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        package.AssertDeployed(output, assemblyName);
        var process = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(output, assemblyName + ".dll")], package.Root);
        Assert.AreEqual("", process.StandardError);
        JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
        Assert.AreEqual(1d, report["elapsed"]!.GetValue<double>());
        foreach (string field in new[] { "sharedContext", "rollbackRefused", "unknown", "rollbackNormalized",
            "nativeRejectedBeforeCallback", "recordingExhaustedWithoutConsumption", "queueExhausted", "allDisposed" })
        {
            Assert.IsTrue(report[field]!.GetValue<bool>(), field);
        }
        Assert.AreEqual("app.refused", report["firstReason"]!.GetValue<string>());
        Assert.AreEqual(3, report["cleanupCount"]!.GetValue<int>());
        Assert.AreEqual(3, report["recordings"]!.GetValue<int>());
        Assert.IsFalse(report["optionalLoaded"]!.GetValue<bool>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CommonTesting.dll"), report["testing"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["core"]!.GetValue<string>());
        package.AssertOriginalAssembliesUnchanged();
    }

}
