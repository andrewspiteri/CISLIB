using System.Globalization;
using System.Text.RegularExpressions;

namespace CIS.Verification;

// Supports the retained GNU date -u --iso-8601=ns observation without rewriting
// its comma or nanoseconds. Older exact observation formats remain accepted.
internal static class HealthcheckMutationUtcObservation
{
    private static readonly string[] LegacyFormats =
        ["O", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"];
    private static readonly Regex GnuUtcNanoseconds = new(
        @"\A(?<seconds>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}),(?<fraction>[0-9]{9})\+00:00\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal readonly record struct Observation(long UtcTicks, byte SubTickNanoseconds, string Raw)
    {
        public int CompareTo(Observation other)
        {
            int wholeTicks = UtcTicks.CompareTo(other.UtcTicks);
            return wholeTicks != 0 ? wholeTicks : SubTickNanoseconds.CompareTo(other.SubTickNanoseconds);
        }

        internal bool IsBefore(DateTimeOffset instant) =>
            CompareTo(new Observation(instant.UtcTicks, 0, instant.ToString("O", CultureInfo.InvariantCulture))) < 0;
    }

    internal static bool TryRead(string raw, out Observation observation)
    {
        observation = default;
        if (raw.Length == 0 || !string.Equals(raw, raw.Trim(), StringComparison.Ordinal)) { return false; }
        if (DateTimeOffset.TryParseExact(raw, LegacyFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTimeOffset legacy))
        {
            observation = new Observation(legacy.UtcTicks, 0, raw);
            return true;
        }

        Match match = GnuUtcNanoseconds.Match(raw);
        if (!match.Success || !DateTimeOffset.TryParseExact(match.Groups["seconds"].Value + "+00:00",
            "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset seconds)
            || !int.TryParse(match.Groups["fraction"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int nanoseconds))
        { return false; }

        // Ticks are100ns. Retaining the remaining two digits prevents rounding,
        // and permits exact comparisons even when both observations have ns.
        observation = new Observation(checked(seconds.UtcTicks + nanoseconds / 100), checked((byte)(nanoseconds % 100)), raw);
        return true;
    }
}
