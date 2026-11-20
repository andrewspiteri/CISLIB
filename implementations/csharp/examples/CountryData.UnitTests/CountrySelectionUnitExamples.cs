using CIS.Examples.CountryData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Examples.CountryData.UnitTests;

[TestClass]
public sealed class CountrySelectionUnitExamples
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DefaultCallerPerformsOneHarmlessReadAndRequiresAnActualBusinessValue(bool missingValue)
    {
        var client = new CIS.CommonTesting.ScriptedRestClient();
        client.EnqueueJsonValue(missingValue ? "null" : System.Text.Json.JsonSerializer.Serialize(new Country("Malta", "MT", "MLT")));

        CountryCallerResult result = await CountryCaller.ExecuteAsync(client);

        Assert.AreEqual(!missingValue, result.Success);
        Assert.AreEqual(missingValue ? "country.missing_response" : null, result.ErrorCode);
        Assert.AreEqual(missingValue ? null : "MT", result.Country?.Iso2);
        Assert.IsNull(result.Selection);
        Assert.AreEqual(1L, client.TotalCalls);
        Assert.AreEqual("GET", client.Calls.Single().Method);
        Assert.AreEqual("/country/MT", client.Calls.Single().Url);
    }

    [TestMethod]
    [DataRow(" mt ")]
    [DataRow("MLT")]
    [DataRow("Malta")]
    public async Task AliasesResolveToTheSameActualCachedResourceWithoutProviderIo(string identifier)
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        Country country = CountrySecurityResource.Resolve(example.Catalog, identifier);
        Assert.AreEqual("MT", country.Iso2);
        Assert.AreEqual(1L, example.Rest.TotalCalls);
        var selection = new CountrySelection();
        CountrySelectionSnapshot before = selection.Capture();
        CountrySelectionSnapshot after = selection.Select(country);
        Assert.IsNull(before.Country);
        Assert.AreEqual(0L, before.Revision);
        Assert.AreEqual(1L, after.Revision);
        Assert.AreSame(country, after.Country);
        Assert.AreSame(after, selection.Capture());
    }

    [TestMethod]
    public async Task MissingResourceCannotChangeTheSelectedValueOrItsRevision()
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        var selection = new CountrySelection();
        CountrySelectionSnapshot selected = selection.Select(CountrySecurityResource.Resolve(example.Catalog, "MT"));
        Assert.ThrowsExactly<Microsoft.AspNetCore.Http.BadHttpRequestException>(() =>
            selection.Select(CountrySecurityResource.Resolve(example.Catalog, "unknown")));
        Assert.AreSame(selected, selection.Capture());
        Assert.AreEqual(1L, example.Rest.TotalCalls);
    }
}
