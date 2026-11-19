namespace CIS.Security;

/// <summary>Protocol-neutral source control; credentials and acquisition representations remain adapter-owned.</summary>
public interface ISecurityCredentialControl
{
    /// <summary>Gets the explicitly selected finite profile alias.</summary>
    string Profile
    {
        get;
    }
    /// <summary>Requests a coalesced source recheck under the caller's existing operation ownership.</summary>
    /// <remarks>This does not promise to mint a new token, renew a certificate, bypass an SDK cache or replay an operation.</remarks>
    ValueTask RequestSourceRecheckAsync(CancellationToken cancellationToken = default);
}
