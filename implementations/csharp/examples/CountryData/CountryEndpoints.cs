namespace CIS.Examples.CountryData;

public static class CountryEndpoints
{
    public static IResult Get(string identifier, CountryCatalog catalog)
    {
        string key = identifier.Trim();
        if (key.Length == 0) { return Results.BadRequest(new { code = "country.invalid_identifier" }); }
        return catalog.TryGet(key, out Country? country)
            ? Results.Ok(country) : Results.NotFound(new { code = "country.not_found" });
    }
}
