using CIS.Examples.CountryData;
using CIS.Workflows;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CountryData;

[TestClass]
public sealed class CountryLookupStepUnitExamples
{
    [TestMethod]
    [DataRow("  mLt  ", "mLt")]
    [DataRow("Malta", "Malta")]
    public async Task NormalizeTrimsTheKeyAndPreservesTheOriginalIdentifier(string identifier, string expectedKey)
    {
        var context = new CountryLookupContext(identifier);
        var step = new NormalizeCountryIdentifier();

        PipelineStepResult result = await step.Execute(context, CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(expectedKey, context.Key);
        Assert.AreEqual(identifier, context.Identifier);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t ")]
    public async Task BlankIdentifierReturnsAnExpectedFailureAndErrorClearsThePartialKey(string identifier)
    {
        var context = new CountryLookupContext(identifier);
        var step = new NormalizeCountryIdentifier();

        PipelineStepResult result = await step.Execute(context, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("country.invalid_identifier", result.ErrorCode);
        Assert.AreEqual(string.Empty, context.Key);

        await step.Error(context, Failure(result.ErrorCode!), CancellationToken.None);

        Assert.IsNull(context.Key);
    }

    [TestMethod]
    [DataRow("mt")]
    [DataRow(" mLt ")]
    [DataRow("Malta")]
    public async Task FindUsesThePublishedCatalogWithoutAnotherRestCall(string identifier)
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        // Leave a result available: an accidental logical REST call would be admitted and counted.
        example.Rest.EnqueueJsonValue(CountryCatalogExample.InitialData);
        var context = new CountryLookupContext(identifier);
        await new NormalizeCountryIdentifier().Execute(context, CancellationToken.None);
        var step = new FindCachedCountry(example.Catalog);

        PipelineStepResult result = await step.Execute(context, CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(context.Country);
        Assert.AreEqual("MT", context.Country.Iso2);
        Assert.AreEqual(1L, example.Rest.TotalCalls);
    }

    [TestMethod]
    public async Task UnknownCountryIsAReportedFailureWithoutAResponseOrProviderRead()
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        example.Rest.EnqueueJsonValue(CountryCatalogExample.InitialData);
        var context = new CountryLookupContext("unknown");
        await new NormalizeCountryIdentifier().Execute(context, CancellationToken.None);
        var step = new FindCachedCountry(example.Catalog);

        PipelineStepResult result = await step.Execute(context, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("country.not_found", result.ErrorCode);
        Assert.IsNull(context.Country);
        Assert.IsNull(context.Response);
        Assert.AreEqual(1L, example.Rest.TotalCalls);
    }

    [TestMethod]
    public async Task PrepareCreatesAnOkResultAndErrorDiscardsOnlyTheResponseDraft()
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        var context = new CountryLookupContext("MT");
        await new NormalizeCountryIdentifier().Execute(context, CancellationToken.None);
        await new FindCachedCountry(example.Catalog).Execute(context, CancellationToken.None);
        Assert.IsNotNull(context.Country);
        Country selected = context.Country;
        var step = new PrepareCountryResponse();

        PipelineStepResult result = await step.Execute(context, CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(context.Response);
        Assert.IsInstanceOfType<IValueHttpResult>(context.Response);
        Assert.AreSame(selected, ((IValueHttpResult)context.Response).Value);
        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)context.Response).StatusCode);

        await step.Error(context, Failure("example.later_failure"), CancellationToken.None);

        Assert.IsNull(context.Response);
        Assert.AreSame(selected, context.Country);
        Assert.IsTrue(example.Catalog.TryGet("MT", out Country? retained));
        Assert.AreSame(selected, retained);
    }

    private static PipelineFailure Failure(string code)
    {
        return new PipelineFailure(code, "An expected unit-example failure.",
            PipelinePhase.Execute, PipelineFailureKind.ReportedFailure);
    }
}
