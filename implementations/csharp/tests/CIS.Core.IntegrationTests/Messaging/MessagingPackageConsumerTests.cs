using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.RestClient;
using CIS.Tests.MessagingCrashFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagingPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("RequiresDocker")]
    [TestCategory("PostgreSql")]
    [TestCategory("RabbitMq")]
    public async Task ActualPublicPackagedHostCommitsTypedQueryAndDurableRequestRepliesAndReplaysOneOriginalEffect()
    {
        RestPackage package = await RestPackage.PrepareAsync(TestContext, "Messaging.Rpc.Package",
            typeof(MessageEnvelope).Assembly, typeof(RabbitMqOptions).Assembly);
        bool packageCleanupConfirmed = true;
        try
        {
            await ImportPinnedNpgsqlAsync(package);
            string app = Directory.CreateDirectory(Path.Combine(package.Root, "rpc")).FullName;
            string project = Path.Combine(app, "Consumer.csproj");
            const string assemblyName = "CIS.Messaging.PublicPackageConsumer.Rpc";
            string source = Path.Combine(CandidatePackageFixture.Repository(),
                "implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/CrashHost");
            string[] inputs = [Path.Combine(source, "Program.cs"), Path.Combine(source, "Contracts.cs")];
            string[] inputHashes = inputs.Select(CandidatePackageFixture.Hash).ToArray();
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName),
                    new XElement("EnableDefaultCompileItems", false)),
                new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                    new[] { "CIS.Core", "CIS.Messaging.RabbitMQ" }.Select(id => new XElement("PackageReference", new XAttribute("Include", id),
                        new XAttribute("Version", "[" + package.Version + "]"))),
                    new XElement("PackageReference", new XAttribute("Include", "Npgsql"), new XAttribute("Version", "[10.0.4]")),
                    inputs.Select(path => new XElement("Compile", new XAttribute("Include", path), new XAttribute("Link", Path.GetFileName(path))))))).Save(project);
            await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
            AssertRuntimeRestore(package, app);
            await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
            string output = Path.Combine(app, "bin/Release/net10.0");
            package.AssertDeployed(output, assemblyName);
            Assert.AreEqual(package.DependencyHash("npgsql.10.0.4.nupkg", "Npgsql"),
                CandidatePackageFixture.Hash(Path.Combine(output, "Npgsql.dll")));

            MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync(minimum: true);
            try
            {
                CrashDescriptor descriptor = fixture.Descriptor("roundtrip", "public-package", minimum: true);
                MessageRpcProcessFixture.RpcChild child = await fixture.StartAsync(descriptor, Path.Combine(output, assemblyName + ".dll"));
                ProcessResult process = await child.Execution;
                packageCleanupConfirmed &= process.OutputDrained;
                Assert.IsTrue(process.OutputDrained && !process.CaptureTruncated);
                Assert.AreEqual(0, process.ExitCode, process.StandardError);
                Assert.AreEqual("", process.StandardError);
                CrashReport report = await fixture.ReportAsync(child);
                Assert.AreEqual(fixture.Broker.Local.Application, report.Application);
                Assert.AreEqual(fixture.Broker.Local.Environment, report.Environment);
                Assert.AreEqual("OutboxCommitted", report.Submission);
                Assert.AreEqual("Committed", report.Completion);
                Assert.AreEqual("ResponseReceived", report.Exchange);
                Assert.AreEqual("Completed", report.Protocol);
                Assert.AreEqual("Succeeded", report.Business);
                Assert.AreEqual("original:7", report.Value);
                Assert.AreEqual(report.Value, report.ReplayedValue);
                Assert.AreEqual("query:7", report.QueryValue);
                Assert.AreEqual(1L, report.EffectCount);
                Assert.AreEqual(1L, report.JournalCount);
                Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT total::bigint FROM {fixture.Server.Table("rpc_effect_counter")}"));
                Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM \"{fixture.OperationSchema}\".operation_journal"));
                Assert.AreNotEqual(Guid.Empty, report.MessageId);
                Assert.AreNotEqual(report.MessageId, report.SecondMessageId);
                Assert.AreNotEqual(Guid.Empty, report.AttemptId);
                Assert.AreNotEqual(report.AttemptId, report.SecondAttemptId);
                Assert.AreNotEqual(Guid.Empty, report.EndpointId);
                Assert.AreEqual(CandidatePackageFixture.Hash(typeof(MessageEnvelope).Assembly.Location), report.CoreHash);
                TestContext.WriteLine("MESSAGING-PACKAGE-RPC " + JsonSerializer.Serialize(report));
            }
            catch (ProcessTimeoutException failure)
            {
                packageCleanupConfirmed &= failure.CleanupCompleted;
                throw;
            }
            catch (ProcessOutputLimitException failure)
            {
                packageCleanupConfirmed &= failure.Result.OutputDrained;
                throw;
            }
            finally
            {
                try
                {
                    await fixture.DisposeAsync();
                }
                catch
                {
                    packageCleanupConfirmed = false;
                    throw;
                }
            }
            CollectionAssert.AreEqual(inputHashes, inputs.Select(CandidatePackageFixture.Hash).ToArray(), "Package consumption must not rewrite the shared public entry source.");
            package.AssertOriginalAssembliesUnchanged();
        }
        finally
        {
            if (packageCleanupConfirmed)
            {
                package.Dispose();
            }
            else
            {
                TestContext.WriteLine("Retained packaged RPC consumer because owned child cleanup is unconfirmed: " + package.Root);
            }
        }
    }

    private static async Task ImportPinnedNpgsqlAsync(RestPackage package)
    {
        string assetsPath = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp/tests/CIS.Core.IntegrationTests/obj/project.assets.json");
        JsonObject assets = RestPackage.Assets(assetsPath);
        JsonNode library = assets["libraries"]!["Npgsql/10.0.4"]!;
        Assert.AreEqual("package", library["type"]!.GetValue<string>());
        string? source = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, "npgsql", "10.0.4", "npgsql.10.0.4.nupkg"))
            .FirstOrDefault(File.Exists);
        Assert.IsNotNull(source, "Use only the existing selected test-only provider archive.");
        string directory = Path.GetDirectoryName(source)!;
        JsonObject metadata = RestPackage.Assets(Path.Combine(directory, ".nupkg.metadata"));
        Assert.AreEqual(library["sha512"]!.GetValue<string>(), metadata["contentHash"]!.GetValue<string>());
        byte[] bytes = await File.ReadAllBytesAsync(source);
        Assert.AreEqual((await File.ReadAllTextAsync(source + ".sha512")).Trim(), Convert.ToBase64String(SHA512.HashData(bytes)));
        using (ZipArchive archive = ZipFile.OpenRead(source))
        {
            using Stream manifest = archive.GetEntry("Npgsql.nuspec")!.Open();
            XDocument identity = XDocument.Load(manifest);
            Assert.AreEqual("Npgsql", identity.Descendants().Single(element => element.Name.LocalName == "id").Value);
            Assert.AreEqual("10.0.4", identity.Descendants().Single(element => element.Name.LocalName == "version").Value);
            using Stream assembly = archive.GetEntry("lib/net10.0/Npgsql.dll")!.Open();
            Assert.AreEqual(CandidatePackageFixture.Hash(typeof(Npgsql.NpgsqlConnection).Assembly.Location), Convert.ToHexStringLower(SHA256.HashData(assembly)));
        }
        await File.WriteAllBytesAsync(Path.Combine(package.Feed, "npgsql.10.0.4.nupkg"), bytes);
    }

    private static void AssertRuntimeRestore(RestPackage package, string app)
    {
        JsonObject assets = RestPackage.Assets(Path.Combine(app, "obj/project.assets.json"));
        Assert.AreEqual(1, assets["packageFolders"]!.AsObject().Count);
        string cache = Path.Combine(package.Root, "packages");
        RestPackage.SamePath(cache, assets["packageFolders"]!.AsObject().Single().Key);
        Assert.IsTrue(assets["libraries"]!.AsObject().All(item => item.Value!["type"]!.GetValue<string>() == "package"));
        string[] approved = ["CIS.Core/" + package.Version, "CIS.Messaging.RabbitMQ/" + package.Version,
            "Npgsql/10.0.4", "RabbitMQ.Client/7.2.2", "Microsoft.Extensions.Logging.Abstractions/10.0.12",
            "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        string[] restored = RestPackage.Packages(assets).ToArray();
        Assert.IsTrue(restored.All(dependency => approved.Contains(dependency, StringComparer.Ordinal)));
        foreach (string required in approved.Take(4))
        {
            CollectionAssert.Contains(restored, required);
        }
        string[] archives = Directory.GetFiles(package.Feed, "*.nupkg");
        foreach (string dependency in restored)
        {
            string[] identity = dependency.Split('/');
            string id = identity[0].ToLowerInvariant();
            string version = identity[1].ToLowerInvariant();
            string file = id + "." + version + ".nupkg";
            string selected = archives.Single(path => string.Equals(Path.GetFileName(path), file, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(CandidatePackageFixture.Hash(selected), CandidatePackageFixture.Hash(Path.Combine(cache, id, version, file)));
        }
    }

    [TestMethod]
    public async Task CurrentCommonTestingPackageExposesPublicMessagingSubstitutesWithoutNativeProviders()
    {
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "Messaging.Helpers.Package",
            typeof(MessageEnvelope).Assembly, typeof(ScriptedMessagingClient).Assembly);
        using (ZipArchive archive = ZipFile.OpenRead(Path.Combine(package.Feed, "CIS.CommonTesting." + package.Version + ".nupkg")))
        {
            Assert.IsNotNull(archive.GetEntry("lib/net10.0/CIS.CommonTesting.dll"));
            Assert.IsNotNull(archive.GetEntry("lib/net10.0/CIS.CommonTesting.xml"));
            Assert.IsNotNull(archive.GetEntry("README.md"));
            using Stream manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            string[] dependencies = XDocument.Load(manifest).Descendants().Where(element => element.Name.LocalName == "dependency")
                .Select(element => (string)element.Attribute("id")!).ToArray();
            CollectionAssert.AreEquivalent(new[] { "CIS.Core" }, dependencies);
        }
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "helpers")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        const string assemblyName = "CIS.Messaging.PublicPackageConsumer.Helpers";
        File.WriteAllText(Path.Combine(app, "Program.cs"), HelperProgram);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.CommonTesting" }.Select(id => new XElement("PackageReference", new XAttribute("Include", id),
                    new XAttribute("Version", "[" + package.Version + "]")))))).Save(project);
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
        foreach (string fact in new[] { "publication", "unsecuredDelivery", "typedRequest", "typedWait", "ticketOwnership", "originalFault", "finiteRecords" })
        {
            Assert.IsTrue(report[fact]!.GetValue<bool>(), fact);
        }
        Assert.IsFalse(report["nativeProviderLoaded"]!.GetValue<bool>());
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CommonTesting.dll"), report["helperPath"]!.GetValue<string>());
        Assert.AreEqual(CandidatePackageFixture.Hash(typeof(MessageEnvelope).Assembly.Location), report["coreHash"]!.GetValue<string>());
        Assert.AreEqual(CandidatePackageFixture.Hash(typeof(ScriptedMessagingClient).Assembly.Location), report["helperHash"]!.GetValue<string>());
        TestContext.WriteLine("MESSAGING-PACKAGE-HELPERS " + process.StandardOutput);
        package.AssertOriginalAssembliesUnchanged();
    }

    [TestMethod]
    public async Task CurrentCoreAndOptionalPackagesExposePublicWireSchemaAndPassiveHostingWithoutNativeStartup()
    {
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "Messaging.Package",
            typeof(MessageEnvelope).Assembly, typeof(RabbitMqOptions).Assembly);
        using ZipArchive core = ZipFile.OpenRead(Path.Combine(package.Feed, "CIS.Core." + package.Version + ".nupkg"));
        ZipArchiveEntry? installation = core.GetEntry("contentFiles/any/any/CIS.Messaging/PostgreSql/install-v2.sql");
        Assert.IsNotNull(installation);
        string installationHash;
        using (var reader = new StreamReader(installation.Open()))
        {
            string selected = (await reader.ReadToEndAsync()).Replace("__CIS_MESSAGING_SCHEMA__", "\"package_fixture\"", StringComparison.Ordinal);
            installationHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(selected)));
        }
        VerifyOptionalArchive(package);

        foreach (bool optional in new[] { false, true })
        {
            string app = Directory.CreateDirectory(Path.Combine(package.Root, optional ? "optional" : "core-only")).FullName;
            string project = Path.Combine(app, "Consumer.csproj");
            string assemblyName = "CIS.Messaging.PublicPackageConsumer." + (optional ? "Optional" : "CoreOnly");
            string[] selectedIds = optional ? ["CIS.Core", "CIS.Messaging.RabbitMQ"] : ["CIS.Core"];
            File.WriteAllText(Path.Combine(app, "Program.cs"), ConsumerProgram);
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName),
                    new XElement("DefineConstants", optional ? "RABBIT" : "")),
                new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                    selectedIds.Select(id => new XElement("PackageReference", new XAttribute("Include", id),
                        new XAttribute("Version", "[" + package.Version + "]")))))).Save(project);
            await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
            package.AssertRestored(app, selectedIds);
            string[] approved = ["CIS.Core/" + package.Version, "Microsoft.Extensions.Logging.Abstractions/10.0.12",
                "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12", "CIS.Messaging.RabbitMQ/" + package.Version,
                "RabbitMQ.Client/7.2.2"];
            string[] restored = RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json"))).ToArray();
            Assert.IsTrue(restored.All(dependency => approved.Contains(dependency, StringComparer.Ordinal)));
            Assert.AreEqual(optional, restored.Contains("RabbitMQ.Client/7.2.2", StringComparer.Ordinal));
            Assert.AreEqual(optional, restored.Contains("CIS.Messaging.RabbitMQ/" + package.Version, StringComparer.Ordinal));

            await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
            string output = Path.Combine(app, "bin/Release/net10.0");
            JsonObject dependencies = package.AssertDeployed(output, assemblyName, selectedIds);
            Assert.AreEqual(optional, File.Exists(Path.Combine(output, "RabbitMQ.Client.dll")));
            Assert.AreEqual(optional, File.Exists(Path.Combine(output, "CIS.Messaging.RabbitMQ.dll")));
            Assert.IsFalse(dependencies["libraries"]!.AsObject().Any(item => item.Key.StartsWith("Npgsql/", StringComparison.OrdinalIgnoreCase)
                || item.Key.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                || item.Key.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)));
            var process = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                Path.Combine(output, assemblyName + ".dll")], package.Root);
            Assert.AreEqual("", process.StandardError);
            JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
            foreach (string fact in new[] { "ownedWire", "payloadPrecision", "schemaPublic", "passiveGraph", "immutableSettings" })
            {
                Assert.IsTrue(report[fact]!.GetValue<bool>(), fact);
            }
            Assert.AreEqual(optional, report["optionalSelected"]!.GetValue<bool>());
            Assert.IsFalse(report["unselectedProviderLoaded"]!.GetValue<bool>());
            Assert.AreEqual(installationHash, report["installationHash"]!.GetValue<string>());
            RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
            Assert.AreEqual(CandidatePackageFixture.Hash(typeof(MessageEnvelope).Assembly.Location), report["coreHash"]!.GetValue<string>());
            if (optional)
            {
                RestPackage.SamePath(Path.Combine(output, "CIS.Messaging.RabbitMQ.dll"), report["adapterPath"]!.GetValue<string>());
                Assert.AreEqual(CandidatePackageFixture.Hash(typeof(RabbitMqOptions).Assembly.Location), report["adapterHash"]!.GetValue<string>());
            }
            TestContext.WriteLine("MESSAGING-PACKAGE-PASSIVE " + process.StandardOutput);
        }
        package.AssertOriginalAssembliesUnchanged();
    }

    private static void VerifyOptionalArchive(RestPackage package)
    {
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(package.Feed, "CIS.Messaging.RabbitMQ." + package.Version + ".nupkg"));
        Assert.IsNotNull(archive.GetEntry("lib/net10.0/CIS.Messaging.RabbitMQ.dll"));
        Assert.IsNotNull(archive.GetEntry("lib/net10.0/CIS.Messaging.RabbitMQ.xml"));
        Assert.IsNotNull(archive.GetEntry("README.md"));
        using Stream manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        XDocument nuspec = XDocument.Load(manifest);
        string[] dependencies = nuspec.Descendants().Where(element => element.Name.LocalName == "dependency")
            .Select(element => (string)element.Attribute("id")!).ToArray();
        CollectionAssert.AreEquivalent(new[] { "CIS.Core", "RabbitMQ.Client" }, dependencies);
        Assert.IsTrue(nuspec.Descendants().Any(element => element.Name.LocalName == "frameworkReference"
            && (string?)element.Attribute("name") == "Microsoft.AspNetCore.App"));
    }

    // Deliberately outside every friend assembly. This checks package boundaries and passive
    // composition only; real PostgreSQL/RabbitMQ runtime evidence belongs to separate tests.
    private const string ConsumerProgram = """
        using System.Security.Cryptography;
        using System.Text;
        using System.Text.Json;
        using CIS.Messaging;
        using CIS.Messaging.Hosting;
        using CIS.Messaging.PostgreSql;
        using CIS.Security;
        using Microsoft.Extensions.DependencyInjection;
        #if RABBIT
        using CIS.Messaging.RabbitMQ;
        #endif

        const string payloadText = "{\"amount\":12345678901234567890.12345678901234567890,\"text\":\"0001\"}";
        byte[] payload = Encoding.UTF8.GetBytes(payloadText);
        var codec = new ExplicitJsonCodec();
        var envelope = new MessageEnvelope(Guid.Parse("65b4736b-04a1-46f4-9c3c-e574490be199"), MessageKind.Event,
            codec.Contract, "package-producer", "package", DateTimeOffset.UnixEpoch, payload);
        payload[0] = 0;
        EncodedMessage encoded = MessageWire.Encode(envelope);
        byte[] detached = encoded.CopyBytes();
        detached[0] = 0;
        bool decoded = MessageWire.TryDecode(encoded.CopyBytes(), out EncodedMessage? received, out _);
        bool ownedWire = decoded && received!.Digest.Equals(encoded.Digest)
            && new MessageDigest(SHA256.HashData(encoded.CopyBytes())).Equals(encoded.Digest);
        bool payloadPrecision = decoded && Encoding.UTF8.GetString(received!.Envelope.CopyPayload()) == payloadText;

        var services = new ServiceCollection();
        var options = new MessagingOptions { MaximumConcurrentDeliveries = 2 };
        services.ConfigureCisMessaging(options);
        services.AddCisMessageRoute(new MessageRouteDefinition<string>("events", MessageKind.Event, codec));
        services.AddCisMessageStore(new("Messaging", "Ledger"));
        services.AddCisMessageSubscription(new MessageSubscriptionDefinition<string>("Amounts", "Ledger", "events", codec,
            _ => throw new InvalidOperationException("Passive registration must not resolve a handler."),
            (_, _, _, _) => throw new InvalidOperationException("Passive registration must not resolve a target."),
            ["amount.read"]));
        services.AddCisMessaging();
        services.AddCisMessaging();
        options.MaximumConcurrentDeliveries = 3;
        MessagingSnapshot snapshot = (MessagingSnapshot)services.Single(item => item.ServiceType == typeof(MessagingSnapshot)).ImplementationInstance!;
        snapshot.Validate();
        MessagingComposition composition = (MessagingComposition)services.Single(item => item.ServiceType == typeof(MessagingComposition)).ImplementationInstance!;
        bool passiveGraph = composition.RouteAliases.SequenceEqual(["events"])
            && composition.SubscriptionAliases.SequenceEqual(["Amounts"])
            && composition.StoreAliases.SequenceEqual(["Messaging"])
            && services.Count(item => item.ServiceType == typeof(IMessagingClient)) == 1
            && services.Count(item => item.ServiceType == typeof(IMessageOutbox)) == 1
            && services.Count(item => item.ServiceType == typeof(IMessageAdministration)) == 1;
        bool immutableSettings = snapshot.Settings.MaximumConcurrentDeliveries == 2;
        bool optionalSelected = false;
        string? adapterPath = null;
        string? adapterHash = null;
        #if RABBIT
        var native = new RabbitMqOptions
        {
            HostName = "127.0.0.1", Port = 1, ServerName = "localhost", UserName = "package-only",
            Password = "unused-package-fixture-value", EndpointNamespaces = [new("package-producer", "package", "package")]
        };
        services.AddCisRabbitMqMessaging(native);
        native.MaximumQueueMessages = 17;
        RabbitMqSettings captured = (RabbitMqSettings)services.Single(item => item.ServiceType == typeof(RabbitMqSettings)).ImplementationInstance!;
        optionalSelected = services.Count(item => item.ServiceType == typeof(IMessageTransport)) == 1
            && captured.Port == 1 && captured.MaximumQueueMessages == 10_000;
        adapterPath = typeof(RabbitMqOptions).Assembly.Location;
        adapterHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(adapterPath)));
        #endif
        string installation = PostgreSqlMessagingSchema.GetInstallationSql("package_fixture");
        string corePath = typeof(MessageEnvelope).Assembly.Location;
        bool unselectedProviderLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is { } name
            && (name.StartsWith("Npgsql", StringComparison.Ordinal) || name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || name.StartsWith("Polly", StringComparison.Ordinal)
        #if !RABBIT
                || name.StartsWith("RabbitMQ", StringComparison.Ordinal) || name == "CIS.Messaging.RabbitMQ"
        #endif
                ));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ownedWire, payloadPrecision, passiveGraph, immutableSettings, optionalSelected, unselectedProviderLoaded,
            schemaPublic = PostgreSqlMessagingSchema.Version == 2 && installation.Contains("\"package_fixture\"", StringComparison.Ordinal),
            installationHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(installation))),
            corePath, coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(corePath))), adapterPath, adapterHash
        }));

        sealed class ExplicitJsonCodec : IMessageCodec<string>
        {
            public MessageContract Contract { get; } = new("package.amount", 1);
            public byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
            public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out string? value)
            {
                value = contract == Contract ? Encoding.UTF8.GetString(json.Span) : null;
                return value is not null;
            }
        }
        """;

    private const string HelperProgram = """
        using System.Security.Cryptography;
        using System.Text.Json;
        using CIS.CommonTesting;
        using CIS.Messaging;
        using CIS.Operations;

        EncodedMessage message = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Event,
            new("package.number", 1), "package", "test", DateTimeOffset.UnixEpoch, "1"u8.ToArray()));
        await using var publisher = new ScriptedMessagePublisher(scriptCapacity: 1, recordingCapacity: 1);
        var confirmation = new MessagePublicationResult(MessagePublicationDisposition.Confirmed);
        publisher.EnqueueResult(confirmation);
        bool publication = ReferenceEquals(confirmation, await publisher.PublishAsync(
            new(message, new("events"), MessageRecordPurpose.Application, 1, Guid.NewGuid())))
            && publisher.Calls.Single().Digest.Equals(message.Digest);
        await using var delivery = new ScriptedMessageDelivery(message.CopyBytes(), "A", new("events"));
        delivery.EnqueueQuarantine(false);
        bool unsecuredDelivery = await delivery.GetNativeEvidenceAsync() is null
            && !await delivery.QuarantineMalformedAsync("package.malformed")
            && delivery.CompletedSettlement is null;

        var receipt = new MessageStageReceipt(Guid.NewGuid(), message.Envelope.MessageId, "Ledger", "package", "test", message.Digest);
        var reference = new MessageRequestReference("Lookup", Guid.NewGuid(), receipt,
            new("package", "test", Guid.NewGuid()), new("package.result", 1));
        var ticket = new ScriptedMessageRequestTicket<string>(reference);
        var expected = new MessageRequestResult<string>(MessageExchangeOutcome.ResponseReceived,
            new(MessageSubmissionOutcome.OutboxCommitted, receipt), reference,
            new(MessageProtocolDisposition.Completed, OperationDecision<string>.Succeed("original")));
        var client = new ScriptedMessagingClient(scriptCapacity: 1, recordingCapacity: 3);
        IMessagingClient port = client;
        client.EnqueueRequest<int, string>(expected);
        bool typedRequest = ReferenceEquals(expected, await port.RequestAsync<int, string>(new("Lookup", 7)));
        client.EnqueueWaitForReply(expected);
        bool typedWait = ReferenceEquals(expected, await port.WaitForReplyAsync(ticket))
            && client.Calls[1].RequestAttemptId == reference.RequestAttemptId;
        bool ticketOwnership = !ticket.DisposeRequested;
        await ticket.DisposeAsync();
        await ticket.DisposeAsync();
        ticketOwnership &= ticket.DisposeRequested;

        var original = new OperationCanceledException("Explicit unrelated scripted fault");
        client.EnqueueRequestFailure<int, string>(original);
        Task<MessageRequestResult<string>> faulted = port.RequestAsync<int, string>(new("Lookup", 7));
        bool originalFault = false;
        try
        {
            await faulted;
        }
        catch (OperationCanceledException error)
        {
            originalFault = ReferenceEquals(original, error) && faulted.IsFaulted;
        }
        client.EnqueueSend<int>(new(MessageSubmissionOutcome.NotSubmitted));
        bool finiteRecords = false;
        try
        {
            await port.SendAsync("commands", 1);
        }
        catch (InvalidOperationException)
        {
            finiteRecords = client.PendingScripts == 1 && client.Calls.Count == 3;
        }
        string corePath = typeof(MessageEnvelope).Assembly.Location;
        string helperPath = typeof(ScriptedMessagingClient).Assembly.Location;
        bool nativeProviderLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is { } name
            && (name.StartsWith("RabbitMQ", StringComparison.Ordinal) || name == "CIS.Messaging.RabbitMQ"
                || name.StartsWith("Npgsql", StringComparison.Ordinal) || name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || name.StartsWith("Polly", StringComparison.Ordinal)));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            publication, unsecuredDelivery, typedRequest, typedWait, ticketOwnership, originalFault, finiteRecords, nativeProviderLoaded,
            corePath, coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(corePath))),
            helperPath, helperHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(helperPath)))
        }));
        """;
}
