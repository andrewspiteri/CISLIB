using System.Reflection;
using System.Security;

namespace CIS.Configuration;

internal static class OptionalMetadataDiscovery
{
    public static T? Read<T>(Func<T?> discover) where T : class
    {
        try
        {
            return discover();
        }
        catch (Exception exception) when (exception is
            NotSupportedException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            CustomAttributeFormatException or
            AmbiguousMatchException or
            TypeLoadException or
            ReflectionTypeLoadException)
        {
            // Unavailable facts remain absent. Unexpected faults and cancellation still propagate.
            return null;
        }
    }
}
