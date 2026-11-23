using CIS.Management.Service.Persistence;
using CIS.Management.Service.Metadata;

namespace CIS.Management.Service;

/// <summary>Immutable deployment inputs. Secrets are supplied by the application, never by request data.</summary>
public sealed class ManagementServiceOptions
{
    private readonly byte[] cursorSigningKey;

    public ManagementServiceOptions(string connectionString, ReadOnlySpan<byte> cursorSigningKey,
        ManagementPersistenceOptions? persistence = null, string basePath = "", int maximumConcurrentRequests = 32,
        string operationsSchema = "cis_operations", string messagingSchema = "cis_messaging",
        ManagementDeliveryOptions? delivery = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (connectionString.Length > 16384 || cursorSigningKey.Length is < 32 or > 64)
        {
            throw new ArgumentException("Supply bounded database settings and a deployment-shared 32 through 64 byte cursor key.");
        }
        if (basePath.Length > 256 || basePath.Length != 0 && (!basePath.StartsWith('/') || basePath.EndsWith('/')
            || basePath.Split('/').Skip(1).Any(segment => segment.Length == 0 || segment is "." or ".."
                || segment.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))))
        {
            throw new ArgumentException("The optional base path contains bounded literal safe path segments.", nameof(basePath));
        }
        if (maximumConcurrentRequests is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentRequests));
        }
        ConnectionString = connectionString;
        this.cursorSigningKey = cursorSigningKey.ToArray();
        Persistence = persistence ?? new();
        BasePath = basePath;
        MaximumConcurrentRequests = maximumConcurrentRequests;
        OperationsSchema = Schema(operationsSchema);
        MessagingSchema = Schema(messagingSchema);
        Delivery = delivery;
    }

    public string ConnectionString
    {
        get;
    }
    public ManagementPersistenceOptions Persistence
    {
        get;
    }
    public string BasePath
    {
        get;
    }
    public int MaximumConcurrentRequests
    {
        get;
    }
    public string OperationsSchema
    {
        get;
    }
    public string MessagingSchema
    {
        get;
    }
    public ManagementDeliveryOptions? Delivery
    {
        get;
    }
    internal byte[] CopyCursorSigningKey() => cursorSigningKey.ToArray();

    private static string Schema(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]{0,62}$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant) ? value
            : throw new ArgumentException("Use a bounded PostgreSQL schema identifier.");
}
