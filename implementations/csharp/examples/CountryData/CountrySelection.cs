using CIS.Security;

namespace CIS.Examples.CountryData;

/// <summary>The example's one bounded selected-country value; catalog refresh remains independent.</summary>
public sealed class CountrySelection
{
    private readonly object gate = new();
    private CountrySelectionSnapshot current = new(null, 0);

    public CountrySelectionSnapshot Capture()
    {
        lock (gate)
        {
            return current;
        }
    }

    public CountrySelectionSnapshot Select(Country country)
    {
        ArgumentNullException.ThrowIfNull(country);
        lock (gate)
        {
            current = new(country, checked(current.Revision + 1));
            return current;
        }
    }
}

public sealed record CountrySelectionSnapshot(Country? Country, long Revision);

/// <summary>Resolves the actual cached resource before authorization and subsequent mutation.</summary>
public static class CountrySecurityResource
{
    private static readonly object ResolvedCountry = new();

    public static Country Resolve(CountryCatalog catalog, string identifier)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(identifier) || !catalog.TryGet(identifier, out Country? country) || country is null)
        {
            throw new BadHttpRequestException("A known country identifier is required.", StatusCodes.Status404NotFound);
        }
        return country;
    }

    public static ValueTask<CapabilityTarget> ResolveTarget(HttpContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string identifier = context.Request.RouteValues["identifier"] as string ?? "";
        Country country = Resolve(context.RequestServices.GetRequiredService<CountryCatalog>(), identifier);
        context.Items[ResolvedCountry] = country;
        CountrySecuritySettings settings = context.RequestServices.GetRequiredService<CountrySecuritySnapshot>().Settings;
        return ValueTask.FromResult(new CapabilityTarget(settings.ServiceApplication, settings.Environment, "country", country.Iso2));
    }

    internal static Country AuthorizedCountry(HttpContext context)
        => context.Items.TryGetValue(ResolvedCountry, out object? value) && value is Country country
            ? country : throw new InvalidOperationException("Resolve and authorize the actual country before executing this endpoint.");
}
