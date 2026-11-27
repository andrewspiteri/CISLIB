using System.Security.Cryptography;
using System.Text.Json;

namespace CIS.Management.Service.Configuration;

internal static class ManagementPredeploymentConfiguration
{
    internal static ManagementConfigurationSchema Definition(string configurationId)
        => new(configurationId, "1", new("object", false, []), "{}");

    internal static byte[] Digest(ManagementConfigurationSchema definition)
        => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition with
        {
            Description = null
        }, ManagementConfigurationOptions.Json));

    internal static bool Matches(ManagementConfigurationSchema definition, byte[] retainedDigest)
        => definition.SchemaVersion == "1" && CryptographicOperations.FixedTimeEquals(Digest(definition), retainedDigest)
            && CryptographicOperations.FixedTimeEquals(Digest(Definition(definition.ConfigurationId)), retainedDigest);
}
