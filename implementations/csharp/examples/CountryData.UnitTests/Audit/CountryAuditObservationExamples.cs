using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.CommonTesting;
using CIS.Examples.CountryData.Audit;
using CIS.Messaging;
using CIS.Startup;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Examples.CountryData.UnitTests.Audit;

[TestClass]
public sealed class CountryAuditObservationExamples
{
    [TestMethod]
    [DataRow(MessageSubmissionOutcome.OutboxCommitted, AuditSubmissionOutcome.OutboxCommitted)]
    [DataRow(MessageSubmissionOutcome.NotSubmitted, AuditSubmissionOutcome.NotSubmitted)]
    [DataRow(MessageSubmissionOutcome.Unknown, AuditSubmissionOutcome.Unknown)]
    public async Task ExplicitObservationReturnsActualStandaloneFactsWithoutRepeatingUnknown(
        MessageSubmissionOutcome outcome, AuditSubmissionOutcome expected)
    {
        await using var catalog = new CountryCatalogExample();
        await catalog.StartAsync();
        var writer = new ScriptedAuditWriter(scriptCapacity: 2, recordingCapacity: 2);
        MessageSubmission submission = Submission(outcome);
        writer.EnqueueSubmit(submission);
        writer.EnqueueSubmit(Submission(MessageSubmissionOutcome.OutboxCommitted));
        await using ServiceProvider provider = Services(catalog.Catalog, writer).BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        CountryCatalogAuditObservation observation = scope.ServiceProvider.GetRequiredService<CountryCatalogAuditObservation>();

        AuditSubmissionResult result = await observation.SubmitAsync(" mLt ");

        Assert.AreEqual(expected, result.Outcome);
        Assert.AreSame(submission, result.Submission);
        Assert.AreSame(submission.Receipt, result.Receipt);
        Assert.AreSame(submission.Completion, result.Completion);
        Assert.HasCount(1, writer.Calls);
        Assert.AreEqual(ScriptedAuditCallKind.Submit, writer.Calls[0].Kind);
        Assert.AreEqual(result.AuditId, writer.Calls[0].AuditId);
        Assert.IsNull(writer.Calls[0].DatabaseName);
        Assert.AreEqual(1, writer.PendingScripts, "Unknown must not consume a second submission script.");
        Assert.AreEqual(1L, catalog.Rest.TotalCalls, "The observation reads the current catalog without another provider request.");
    }

