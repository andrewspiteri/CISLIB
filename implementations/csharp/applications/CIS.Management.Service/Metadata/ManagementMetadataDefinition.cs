using System.Text;
using CIS.Management.Service.Http;
using CIS.Management.Wire;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;

namespace CIS.Management.Service.Metadata;

internal sealed record ManagementMetadataInput(string ApplicationId, string IfMatch, ManagementMetadataCommand Command);

internal static class ManagementMetadataDefinition
{
    internal const string Name = "management.metadata.replace";
    internal static readonly OperationContract FingerprintContract = new("cis.management.metadata.input", 1);
    internal static readonly OperationContract ResultContract = new("cis.management.metadata.result", 1);

    internal static OperationDefinition<ManagementMetadataInput, ManagementMetadataDecision> Create()
        => new(Name, "management", FingerprintContract, new Normalizer(), new ResultCodec(),
            root => new ScopeResolver(root.GetRequiredService<ManagementMetadataTarget>()), new()
            {
                ExecuteCapabilities = ["management.applications.metadata.write"],
                LookupCapabilities = ["management.operations.read"],
                MaximumCanonicalBytes = 32768,
                MaximumResultBytes = 65536,
                LogCalls = false
            });

    internal static string Scope(CallerContext caller, string applicationId)
        => "management.metadata.v1/" + applicationId + "/" + ManagementActorScope.Create(caller, applicationId);

    private sealed class ScopeResolver(ManagementMetadataTarget target) : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagementApplication application = target.Application
                ?? throw new InvalidOperationException("Bind the actual stored management resource before opening the metadata owner.");
            if (caller is null || operationName != Name || requestedScope != Scope(caller, application.ApplicationId))
            {
                throw new ArgumentException("The operation scope must match the current validated actor and actual resource.", nameof(requestedScope));
            }

            return ValueTask.FromResult(new OperationScope(requestedScope, "management.application", application.ApplicationId));
        }
    }

    private sealed class Normalizer : IOperationRequestNormalizer<ManagementMetadataInput>
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);

        public NormalizedOperationInput<ManagementMetadataInput> Normalize(ManagementMetadataInput input, OperationScope scope)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!ManagementWire.IsUuid(input.ApplicationId) || scope.ResourceType != "management.application"
                || scope.ResourceIdentifier != input.ApplicationId || !ManagementWire.IsStrongETag(input.IfMatch))
            {
                throw new ArgumentException("Resolve the actual resource and original strong metadata precondition.", nameof(input));
            }
            _ = ManagementWire.EncodeRequest(input.Command);
            var copy = new ManagementMetadataInput(input.ApplicationId, input.IfMatch,
                new(input.Command.CommandVersion, input.Command.DisplayName, input.Command.Description));
            OperationCanonicalValue canonical = OperationCanonicalValue.Sequence([
                OperationCanonicalValue.Integer(copy.Command.CommandVersion),
                OperationCanonicalValue.Bytes(Utf8.GetBytes(copy.ApplicationId)),
                OperationCanonicalValue.Bytes(Utf8.GetBytes(copy.IfMatch)),
                OperationCanonicalValue.Bytes(Utf8.GetBytes(copy.Command.DisplayName)),
                copy.Command.Description is null ? OperationCanonicalValue.Null
                    : OperationCanonicalValue.Bytes(Utf8.GetBytes(copy.Command.Description))
            ]);
            return new(copy, [new(FingerprintContract, canonical)]);
        }
    }

    private sealed class ResultCodec : IOperationResultCodec<ManagementMetadataDecision>
    {
        public OperationContract CurrentContract => ResultContract;
        public IReadOnlyList<OperationContract> ReadableContracts { get; } = Array.AsReadOnly(new[] { ResultContract });

        public byte[] Encode(ManagementMetadataDecision? value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return ManagementWire.EncodeResponse(value);
        }

        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out ManagementMetadataDecision? decoded)
        {
            decoded = contract == ResultContract ? ManagementWire.DecodeResponse<ManagementMetadataDecision>(value) : null;
            return decoded is not null;
        }
    }
}

// Request-scoped server data, populated only from the endpoint's actual stored resource.
// It avoids opening another database lease while the original operation owner is held.
internal sealed class ManagementMetadataTarget
{
    internal ManagementApplication? Application
    {
        get; private set;
    }

    internal void Bind(ManagementApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (Application is not null && Application.ApplicationId != application.ApplicationId)
        {
            throw new InvalidOperationException("One metadata request has one resolved application resource.");
        }
        Application = application;
    }
}
