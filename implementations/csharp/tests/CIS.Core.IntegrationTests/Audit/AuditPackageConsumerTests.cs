using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Audit;
using CIS.CommonTesting;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CurrentCoreAndCommonTestingPackagesSupportPublicAuditCaptureAndExactBoundedWriterSubstitution()
    {
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "Audit.Helpers.Package",
            typeof(IAuditWriter).Assembly, typeof(ScriptedAuditWriter).Assembly);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "audit-consumer")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        const string assemblyName = "CIS.Audit.PublicPackageConsumer";
        File.WriteAllText(Path.Combine(app, "Program.cs"), ConsumerProgram);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.CommonTesting" }.Select(id => new XElement("PackageReference",
                    new XAttribute("Include", id), new XAttribute("Version", "[" + package.Version + "]")))))).Save(project);
        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        string[] restored = RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json"))).ToArray();
        string[] required = ["CIS.Core/" + package.Version, "CIS.CommonTesting/" + package.Version];
        string[] approved = ["CIS.Core/" + package.Version, "CIS.CommonTesting/" + package.Version,
            "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        CollectionAssert.AreEquivalent(required,
            restored.Where(dependency => dependency.StartsWith("CIS.", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(restored.All(dependency => approved.Contains(dependency, StringComparer.Ordinal)),
            "Every retained package must use the exact approved closure; the shared framework may prune the Microsoft abstraction packages.");
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        package.AssertDeployed(output, assemblyName);
        var process = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(output, assemblyName + ".dll")], package.Root);
        Assert.AreEqual("", process.StandardError);
        JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
        foreach (string fact in new[] { "passiveWithoutWriter", "noNativeProviders", "trustedPublicCapture", "immutableBytes",
            "explicitSelectedDetails", "decodedRemainsPortable", "noPublicSourcePromotion", "stagedWithoutCompletingOwner",
            "notSubmitted", "committed", "unknown", "exactResultFacts", "boundedMetadata", "scriptCapacity",
            "recordCapacity", "preCancellation", "originalCancellationFault", "noDefaultSuccess", "outsideFriendAssembly" })
        {
            Assert.IsTrue(report[fact]!.GetValue<bool>(), fact);
        }
        Assert.IsFalse(report["nativeProviderLoaded"]!.GetValue<bool>());
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CommonTesting.dll"), report["helperPath"]!.GetValue<string>());
        Assert.AreEqual(CandidatePackageFixture.Hash(typeof(IAuditWriter).Assembly.Location), report["coreHash"]!.GetValue<string>());
        Assert.AreEqual(CandidatePackageFixture.Hash(typeof(ScriptedAuditWriter).Assembly.Location), report["helperHash"]!.GetValue<string>());
        TestContext.WriteLine("AUDIT-PACKAGE-HELPERS " + process.StandardOutput);
        package.AssertOriginalAssembliesUnchanged();
    }

    private const string ConsumerProgram = """
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Security.Cryptography;
        using System.Text;
        using System.Text.Json;
        using CIS.Audit;
        using CIS.Audit.Hosting;
        using CIS.CommonTesting;
        using CIS.Messaging;
        using CIS.Security;
        using CIS.Transactions;
        using Microsoft.Extensions.DependencyInjection;

        var services = new ServiceCollection();
        services.ConfigureCisAudit(new AuditOptions("audit"));
        bool passiveWithoutWriter = !services.Any(descriptor => descriptor.ServiceType == typeof(IAuditWriter));
        var scripts = new ScriptedAuditWriter(scriptCapacity: 4, recordingCapacity: 4);
        services.AddSingleton<IAuditWriter>(scripts);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IAuditCapture capture = scope.ServiceProvider.GetRequiredService<IAuditCapture>();
        IAuditWriter port = provider.GetRequiredService<IAuditWriter>();
        var definition = new AuditEventDefinition<SelectedDetails>("example.amount.recorded", 1, AuditCategory.Business,
            new DetailsCodec(), requireTarget: true, requireReason: true);
        var input = new SelectedDetails { Amount = "9007199254740993.123456789", ExternalId = "9223372036854775808" };
        var options = new AuditCaptureOptions(AuditOutcome.Succeeded,
            target: new AuditTarget("resource-owner", "test", "ledger", "ledger-one"), reason: new AuditReason("example.recorded"));
        CapturedAuditRecord captured = capture.Capture(definition, input, options);
        CapturedAuditRecord system = capture.CaptureLocalSystem(definition, input, options);
        byte[] original = captured.OriginalBytes.ToArray();
        input.Amount = "changed";
        input.ExternalId = "changed";
        var codec = new AuditRecordCodec();
        bool immutableBytes = MemoryMarshal.TryGetArray(captured.OriginalBytes, out ArraySegment<byte> detached);
        if (immutableBytes)
        {
            detached.Array![detached.Offset] = (byte)'!';
        }
        immutableBytes &= original.AsSpan().SequenceEqual(codec.Encode(captured.Record));
        if (MemoryMarshal.TryGetArray(captured.Record.DetailsJson, out ArraySegment<byte> detachedDetails))
        {
            detachedDetails.Array![detachedDetails.Offset] = (byte)'!';
        }
        immutableBytes &= original.AsSpan().SequenceEqual(codec.Encode(captured.Record));
        bool explicitSelectedDetails;
        using (JsonDocument details = JsonDocument.Parse(captured.Record.DetailsJson))
        {
            explicitSelectedDetails = details.RootElement.GetProperty("amount").GetString() == "9007199254740993.123456789"
                && details.RootElement.GetProperty("externalId").GetString() == "9223372036854775808"
                && !details.RootElement.TryGetProperty("notSelected", out _);
        }
        bool decodedRemainsPortable = codec.TryDecode(original, out AuditRecord? decoded, out AuditWireFailure failure)
            && failure == AuditWireFailure.None && decoded is not null && original.AsSpan().SequenceEqual(codec.Encode(decoded));
        bool noPublicSourcePromotion = typeof(CapturedAuditRecord).GetConstructors().Length == 0
            && typeof(IAuditWriter).GetMethods().All(method => method.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(CapturedAuditRecord)));
        bool trustedPublicCapture = captured.Record.Actor.Kind == AuditActorKind.Unknown
            && captured.Record.Actor.Subject is null && system.Record.Actor.Kind == AuditActorKind.System
            && system.Record.Actor.Subject is null && captured.AuditId != system.AuditId
            && captured.Record.Producer.Application == LocalApplicationIdentity.Capture().Application
            && captured.Record.Producer.Environment == LocalApplicationIdentity.Capture().Environment
            && captured.Record.Producer.Instance!.Kind == AuditInstanceKind.Reported
            && captured.Record.OccurredAtUtc == captured.Record.RecordedAtUtc;
        bool noNativeProviders = provider.GetService<IMessagingStore>() is null
            && provider.GetService<IMessageTransport>() is null && provider.GetService<ILocalTransactionFactory>() is null
            && provider.GetService<IMessageOutbox>() is null && provider.GetService<ICallerContextAccessor>() is null;

        MessageStageReceipt Receipt() => new(Guid.NewGuid(), Guid.NewGuid(), "Ledger",
            captured.Record.Producer.Application, captured.Record.Producer.Environment, new(new byte[32]));
        var stageWriter = new ScriptedAuditWriter(scriptCapacity: 1, recordingCapacity: 1);
        var stage = new MessageStageResult(MessageStageDisposition.Staged, Receipt());
        stageWriter.EnqueueStage(stage);
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        bool stagedWithoutCompletingOwner;
        await using (ILocalTransaction owner = await transactions.BeginAsync("Ledger"))
        {
            AuditStageResult staged = await stageWriter.StageAsync(owner.Context, captured);
            stagedWithoutCompletingOwner = staged.AuditId == captured.AuditId && ReferenceEquals(stage, staged.Staging)
                && ReferenceEquals(stage.Receipt, staged.Receipt) && owner.Completion is null && !owner.Context.IsRollbackOnly;
            await owner.RollbackAsync();
        }

        var cleanup = new TransactionError(TransactionErrorPhase.TransactionDispose, "transaction.dispose_failed", "Selected safe cleanup context.");
        var unsubmitted = new MessageSubmission(MessageSubmissionOutcome.NotSubmitted,
            error: new MessageError("audit.unavailable", "Selected safe submission context."));
        var committedCompletion = new TransactionCompletion(TransactionOutcome.Committed, TransactionEndReason.CommitRequested,
            true, false, false, cleanupErrors: [cleanup]);
        var committedSubmission = new MessageSubmission(MessageSubmissionOutcome.OutboxCommitted, Receipt(), committedCompletion);
        var unknownCompletion = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
            true, true, true, new TransactionError(TransactionErrorPhase.Commit, "transaction.unknown", "Selected safe commit context."), [cleanup]);
        var unknownSubmission = new MessageSubmission(MessageSubmissionOutcome.Unknown, Receipt(), unknownCompletion,
            new MessageError("audit.unknown", "Selected safe uncertainty context."));
        scripts.EnqueueSubmit(unsubmitted);
        scripts.EnqueueSubmit(committedSubmission);
        scripts.EnqueueSubmit(unknownSubmission);
        var originalFault = new InvalidOperationException("Original scripted writer fault.");
        scripts.EnqueueSubmitFailure(originalFault);
        bool scriptCapacity = false;
        try
        {
            scripts.EnqueueSubmit(unsubmitted);
        }
        catch (InvalidOperationException)
        {
            scriptCapacity = scripts.PendingScripts == 4;
        }
        AuditSubmissionResult first = await port.SubmitAsync(captured);
        AuditSubmissionResult second = await port.SubmitAsync(captured);
        AuditSubmissionResult third = await port.SubmitAsync(captured);
        bool originalFaultPreserved = false;
        try
        {
            await port.SubmitAsync(captured);
        }
        catch (InvalidOperationException error)
        {
            originalFaultPreserved = ReferenceEquals(error, originalFault);
        }
        bool exactResultFacts = ReferenceEquals(first.Submission, unsubmitted) && ReferenceEquals(first.Error, unsubmitted.Error)
            && first.Receipt is null && first.Completion is null
            && ReferenceEquals(second.Submission, committedSubmission) && ReferenceEquals(second.Receipt, committedSubmission.Receipt)
            && ReferenceEquals(second.Completion, committedCompletion) && ReferenceEquals(second.Completion!.CleanupErrors[0], cleanup)
            && ReferenceEquals(third.Submission, unknownSubmission) && ReferenceEquals(third.Receipt, unknownSubmission.Receipt)
            && ReferenceEquals(third.Completion, unknownCompletion) && ReferenceEquals(third.Error, unknownSubmission.Error)
            && third.Completion!.RollbackConfirmed && originalFaultPreserved;
        scripts.EnqueueSubmit(unsubmitted);
        bool recordCapacity = false;
        try
        {
            await port.SubmitAsync(captured);
        }
        catch (InvalidOperationException)
        {
            recordCapacity = scripts.PendingScripts == 1 && scripts.Calls.Count == 4;
        }
        IReadOnlyList<ScriptedAuditCall> metadata = scripts.Calls;
        bool boundedMetadata = metadata.Count == 4 && metadata.All(call => call.AuditId == captured.AuditId
            && call.Kind == ScriptedAuditCallKind.Submit && call.DatabaseName is null)
            && stageWriter.Calls.Single().DatabaseName == "Ledger"
            && typeof(ScriptedAuditCall).GetProperties().All(property => property.PropertyType == typeof(ScriptedAuditCallKind)
                || property.PropertyType == typeof(Guid) || property.PropertyType == typeof(string));

        var cancelScripts = new ScriptedAuditWriter();
        var originalCancellation = new OperationCanceledException("Original independent cancellation fault.");
        cancelScripts.EnqueueSubmitFailure(originalCancellation);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool preCancellation = false;
        Task<AuditSubmissionResult> canceledTask = cancelScripts.SubmitAsync(captured, canceled.Token);
        try
        {
            await canceledTask;
        }
        catch (OperationCanceledException error)
        {
            preCancellation = canceledTask.IsCanceled && error.CancellationToken == canceled.Token
                && cancelScripts.PendingScripts == 1 && cancelScripts.Calls.Count == 0;
        }
        bool originalCancellationFault = false;
        Task<AuditSubmissionResult> faultedTask = cancelScripts.SubmitAsync(captured);
        try
        {
            await faultedTask;
        }
        catch (OperationCanceledException error)
        {
            originalCancellationFault = faultedTask.IsFaulted && ReferenceEquals(error, originalCancellation);
        }
        bool noDefaultSuccess = false;
        try
        {
            await new ScriptedAuditWriter().SubmitAsync(captured);
        }
        catch (InvalidOperationException)
        {
            noDefaultSuccess = true;
        }

        string corePath = typeof(IAuditWriter).Assembly.Location;
        string helperPath = typeof(ScriptedAuditWriter).Assembly.Location;
        string consumerName = Assembly.GetEntryAssembly()!.GetName().Name!;
        bool outsideFriendAssembly = !typeof(IAuditWriter).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Any(friend => new AssemblyName(friend.AssemblyName).Name == consumerName);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passiveWithoutWriter, noNativeProviders, trustedPublicCapture, immutableBytes, explicitSelectedDetails,
            decodedRemainsPortable, noPublicSourcePromotion, stagedWithoutCompletingOwner,
            notSubmitted = first.Outcome == AuditSubmissionOutcome.NotSubmitted,
            committed = second.Outcome == AuditSubmissionOutcome.OutboxCommitted,
            unknown = third.Outcome == AuditSubmissionOutcome.Unknown,
            exactResultFacts, boundedMetadata, scriptCapacity, recordCapacity, preCancellation, originalCancellationFault,
            noDefaultSuccess, outsideFriendAssembly, corePath, helperPath,
            coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(corePath))),
            helperHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(helperPath))),
            nativeProviderLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is { } name
                && (name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("RabbitMQ", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("TestFramework", StringComparison.OrdinalIgnoreCase)))
        }));

        sealed class SelectedDetails
        {
            public required string Amount { get; set; }
            public required string ExternalId { get; set; }
            public string NotSelected { get; } = "not-selected";
        }

        sealed class DetailsCodec : IAuditDetailsCodec<SelectedDetails>
        {
            public void Validate(SelectedDetails details)
            {
                ArgumentNullException.ThrowIfNull(details);
                ArgumentException.ThrowIfNullOrEmpty(details.Amount);
                ArgumentException.ThrowIfNullOrEmpty(details.ExternalId);
                if (details.Amount.Length > 128 || details.ExternalId.Length > 128)
                {
                    throw new ArgumentException("Selected example details exceed their explicit schema bounds.");
                }
            }

            public void Write(Utf8JsonWriter writer, SelectedDetails details)
            {
                writer.WriteStartObject();
                writer.WriteString("amount", details.Amount);
                writer.WriteString("externalId", details.ExternalId);
                writer.WriteEndObject();
            }
        }
        """;
}
