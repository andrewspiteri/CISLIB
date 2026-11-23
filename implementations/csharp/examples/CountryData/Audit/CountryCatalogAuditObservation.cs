using System.Buffers;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Examples.CountryData.Audit;

/// <summary>An explicitly invoked observation of a country present in the current in-memory catalog.</summary>
/// <remarks>This is standalone Audit submission. It neither changes the catalog nor makes its earlier publication transactional.</remarks>
public sealed class CountryCatalogAuditObservation
{
    private readonly CountryCatalog catalog;
    private readonly IAuditCapture capture;
    private readonly IAuditWriter writer;

    public CountryCatalogAuditObservation(CountryCatalog catalog, IAuditCapture capture, IAuditWriter writer)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(writer);

        this.catalog = catalog;
        this.capture = capture;
        this.writer = writer;
    }

    /// <summary>Gets the explicit safe details schema: name (at most 512 scalars), uppercase ISO2 and ISO3.</summary>
    public static AuditEventDefinition<Country> Definition
    {
        get;
    } = new("country.catalog.observed", 1,
        AuditCategory.Business, new CountryDetailsCodec(), requireTarget: true);

    /// <summary>Captures one current catalog fact with the actual trusted caller and local producer.</summary>
    public CapturedAuditRecord Capture(string identifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(identifier);
        if (!catalog.TryGet(identifier, out Country? country) || country is null)
        {
            throw new KeyNotFoundException("The current catalog does not contain that country.");
        }

        return capture.Capture(Definition, country, new(AuditOutcome.Succeeded,
            target: new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, "country", country.Iso2)),
            cancellationToken);
    }

    /// <summary>Captures once and returns the actual standalone submission; Unknown is never automatically repeated.</summary>
    public Task<AuditSubmissionResult> SubmitAsync(string identifier, CancellationToken cancellationToken = default)
        => writer.SubmitAsync(Capture(identifier, cancellationToken), cancellationToken);

    private sealed class CountryDetailsCodec : IAuditDetailsCodec<Country>
    {
        public void Validate(Country details)
        {
            ArgumentNullException.ThrowIfNull(details);
            if (!ValidName(details.Name) || !Code(details.Iso2, 2) || !Code(details.Iso3, 3))
            {
                throw new ArgumentException("Select a bounded country name and its canonical uppercase ISO codes.", nameof(details));
            }
        }

        public void Write(Utf8JsonWriter writer, Country details)
        {
            writer.WriteStartObject();
            writer.WriteString("name", details.Name);
            writer.WriteString("iso2", details.Iso2);
            writer.WriteString("iso3", details.Iso3);
            writer.WriteEndObject();
        }

        private static bool Code(string? value, int length)
            => value?.Length == length && value.All(character => character is >= 'A' and <= 'Z');

        private static bool ValidName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
            {
                return false;
            }
            ReadOnlySpan<char> remaining = value.AsSpan();
            int scalars = 0;
            while (!remaining.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(remaining, out Rune scalar, out int consumed) != OperationStatus.Done
                    || Rune.IsControl(scalar) || ++scalars > 512)
                {
                    return false;
                }
                remaining = remaining[consumed..];
            }
            return true;
        }
    }
}

/// <summary>Explicit application composition only; the default tutorial never selects this observation.</summary>
public static class CountryCatalogAuditObservationRegistration
{
    /// <summary>Registers a scoped helper. Applications separately configure actual Audit and Messaging delivery.</summary>
    public static IServiceCollection AddCountryCatalogAuditObservation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<CountryCatalogAuditObservation>();
        return services;
    }
}
