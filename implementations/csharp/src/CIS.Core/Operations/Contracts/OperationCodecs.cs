namespace CIS.Operations;

/// <summary>Owned immutable normalized business input and explicit current/historical canonical meanings.</summary>
/// <remarks>The trusted normalizer must isolate the typed value from mutable caller input; Core does not reflectively clone it.</remarks>
public sealed class NormalizedOperationInput<TRequest>
{
    /// <summary>Captures an owned typed snapshot and at most four distinct immutable fingerprint inputs.</summary>
    public NormalizedOperationInput(TRequest value, IEnumerable<OperationFingerprintInput> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        var copied = new List<OperationFingerprintInput>();
        foreach (OperationFingerprintInput input in fingerprints)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (copied.Count == 4 || copied.Any(previous => previous.Contract == input.Contract))
            {
                throw new ArgumentException("Supply one to four distinct fingerprint comparison versions.", nameof(fingerprints));
            }
            copied.Add(input);
        }
        if (copied.Count == 0)
        {
            throw new ArgumentException("Supply the current fingerprint input.", nameof(fingerprints));
        }
        Value = value;
        Fingerprints = Array.AsReadOnly(copied.ToArray());
    }

    /// <summary>Gets the owned immutable business value passed to the application handler.</summary>
    public TRequest Value
    {
        get;
    }
    /// <summary>Gets copied comparison inputs; unsupported retained versions cannot be guessed.</summary>
    public IReadOnlyList<OperationFingerprintInput> Fingerprints
    {
        get;
    }
}

/// <summary>Operation-specific server normalization, semantic defaults and owned snapshot creation.</summary>
public interface IOperationRequestNormalizer<TRequest>
{
    /// <summary>Creates an isolated business value and all explicitly supported canonical comparisons without I/O.</summary>
    NormalizedOperationInput<TRequest> Normalize(TRequest input, OperationScope scope);
}

/// <summary>Stable bounded success-value encoding; rejections use the portable OperationError fields.</summary>
/// <remarks>Implementations are application-owned thread-safe contracts. False means expected retained-data incompatibility;
/// unexpected programming faults throw and never authorize handler reexecution.</remarks>
public interface IOperationResultCodec<TResult>
{
    /// <summary>Gets the stable current result contract captured at registration.</summary>
    OperationContract CurrentContract
    {
        get;
    }
    /// <summary>Gets one to four supported versions of that same stable contract, including the current version.</summary>
    IReadOnlyList<OperationContract> ReadableContracts
    {
        get;
    }
    /// <summary>Encodes a success value without persisting CLR type identity.</summary>
    byte[] Encode(TResult? value);
    /// <summary>Decodes the exact stored bytes or reports a known incompatible representation.</summary>
    bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out TResult? decoded);
}
