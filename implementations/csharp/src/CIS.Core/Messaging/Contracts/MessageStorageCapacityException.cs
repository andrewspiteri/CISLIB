namespace CIS.Messaging;

/// <summary>Finite configured store admission failed; the original participant must roll back.</summary>
public sealed class MessageStorageCapacityException : InvalidOperationException
{
    /// <summary>Creates fixed safe context without provider, payload or credential details.</summary>
    public MessageStorageCapacityException() : base("The configured retained messaging envelope capacity is exhausted.")
    {
    }
}
