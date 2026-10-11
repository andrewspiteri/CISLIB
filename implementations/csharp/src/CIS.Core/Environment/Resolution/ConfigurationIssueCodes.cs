namespace CIS.Configuration;

/// <summary>Stable codes for configuration validation problems.</summary>
public static class ConfigurationIssueCodes
{
    /// <summary>Identifies InvalidToken configuration problems.</summary>
    public const string InvalidToken = "InvalidToken";
    /// <summary>Identifies OutOfRange configuration problems.</summary>
    public const string OutOfRange = "OutOfRange";
    /// <summary>Identifies UnsupportedSchemaVersion configuration problems.</summary>
    public const string UnsupportedSchemaVersion = "UnsupportedSchemaVersion";
    /// <summary>Identifies InvalidPath configuration problems.</summary>
    public const string InvalidPath = "InvalidPath";
    /// <summary>Identifies InvalidIdentity configuration problems.</summary>
    public const string InvalidIdentity = "InvalidIdentity";
    /// <summary>Identifies InvalidEnvironmentKind configuration problems.</summary>
    public const string InvalidEnvironmentKind = "InvalidEnvironmentKind";
    /// <summary>Identifies MissingApplicationName configuration problems.</summary>
    public const string MissingApplicationName = "MissingApplicationName";
}
