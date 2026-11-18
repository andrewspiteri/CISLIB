using System.Collections.Frozen;

namespace CIS.Examples.CountryData;

public sealed record Country(string Name, string Iso2, string Iso3);

internal sealed record CountrySnapshot(FrozenDictionary<string, Country> Index, int Count, DateTimeOffset? PublishedAt)
{
    internal static CountrySnapshot Empty { get; } = new(FrozenDictionary<string, Country>.Empty, 0, null);

    internal static bool TryCreate(ProviderCountry?[]? rows, DateTimeOffset publishedAt, out CountrySnapshot snapshot)
    {
        snapshot = Empty;
        if (rows is null || rows.Length == 0)
        {
            return false;
        }

        var index = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        var unique = new HashSet<Country>();
        foreach (ProviderCountry? row in rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Name) || !Code(row.Iso2, 2) || !Code(row.Iso3, 3))
            {
                return false;
            }

            var country = new Country(row.Name, row.Iso2!.ToUpperInvariant(), row.Iso3!.ToUpperInvariant());
            // Consistently reject duplicate records rather than choosing a later winner.
            if (!unique.Add(country))
            {
                return false;
            }

            foreach (string alias in new[] { country.Name.Trim(), country.Iso2, country.Iso3 })
            {
                if (!index.TryAdd(alias, country) && !ReferenceEquals(index[alias], country))
                {
                    return false;
                }
            }
        }

        snapshot = new(index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), unique.Count, publishedAt);
        return true;
    }

    private static bool Code(string? value, int length)
        => value?.Length == length && value.All(character => char.IsAsciiLetter(character));
}
