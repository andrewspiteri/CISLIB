using System.Text;
using CIS.Management.Wire;
using CIS.Operations;
using CIS.Operations.Hosting;

namespace CIS.Management.Service.Configuration;

internal sealed record ManagementReleaseInput(ManagementConfigurationBindingResource Binding, string IfMatch, ManagementConfigurationReleaseCommand Command);
internal sealed record ManagementAssignmentInput(ManagementConfigurationBindingResource Binding, string IfMatch, ManagementConfigurationAssignmentCommand Command);

internal static partial class ManagementConfigurationOperations
{
    internal const string ReleaseName = "management.configuration.release.create";
    internal const string AssignmentName = "management.configuration.assignment.replace";

    internal static OperationDefinition<ManagementReleaseInput, ManagementConfigurationDecision> Release()
        => new(ReleaseName, "management", new("cis.management.configuration.release.input", 1), new ReleaseNormalizer(),
            new ResultCodec(), services => new ScopeResolver(services.GetRequiredService<ManagementConfigurationTarget>(),
                ReleaseName, ManagementConfigurationCapabilities.BindingResource), Options(ManagementConfigurationCapabilities.ReleaseCreate));

    internal static OperationDefinition<ManagementAssignmentInput, ManagementConfigurationDecision> Assignment()
        => new(AssignmentName, "management", new("cis.management.configuration.assignment.input", 1), new AssignmentNormalizer(),
            new ResultCodec(), services => new ScopeResolver(services.GetRequiredService<ManagementConfigurationTarget>(),
                AssignmentName, ManagementConfigurationCapabilities.BindingResource), Options(ManagementConfigurationCapabilities.AssignmentWrite));

    private sealed class ReleaseNormalizer : IOperationRequestNormalizer<ManagementReleaseInput>
    {
        public NormalizedOperationInput<ManagementReleaseInput> Normalize(ManagementReleaseInput input, OperationScope scope)
        {
            Require(input.Binding.BindingId, input.IfMatch, scope, ManagementConfigurationCapabilities.BindingResource);
            byte[] command = ManagementConfigurationWire.EncodeRequest(input.Command);
            var copy = new ManagementReleaseInput(BindingCopy(input.Binding), input.IfMatch,
                ManagementConfigurationWire.DecodeRequest<ManagementConfigurationReleaseCommand>(command)!);
            return new(copy, [new(new("cis.management.configuration.release.input", 1), BindingCanonical(copy.Binding, copy.IfMatch, command))]);
        }
    }

    private sealed class AssignmentNormalizer : IOperationRequestNormalizer<ManagementAssignmentInput>
    {
        public NormalizedOperationInput<ManagementAssignmentInput> Normalize(ManagementAssignmentInput input, OperationScope scope)
        {
            Require(input.Binding.BindingId, input.IfMatch, scope, ManagementConfigurationCapabilities.BindingResource);
            byte[] original = ManagementConfigurationWire.EncodeRequest(input.Command);
            ManagementConfigurationAssignmentCommand owned = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationAssignmentCommand>(original)!;
            owned = owned with
            {
                Overrides = Array.AsReadOnly(owned.Overrides.OrderBy(item => item.CohortId, StringComparer.Ordinal).ToArray())
            };
            var copy = new ManagementAssignmentInput(BindingCopy(input.Binding), input.IfMatch, owned);
            return new(copy, [new(new("cis.management.configuration.assignment.input", 1),
                BindingCanonical(copy.Binding, copy.IfMatch, ManagementConfigurationWire.EncodeRequest(copy.Command)))]);
        }
    }

    private static ManagementConfigurationBindingResource BindingCopy(ManagementConfigurationBindingResource binding)
    {
        var descriptor = new ManagementConfigurationDefinition(binding.ConfigurationId, binding.SchemaVersion,
            binding.ApplicationSourceId ?? binding.BindingId, binding.Environment, "application", binding.Shape, binding.DefaultsJson,
            ApplicationId: binding.ApplicationId, BindingId: binding.BindingId, HostApplicationName: binding.HostApplicationName);
        ManagementConfigurationDefinition copy = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDefinition>(
            ManagementConfigurationWire.EncodeResponse(descriptor))!;
        return binding with
        {
            Shape = copy.Shape,
            DefaultsJson = copy.DefaultsJson!
        };
    }

    private static OperationCanonicalValue BindingCanonical(ManagementConfigurationBindingResource binding, string etag, byte[] command)
        => OperationCanonicalValue.Sequence([Canonical(binding.BindingId, etag, command),
            OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(binding.ApplicationId)),
            OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(binding.Environment)),
            OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(binding.ConfigurationId)),
            OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(binding.SchemaVersion))]);
}
