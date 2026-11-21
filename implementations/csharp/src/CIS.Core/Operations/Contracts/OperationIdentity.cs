using CIS.Security;

namespace CIS.Operations;

/// <summary>The complete ordinal operation identity, excluding instance, deployment, tracing and credentials.</summary>
public sealed record OperationIdentity
{
    /// <summary>Copies the five bounded identity fields. Constructing identity data grants no authority.</summary>
    public OperationIdentity(string application, string environment, string scope, string operationName, string key)
    {
        var target = new CapabilityTarget(application, environment);
        Application = target.Application;
        Environment = target.Environment;
        Scope = OperationText.Utf8(scope, 256, nameof(scope));
        OperationName = OperationText.Alias(operationName, nameof(operationName));
        Key = OperationText.Key(key, nameof(key));
    }

    /// <summary>Gets the trusted server application.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the trusted server environment.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the application-resolved logical scope.</summary>
    public string Scope
    {
        get;
    }
    /// <summary>Gets the registered operation alias.</summary>
    public string OperationName
    {
        get;
    }
    /// <summary>Gets the opaque case-sensitive caller key.</summary>
    public string Key
    {
        get;
    }
}

/// <summary>A stable cross-language contract identifier and positive version, never a CLR type name.</summary>
public sealed record OperationContract
{
    /// <summary>Creates a bounded stable identifier and version.</summary>
    public OperationContract(string identifier, int version)
    {
        Identifier = OperationText.Contract(identifier, nameof(identifier));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Version = version;
    }

    /// <summary>Gets the exact stable ASCII identifier.</summary>
    public string Identifier
    {
        get;
    }
    /// <summary>Gets the positive contract version.</summary>
    public int Version
    {
        get;
    }
}

/// <summary>A copied SHA-256 binding to a particular fingerprint contract.</summary>
public sealed class OperationFingerprint
{
    private readonly byte[] digest;

    /// <summary>Copies exactly 32 digest bytes. Runtime fingerprints are computed server-side by the canonical frame.</summary>
    public OperationFingerprint(OperationContract contract, ReadOnlyMemory<byte> digest)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (digest.Length != 32)
        {
            throw new ArgumentException("A SHA-256 digest contains exactly 32 bytes.", nameof(digest));
        }
        Contract = contract;
        this.digest = digest.ToArray();
    }

    /// <summary>Gets the recorded fingerprint contract.</summary>
    public OperationContract Contract
    {
        get;
    }
    /// <summary>Gets a detached copy of the digest.</summary>
    public ReadOnlyMemory<byte> Digest => digest.ToArray();
    internal ReadOnlySpan<byte> DigestSpan => digest;
}
