using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CIS.Operations;

/// <summary>One operation-selected canonical input under a stable fingerprint contract.</summary>
public sealed class OperationFingerprintInput
{
    /// <summary>Captures the immutable contract and explicitly constructed canonical root.</summary>
    public OperationFingerprintInput(OperationContract contract, OperationCanonicalValue value)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(value);
        Contract = contract;
        Value = value;
    }

    /// <summary>Gets the fingerprint contract.</summary>
    public OperationContract Contract
    {
        get;
    }
    /// <summary>Gets the immutable ordered canonical root.</summary>
    public OperationCanonicalValue Value
    {
        get;
    }
}

/// <summary>The portable request frame v1 and server-side SHA-256 calculation.</summary>
public static class OperationCanonicalFrame
{
    /// <summary>Gets the initial whole-frame input limit in bytes.</summary>
    public const int DefaultMaximumBytes = 65_536;
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("CIS.OPERATIONS.REQUEST\0");

    /// <summary>Encodes exact v1 bytes, rejecting the complete preimage before it exceeds its configured limit.</summary>
    public static byte[] Encode(OperationFingerprintInput input, int maximumBytes = DefaultMaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(input);
        OperationText.ByteLimit(maximumBytes, nameof(maximumBytes));
        using var writer = new CanonicalFrameWriter(maximumBytes);
        writer.Bytes(Domain);
        writer.UInt16(1);
        byte[] identifier = Encoding.ASCII.GetBytes(input.Contract.Identifier);
        writer.UInt32((uint)identifier.Length);
        writer.Bytes(identifier);
        writer.UInt32((uint)input.Contract.Version);
        input.Value.Write(writer);
        return writer.ToArray();
    }

    /// <summary>Hashes the exact bounded canonical frame; callers cannot provide an authoritative digest to Execute.</summary>
    public static OperationFingerprint Fingerprint(OperationFingerprintInput input,
        int maximumBytes = DefaultMaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.Contract, SHA256.HashData(Encode(input, maximumBytes)));
    }
}

internal sealed class CanonicalFrameWriter(int maximumBytes) : IDisposable
{
    private readonly MemoryStream stream = new();

    internal void Byte(byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        Bytes(bytes);
    }

    internal void UInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        Bytes(bytes);
    }

    internal void UInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        Bytes(bytes);
    }

    internal void Bytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > maximumBytes - stream.Length)
        {
            throw new ArgumentException("The complete canonical fingerprint frame exceeds its configured byte limit.");
        }
        stream.Write(bytes);
    }

    internal byte[] ToArray() => stream.ToArray();
    public void Dispose() => stream.Dispose();
}
