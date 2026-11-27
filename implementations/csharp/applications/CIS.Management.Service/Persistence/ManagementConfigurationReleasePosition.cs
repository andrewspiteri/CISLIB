using System.Globalization;
using CIS.Management.Wire;

namespace CIS.Management.Service.Persistence;

// This one finite key matches the existing (binding_id,created_at,release_id) index.
internal static class ManagementConfigurationReleasePosition
{
    internal static string Create(ManagementConfigurationRelease release) => release.CreatedAtUtc + "/" + release.ReleaseId;

    internal static bool TryRead(string value, out DateTime created, out Guid id)
    {
        created = default;
        id = default;
        if (value is null)
        {
            return false;
        }
        int separator = value.IndexOf('/');
        if (separator <= 0 || value.Length > 96 || !ManagementWire.IsTimestamp(value[..separator])
            || !ManagementWire.IsUuid(value[(separator + 1)..]))
        {
            return false;
        }
        created = DateTime.ParseExact(value[..separator], "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        id = Guid.Parse(value[(separator + 1)..]);
        return true;
    }

    internal static int Compare(string left, string right)
    {
        if (!TryRead(left, out DateTime first, out Guid firstId) || !TryRead(right, out DateTime second, out Guid secondId))
        {
            throw new ArgumentException("Compare only validated retained-release positions.");
        }
        int time = first.CompareTo(second);
        return time != 0 ? time : StringComparer.Ordinal.Compare(firstId.ToString("D"), secondId.ToString("D"));
    }
}

