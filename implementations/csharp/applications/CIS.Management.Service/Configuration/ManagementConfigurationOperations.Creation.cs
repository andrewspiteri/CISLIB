using CIS.Management.Wire;
using CIS.Operations;
using CIS.Operations.Hosting;

namespace CIS.Management.Service.Configuration;

internal sealed record ManagementCreationInput(ManagementApplication Application, string IfMatch, ManagementConfigurationCreationCommand Command);

internal static partial class ManagementConfigurationOperations
{
    internal const string CreationName = "management.configuration.create";

    internal static OperationDefinition<ManagementCreationInput, ManagementConfigurationDecision> Creation()
        => new(CreationName, "management", new("cis.management.configuration.creation.input", 1), new CreationNormalizer(),
            new ResultCodec(), services => Resolver(services, CreationName, "management.application"), Options(ManagementConfigurationCapabilities.Create));

    private sealed class CreationNormalizer : IOperationRequestNormalizer<ManagementCreationInput>
    {
        public NormalizedOperationInput<ManagementCreationInput> Normalize(ManagementCreationInput input, OperationScope scope)
        {
            Require(input.Application.ApplicationId, input.IfMatch, scope, "management.application");
            byte[] bytes = ManagementConfigurationWire.EncodeRequest(input.Command);
            var copy = input with
            {
                Command = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCreationCommand>(bytes)!
            };
            return new(copy, [new(new("cis.management.configuration.creation.input", 1), Canonical(input.Application.ApplicationId, input.IfMatch, bytes))]);
        }
    }
}
