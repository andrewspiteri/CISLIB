using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CIS.Security;

namespace CIS.Management.Service.Http;

/// <summary>Exact, versioned actor/resource identity, deliberately excluding mutable grants and credentials.</summary>
public static class ManagementActorScope
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Create(CallerContext caller, string? applicationId = null)
    {
        ArgumentNullException.ThrowIfNull(caller);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (string? field in new[] { "cis.management.actor.v1", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, applicationId, caller.ActorKind.ToString(), caller.Subject.Issuer,
            caller.Subject.Identifier, caller.Application, caller.Environment, caller.HumanSubject?.Issuer,
            caller.HumanSubject?.Identifier, caller.VerifiedDelegation })
        {
            byte[]? encoded = field is null ? null : StrictUtf8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, encoded?.Length ?? -1);
            digest.AppendData(length);
            if (encoded is not null)
            {
                digest.AppendData(encoded);
            }
        }
        return "management.actor.v1." + Convert.ToHexStringLower(digest.GetHashAndReset());
    }
}
