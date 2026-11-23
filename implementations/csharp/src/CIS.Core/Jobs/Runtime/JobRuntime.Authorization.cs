using CIS.Coordination;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Jobs;

public sealed partial class JobRuntime
{
    internal async Task<JobAuthorizedScopeResult> OpenAuthorizedScopeAsync(JobDutyReservation duty,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(duty);
        cancellationToken.ThrowIfCancellationRequested();
        if (duty.IsDraining)
        {
            return new(JobAdmissionOutcome.Deferred);
        }

        JobAuthorizedScopeResult result = await OpenAuthorizedScopeCoreAsync(duty.Target, duty.Binding, cancellationToken).ConfigureAwait(false);
        if (duty.IsDraining && result.Scope is { } late)
        {
            await late.DisposeAsync().ConfigureAwait(false);
            return new(JobAdmissionOutcome.Deferred);
        }

        return result;
    }

    internal Task<JobAuthorizedScopeResult> OpenAuthorizedScopeAsync(JobScopeRequest target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        JobHandlerBinding? binding = registered.GetValueOrDefault((target.ExecutionScope, target.JobId));
        if (binding is null || !binding.Definition.SupportedModes.Contains(target.Mode))
        {
            return Task.FromResult(new JobAuthorizedScopeResult(JobAdmissionOutcome.Unsupported));
        }

        return OpenAuthorizedScopeCoreAsync(target, binding, cancellationToken);
    }

    private async Task<JobAuthorizedScopeResult> OpenAuthorizedScopeCoreAsync(JobScopeRequest requested, JobHandlerBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closing)
            {
                return new(JobAdmissionOutcome.Deferred);
            }
        }

        IJobExecutionScope owned = await scopes.CreateAsync(requested, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The trusted job scope factory returned no owned scope.");
        JobAuthorizedScope? accepted = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IServiceProvider services = owned.Services;
            var request = new CoordinationRequest(requested.ExecutionScope, requested.JobId, requested.ResourceId);
            CoordinationTarget? target = await services.GetRequiredService<ICoordinationTargetResolver>()
                .ResolveAsync(request, application, cancellationToken).ConfigureAwait(false);
            CoordinationKey expected = new(application.Environment, requested.ExecutionScope, requested.JobId, requested.ResourceId);
            if (target is null || target.Key != expected || target.Resource.Environment != application.Environment)
            {
                return new(JobAdmissionOutcome.Unsupported);
            }

            AuthorizationDecision decision = await services.GetRequiredService<ICapabilityAuthorizer>()
                .AuthorizeAsync(binding.Definition.ExecuteCapabilities, target.Resource, cancellationToken).ConfigureAwait(false);
            if (!decision.IsAllowed)
            {
                return new(JobAdmissionOutcome.Unauthorized);
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (closing)
                {
                    return new(JobAdmissionOutcome.Deferred);
                }
            }

            accepted = new(owned, target, RecordCleanupDiagnostic);
            return new(JobAdmissionOutcome.Accepted, accepted);
        }
        finally
        {
            if (accepted is null)
            {
                try
                {
                    await owned.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { RecordCleanupDiagnostic("jobs.scope_cleanup_failed"); }
            }
        }
    }

    internal async Task<ManagedCoordinationResult> AcquireAsync(JobDutyReservation duty, CancellationToken cancellationToken)
    {
        JobAuthorizedScopeResult authorization = await OpenAuthorizedScopeAsync(duty, cancellationToken).ConfigureAwait(false);
        if (authorization.Scope is not { } scope)
        {
            return new(authorization.Outcome == JobAdmissionOutcome.Unauthorized
                ? CoordinationAcquireOutcome.Unauthorized : CoordinationAcquireOutcome.InvalidRequest,
                reasonCode: "jobs.scope_refused");
        }

        await using (scope.ConfigureAwait(false))
        {
            return await AcquireAsync(duty, scope, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<ManagedCoordinationResult> AcquireAsync(JobDutyReservation duty, JobAuthorizedScope authorizedScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(duty);
        ArgumentNullException.ThrowIfNull(authorizedScope);
        CoordinationKey expected = new(application.Environment, duty.Target.ExecutionScope, duty.Target.JobId, duty.Target.ResourceId);
        if (authorizedScope.Target.Key != expected || duty.IsDraining)
        {
            return new(CoordinationAcquireOutcome.InvalidRequest, reasonCode: "jobs.authorized_scope_mismatch");
        }
        if (!CanAdmitManagedWork())
        {
            return new(CoordinationAcquireOutcome.Unavailable, reasonCode: ManagedAdmissionClosed);
        }

        duty.SetState(JobLocalState.Acquiring);
        ManagedCoordinationResult result = await authorizedScope.Services.GetRequiredService<ICoordinationService>().AcquireAsync(
                new(duty.Target.ExecutionScope, duty.Target.JobId, duty.Target.ResourceId), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The scoped coordination service returned no acquisition result.");
        if (result.Lease is { } lease && !AttachLease(duty, lease))
        {
            CoordinationGrant grant = lease.Grant;
            if (grant.Key == new CoordinationKey(application.Environment, duty.Target.ExecutionScope, duty.Target.JobId, duty.Target.ResourceId)
                && grant.Acquisition.OwnerApplication == application.Application && grant.Acquisition.InstanceId == application.InstanceId)
            {
                await ReleaseOwnedLeaseAsync(lease).ConfigureAwait(false);
            }

            return new(CoordinationAcquireOutcome.InvalidRequest, reasonCode: "jobs.grant_mismatch");
        }

        if (result.Outcome == CoordinationAcquireOutcome.Busy)
        {
            ObserveStandby(duty, result.ObservedOwner);
        }

        return result;
    }

    internal Task ReleaseLeaseAsync(JobDutyReservation duty)
        => duty.Lease is { } lease ? ReleaseOwnedLeaseAsync(lease) : Task.CompletedTask;

    private async Task ReleaseOwnedLeaseAsync(ICoordinationLease lease)
    {
        try
        {
            CoordinationMutationResult result = await lease.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (result.Outcome is CoordinationMutationOutcome.Indeterminate or CoordinationMutationOutcome.Unavailable)
            {
                RecordCleanupDiagnostic("jobs.release_unconfirmed");
            }
        }
        catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { RecordCleanupDiagnostic("jobs.release_failed"); }
        finally
        {
            try
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { RecordCleanupDiagnostic("jobs.lease_cleanup_failed"); }
        }
    }
}
