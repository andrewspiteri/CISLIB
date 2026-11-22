using CIS.Jobs;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;

namespace CIS.Examples.CountryData.Coordination;

/// <summary>Trusted hosting adapter over the existing independent native security projection.</summary>
/// <remarks>The supplier performs native workload authentication. Never expose this adapter to serialized
/// principals, arbitrary AuthenticateResult input, or an HTTP caller's ambient context. Keep the root alive through actual job drain.</remarks>
public sealed class SecurityJobScopeFactory : IJobExecutionScopeFactory
{
    private readonly IServiceProvider root;
    private readonly string profile;
    private readonly Func<JobScopeRequest, CancellationToken, ValueTask<AuthenticateResult>> authenticate;

    public SecurityJobScopeFactory(IServiceProvider root, string profile,
        Func<JobScopeRequest, CancellationToken, ValueTask<AuthenticateResult>> authenticate)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentNullException.ThrowIfNull(authenticate);
        this.root = root;
        this.profile = profile;
        this.authenticate = authenticate;
    }

    public async ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool wallet = request.ExecutionScope == CountryMaintenanceJobs.WalletScope && request.JobId == CountryMaintenanceJobs.WalletJob
            && request.Mode == JobExecutionMode.Continuous;
        bool customer = request.ExecutionScope == CountryMaintenanceJobs.CustomerScope && request.JobId == CountryMaintenanceJobs.CustomerJob
            && request.Mode == JobExecutionMode.Scheduled;
        if (!wallet && !customer)
        {
            throw new ArgumentException("Select one explicitly registered maintenance target.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        AuthenticateResult authentication = await authenticate(request, cancellationToken);
        SecurityHostContextScope owned = await SecurityHostContextScope.CreateAsync(root, profile, authentication, cancellationToken);
        return new Scope(owned);
    }

    private sealed class Scope(SecurityHostContextScope owned) : IJobExecutionScope
    {
        public IServiceProvider Services => owned.Services;
        public ValueTask DisposeAsync() => owned.DisposeAsync();
    }
}
