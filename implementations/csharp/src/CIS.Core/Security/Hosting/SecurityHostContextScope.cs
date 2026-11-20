using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Security.Hosting;

/// <summary>An explicit independent host-workload scope; it never inherits ambient HTTP authority.</summary>
/// <remarks>The host integration supplies an already validated native authentication result. CallerContext data alone is not accepted.</remarks>
public sealed class SecurityHostContextScope : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly AsyncServiceScope scope;
    private readonly IDisposable restoration;
    private readonly SecurityCallerState.Authority authority;
    private Task? disposal;

    private SecurityHostContextScope(AsyncServiceScope scope, IDisposable restoration, SecurityCallerState.Authority authority)
    {
        this.scope = scope;
        this.restoration = restoration;
        this.authority = authority;
    }

    /// <summary>Gets services in the new independent scope. Join all owned background work before disposing it.</summary>
    public IServiceProvider Services
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposal is not null, this);
                return scope.ServiceProvider;
            }
        }
    }
    /// <summary>Gets the established host caller, or null after an expected authentication/mapping failure.</summary>
    public CallerContext? Caller => authority.Caller;
    /// <summary>Gets a known safe establishment failure without inventing caller authority.</summary>
    public SecurityFailure? Failure => authority.Failure;

    /// <summary>Creates a separate DI scope and projects explicitly supplied, already validated native workload evidence.</summary>
    /// <remarks>This is a trusted hosting integration API, never an endpoint accepting serialized credentials or caller contexts.</remarks>
    public static async ValueTask<SecurityHostContextScope> CreateAsync(IServiceProvider root, string profile,
        AuthenticateResult authentication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(authentication);
        cancellationToken.ThrowIfCancellationRequested();
        AsyncServiceScope owned = root.CreateAsyncScope();
        try
        {
            SecurityPolicySnapshot policy = owned.ServiceProvider.GetRequiredService<SecurityPolicySnapshot>();
            SecurityProfile selected = policy.GetProfile(profile);
            if ((selected.Roles & SecurityProfileRoles.Inbound) == 0)
            {
                throw new ArgumentException("Explicit host projection uses a configured native receiving/mapping integration.", nameof(profile));
            }
            var context = new DefaultHttpContext
            {
                RequestServices = owned.ServiceProvider,
                RequestAborted = cancellationToken
            };
            SecurityCallerState.Authority authority = await SecurityProjection.ProjectAsync(context, policy, selected,
                authentication, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            IDisposable restoration = owned.ServiceProvider.GetRequiredService<SecurityCallerState>().Establish(authority);
            return new(owned, restoration, authority);
        }
        catch
        {
            await owned.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Restores caller absence and joins actual asynchronous service-scope disposal once.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task pending;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            pending = disposal;
        }
        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }
        return new(pending);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                restoration.Dispose();
            }
            finally
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }
}