    [TestMethod]
    public async Task PublicTrustedCaptureRetainsOnlyTheExplicitTypedCurrentCountryFact()
    {
        await using var catalog = new CountryCatalogExample();
        await catalog.StartAsync();
        var writer = new ScriptedAuditWriter();
        await using ServiceProvider provider = Services(catalog.Catalog, writer).BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        CountryCatalogAuditObservation observation = scope.ServiceProvider.GetRequiredService<CountryCatalogAuditObservation>();

        CapturedAuditRecord captured = observation.Capture("malta");

        Assert.AreEqual("country.catalog.observed", captured.Record.EventType);
        Assert.AreEqual(1, captured.Record.EventVersion);
        Assert.AreEqual(AuditCategory.Business, captured.Record.Category);
        Assert.AreEqual(AuditOutcome.Succeeded, captured.Record.Outcome);
        Assert.AreEqual(AuditActorKind.Unknown, captured.Record.Actor.Kind, "No trusted caller was selected for this test scope.");
        Assert.AreEqual(EnvironmentOptions.ApplicationName, captured.Record.Producer.Application);
        Assert.AreEqual(EnvironmentOptions.EnvironmentName, captured.Record.Producer.Environment);
        Assert.IsNotNull(captured.Record.Target);
        Assert.AreEqual("country", captured.Record.Target.ResourceType);
        Assert.AreEqual("MT", captured.Record.Target.ResourceIdentifier);
        Assert.IsNull(captured.Record.OperationReference);
        byte[] frozen = captured.OriginalBytes.ToArray();
        Assert.IsTrue(MemoryMarshal.TryGetArray(captured.OriginalBytes, out ArraySegment<byte> callerCopy));
        callerCopy.Array![callerCopy.Offset] ^= 1;
        CollectionAssert.AreEqual(frozen, captured.OriginalBytes.ToArray());
        using JsonDocument details = JsonDocument.Parse(captured.Record.DetailsJson);
        CollectionAssert.AreEquivalent(new[] { "name", "iso2", "iso3" }, details.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("Malta", details.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("MT", details.RootElement.GetProperty("iso2").GetString());
        Assert.AreEqual("MLT", details.RootElement.GetProperty("iso3").GetString());
        Assert.IsTrue(new AuditRecordCodec().TryDecode(captured.OriginalBytes, out AuditRecord? decoded, out _));
        Assert.IsNotNull(decoded);
        CollectionAssert.AreEqual(frozen, new AuditRecordCodec().Encode(decoded));
        Assert.IsEmpty(writer.Calls, "Capture alone provides no submission fact.");
    }

    [TestMethod]
    public async Task MissingCountryCanceledCallerAndOriginalSubmissionFaultHaveNoHiddenRetry()
    {
        await using var catalog = new CountryCatalogExample();
        await catalog.StartAsync();
        var writer = new ScriptedAuditWriter(scriptCapacity: 1, recordingCapacity: 1);
        var original = new InvalidOperationException("Original scripted submission fault.");
        writer.EnqueueSubmitFailure(original);
        await using ServiceProvider provider = Services(catalog.Catalog, writer).BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        CountryCatalogAuditObservation observation = scope.ServiceProvider.GetRequiredService<CountryCatalogAuditObservation>();
        Assert.ThrowsExactly<KeyNotFoundException>(() => observation.Capture("not-a-country"));
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        OperationCanceledException canceled = Assert.ThrowsExactly<OperationCanceledException>(() => observation.Capture("MT", caller.Token));
        Assert.AreEqual(caller.Token, canceled.CancellationToken);
        Assert.AreEqual(1, writer.PendingScripts);
        Assert.IsEmpty(writer.Calls);

        Exception observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => observation.SubmitAsync("MT"));

        Assert.AreSame(original, observed);
        Assert.HasCount(1, writer.Calls);
        Assert.AreEqual(0, writer.PendingScripts);
    }

    [TestMethod]
    public void OptInRegistrationIsPassiveAndAddsNoStartupWorkOrDefaultWriter()
    {
        var services = new ServiceCollection();

        services.AddCountryCatalogAuditObservation();
        services.AddCountryCatalogAuditObservation();

        Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == typeof(CountryCatalogAuditObservation)));
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(IStartable)));
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(IAuditWriter)));
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(IAuditCapture)));
    }

    [TestMethod]
    public void ExplicitCountryDetailsSchemaRefusesInvalidUnicodeControlsAndNoncanonicalCodes()
    {
        IAuditDetailsCodec<Country> codec = CountryCatalogAuditObservation.Definition.DetailsCodec;
        codec.Validate(new(string.Concat(Enumerable.Repeat("😀", 512)), "MT", "MLT"));
        Assert.ThrowsExactly<ArgumentException>(() => codec.Validate(new(new string('a', 513), "MT", "MLT")));
        Assert.ThrowsExactly<ArgumentException>(() => codec.Validate(new("bad\nname", "MT", "MLT")));
        Assert.ThrowsExactly<ArgumentException>(() => codec.Validate(new("bad\ud800", "MT", "MLT")));
        Assert.ThrowsExactly<ArgumentException>(() => codec.Validate(new("Malta", "mt", "MLT")));
    }

    private static IServiceCollection Services(CountryCatalog catalog, ScriptedAuditWriter writer)
    {
        var services = new ServiceCollection();
        services.ConfigureCisAudit(new("example-audit"));
        services.AddSingleton(catalog);
        services.AddSingleton<IAuditWriter>(writer);
        services.AddCountryCatalogAuditObservation();
        return services;
    }

    private static MessageSubmission Submission(MessageSubmissionOutcome outcome)
    {
        if (outcome == MessageSubmissionOutcome.NotSubmitted)
        {
            return new(outcome, error: new("audit.example_refusal", "Selected scripted refusal."));
        }
        TransactionOutcome persistence = outcome == MessageSubmissionOutcome.OutboxCommitted
            ? TransactionOutcome.Committed : TransactionOutcome.Unknown;
        var completion = new TransactionCompletion(persistence, TransactionEndReason.CommitRequested, true, false, false);
        var receipt = new MessageStageReceipt(Guid.NewGuid(), Guid.NewGuid(), "Example", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, new(new byte[32]));
        return new(outcome, receipt, completion);
    }
}
