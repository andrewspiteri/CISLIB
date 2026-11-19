using System.Security.Claims;

namespace CIS.Security.Hosting;

// One explicit DI scope, never ambient execution-context authority.
internal sealed class SecurityCallerState : ICallerContextAccessor
{
    private readonly object gate = new();
    private Authority? current;

    public CallerContext? Current => Volatile.Read(ref current)?.Caller;
    internal Authority? CurrentAuthority => Volatile.Read(ref current);

    internal IDisposable Establish(Authority authority)
    {
        lock (gate)
        {
            Authority? previous = current;
            Volatile.Write(ref current, authority);
            return new Restoration(this, authority, previous);
        }
    }

    private void Restore(Authority expected, Authority? previous)
    {
        lock (gate)
        {
            if (!ReferenceEquals(current, expected))
            {
                throw new InvalidOperationException("Trusted caller scopes must settle in their established nesting order.");
            }
            Volatile.Write(ref current, previous);
        }
    }

    internal sealed class Authority
    {
        internal Authority(SecurityProfile? profile, CallerContext? caller, ClaimsPrincipal principal,
            IReadOnlyList<string>? providerRoles = null, IReadOnlyList<string>? mappedRoles = null,
            SecurityFailure? failure = null)
        {
            Profile = profile;
            Caller = caller;
            Principal = principal;
            ProviderRoles = providerRoles ?? [];
            MappedRoles = mappedRoles ?? [];
            Failure = failure;
        }

        internal SecurityProfile? Profile
        {
            get;
        }
        internal CallerContext? Caller
        {
            get;
        }
        internal ClaimsPrincipal Principal
        {
            get;
        }
        internal IReadOnlyList<string> ProviderRoles
        {
            get;
        }
        internal IReadOnlyList<string> MappedRoles
        {
            get;
        }
        internal SecurityFailure? Failure
        {
            get;
        }
    }

    private sealed class Restoration(SecurityCallerState owner, Authority expected, Authority? previous) : IDisposable
    {
        private SecurityCallerState? owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Restore(expected, previous);
        }
    }
}
