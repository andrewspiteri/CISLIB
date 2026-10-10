using System.ComponentModel;
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
        catch (Exception exception) when (IsExpectedDiscoveryFailure(exception))
        {
            return null;
        }
    }

    public static T? ReadValue<T>(Func<T?> discover) where T : struct
    {
        try
        {
            return discover();
        }
        catch (Exception exception) when (IsExpectedDiscoveryFailure(exception))
        {
            return null;
        }
    }

    private static bool IsExpectedDiscoveryFailure(Exception exception) => exception is
        NotSupportedException or
        InvalidOperationException or
        IOException or
        UnauthorizedAccessException or
        SecurityException or
        Win32Exception or
        CustomAttributeFormatException or
        AmbiguousMatchException or
        TypeLoadException or
        ReflectionTypeLoadException;
}
