using System.Reflection;

namespace CIS.Configuration;

internal static class ConfigurationIdentity
{
    internal static string Create(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        AssemblyName assembly = type.Assembly.GetName();
        if (string.IsNullOrEmpty(type.FullName) || string.IsNullOrEmpty(assembly.Name))
        { throw ConfigurationDiagnostics.Error(type, "Configuration.Identity", "Configuration requires a stable full type and defining assembly name."); }
        string culture = string.IsNullOrEmpty(assembly.CultureName) ? "neutral" : assembly.CultureName;
        byte[] token = assembly.GetPublicKeyToken() ?? [];
        string publicKey = token.Length == 0 ? "null" : Convert.ToHexStringLower(token);
        string name = TypeName(type);
        return name + ", " + assembly.Name + ", Culture=" + culture + ", PublicKeyToken=" + publicKey;
    }

    private static string TypeName(Type type)
    {
        if (type.IsArray)
        { return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]"; }
        return type.IsConstructedGenericType
            ? type.GetGenericTypeDefinition().FullName + "[" + string.Join(",", type.GetGenericArguments().Select(argument => "[" + Create(argument) + "]")) + "]"
            : type.FullName!;
    }
}
