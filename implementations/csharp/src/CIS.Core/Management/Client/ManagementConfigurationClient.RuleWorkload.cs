using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Management.Wire;

namespace CIS.Management;

internal interface IManagementRuleWorkloadClient
{
    Task<ManagementConfigurationCallResult<ManagementRuleWorkloadVersion>> GetRegistrationRuleVersionAsync(
        ManagementConfigurationSnapshotTarget expected, string rulesetId, string version, CancellationToken cancellationToken);
}

public sealed partial class ManagementConfigurationClient
{
    async Task<ManagementConfigurationCallResult<ManagementRuleWorkloadVersion>> IManagementRuleWorkloadClient.GetRegistrationRuleVersionAsync(
        ManagementConfigurationSnapshotTarget expected, string rulesetId, string version, CancellationToken cancellationToken)
    {
        long entered = clock.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTarget(expected);
        if (!ManagementRuleEngineWire.IsRuleId(rulesetId))
        {
            throw new ArgumentException("Supply the exact local ruleset identity.", nameof(rulesetId));
        }

        string path = "registrations/" + Uuid(expected.RegistrationId) + "/configuration/" + Identifier(expected.ConfigurationId)
            + "/rule-versions/" + Uuid(version);
        var mapper = new ConfigurationMapper(settings.WireLimits, workloadVersion: new(expected, rulesetId, version));
        RestResult<ManagementRuleWorkloadVersion> exchange = await SendAsync<ManagementRuleWorkloadVersion>(
            entered, "GET", path, expected.SchemaVersion, null, null, null, null, mapper, null, cancellationToken)
            .ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper));
    }

    private sealed record ExpectedRuleWorkloadVersion(ManagementConfigurationSnapshotTarget Target, string RulesetId, string Version)
    {
        internal void Check(object? value)
        {
            if (value is not ManagementRuleWorkloadVersion candidate || candidate.Target != Target
                || candidate.RulesetId != RulesetId || candidate.Version != Version)
            {
                throw new RestDecodingException();
            }
        }
    }
}
