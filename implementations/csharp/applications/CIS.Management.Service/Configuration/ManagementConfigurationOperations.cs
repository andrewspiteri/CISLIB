using System.Text;
using System.Security.Cryptography;
using CIS.Management.Service.Http;
using CIS.Management.Wire;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;

namespace CIS.Management.Service.Configuration;

public static class ManagementConfigurationCapabilities
{
    public const string CatalogRead = "management.configuration.catalog.read";
    public const string CatalogApply = "management.configuration.catalog.apply";
    public const string DefinitionsRead = "management.configuration.definitions.read";
    public const string ContentRead = "management.configuration.content.read";
    public const string Validate = "management.configuration.validate";
    public const string Publish = "management.configuration.sources.publish";
    public const string OperationsRead = "management.configuration.operations.read";
    public const string Preview = "management.configuration.preview";
    public const string ReleaseCreate = "management.configuration.releases.create";
    public const string AssignmentWrite = "management.configuration.assignments.write";
    public const string AdoptionRead = "management.configuration.adoption.read";
    public const string CatalogResource = "management.configuration.catalog";
    public const string SourceResource = "management.configuration.source";
    public const string BindingResource = "management.configuration.binding";
    public const string ReleaseResource = "management.configuration.release";
    public static IReadOnlyList<string> All
    {
        get;
    } = Array.AsReadOnly(new[]
    {
        CatalogRead, CatalogApply, DefinitionsRead, ContentRead, Validate, Publish, OperationsRead, Preview, ReleaseCreate, AssignmentWrite, AdoptionRead
    });
}

internal sealed record ManagementCatalogInput(string CatalogId, string IfMatch, ManagementConfigurationCatalogCommand Command);
internal sealed record ManagementPublicationInput(ManagementConfigurationDefinition Source, string IfMatch,
    ManagementConfigurationPublicationCommand Command);

internal static partial class ManagementConfigurationOperations
{
    internal const string CatalogName = "management.configuration.catalog.apply";
    internal const string PublicationName = "management.configuration.source.publish";
    internal static readonly OperationContract ResultContract = new("cis.management.configuration.decision", 1);

    internal static OperationDefinition<ManagementCatalogInput, ManagementConfigurationDecision> Catalog()
        => new(CatalogName, "management", new("cis.management.configuration.catalog.input", 1), new CatalogNormalizer(),
            new ResultCodec(), services => new ScopeResolver(services.GetRequiredService<ManagementConfigurationTarget>(),
                CatalogName, ManagementConfigurationCapabilities.CatalogResource), Options(ManagementConfigurationCapabilities.CatalogApply));

    internal static OperationDefinition<ManagementPublicationInput, ManagementConfigurationDecision> Publication()
        => new(PublicationName, "management", new("cis.management.configuration.publication.input", 1), new PublicationNormalizer(),
            new ResultCodec(), services => new ScopeResolver(services.GetRequiredService<ManagementConfigurationTarget>(),
                PublicationName, ManagementConfigurationCapabilities.SourceResource), Options(ManagementConfigurationCapabilities.Publish));

    private static OperationDefinitionOptions Options(string capability) => new()
    {
        ExecuteCapabilities = [capability],
        LookupCapabilities = [ManagementConfigurationCapabilities.OperationsRead],
        MaximumCanonicalBytes = 16384,
        MaximumResultBytes = 65536,
        LogCalls = false
    };

    internal static string Scope(string family, CallerContext caller, string resourceId)
        => "management.configuration.v1/" + family + "/" + resourceId + "/" + ManagementActorScope.Create(caller, resourceId);

    private sealed class ScopeResolver(ManagementConfigurationTarget target, string name, string resourceType) : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = target.ResourceId ?? throw new InvalidOperationException("Resolve the actual stored resource before opening its operation owner.");
            if (caller is null || target.ResourceType != resourceType || operationName != name
                || requestedScope != Scope(name, caller, id))
            {
                throw new ArgumentException("The original scope must match the actual resource and native actor.", nameof(requestedScope));
            }
            return ValueTask.FromResult(new OperationScope(requestedScope, resourceType, id));
        }
    }

    private sealed class CatalogNormalizer : IOperationRequestNormalizer<ManagementCatalogInput>
    {
        public NormalizedOperationInput<ManagementCatalogInput> Normalize(ManagementCatalogInput input, OperationScope scope)
        {
            Require(input.CatalogId, input.IfMatch, scope, ManagementConfigurationCapabilities.CatalogResource);
            byte[] command = ManagementConfigurationWire.EncodeRequest(input.Command);
            var copy = input with
            {
                Command = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCatalogCommand>(command)!
            };
            return new(copy, [new(new("cis.management.configuration.catalog.input", 1), Canonical(input.CatalogId, input.IfMatch, command))]);
        }
    }

    private sealed class PublicationNormalizer : IOperationRequestNormalizer<ManagementPublicationInput>
    {
        public NormalizedOperationInput<ManagementPublicationInput> Normalize(ManagementPublicationInput input, OperationScope scope)
        {
            Require(input.Source.SourceId, input.IfMatch, scope, ManagementConfigurationCapabilities.SourceResource);
            ArgumentNullException.ThrowIfNull(input.Command);
            if (input.Command.CommandVersion != 1 || !ManagementWire.IsUtf8Text(input.Command.Reason, 1024))
            {
                throw new ArgumentException("Supply one complete supported bounded publication command.", nameof(input));
            }
            _ = ManagementConfigurationWire.ComputePayloadSha256(input.Command.PayloadJson);
            ManagementConfigurationDefinition source = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDefinition>(
                ManagementConfigurationWire.EncodeResponse(input.Source))!;
            var copy = new ManagementPublicationInput(source, input.IfMatch,
                new(input.Command.CommandVersion, input.Command.PayloadJson, input.Command.Reason));
            OperationCanonicalValue canonical = OperationCanonicalValue.Sequence([
                Canonical(source.SourceId, input.IfMatch, SHA256.HashData(Encoding.UTF8.GetBytes(copy.Command.PayloadJson))),
                OperationCanonicalValue.Integer(copy.Command.CommandVersion),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(copy.Command.Reason)),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(source.ConfigurationId)),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(source.SchemaVersion)),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(source.Environment)),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(source.Kind)),
                source.ApplicationId is null ? OperationCanonicalValue.Null : OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(source.ApplicationId))
            ]);
            return new(copy, [new(new("cis.management.configuration.publication.input", 1), canonical)]);
        }
    }

    private static OperationCanonicalValue Canonical(string resource, string precondition, byte[] command)
        => OperationCanonicalValue.Sequence([OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(resource)),
            OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(precondition)), OperationCanonicalValue.Bytes(command)]);

    private static void Require(string id, string etag, OperationScope scope, string type)
    {
        if (!ManagementWire.IsUuid(id) || !ManagementWire.IsStrongETag(etag) || scope.ResourceType != type || scope.ResourceIdentifier != id)
        {
            throw new ArgumentException("Supply the original actual resource and strong precondition.");
        }
    }

    private sealed class ResultCodec : IOperationResultCodec<ManagementConfigurationDecision>
    {
        public OperationContract CurrentContract => ResultContract;
        public IReadOnlyList<OperationContract> ReadableContracts { get; } = Array.AsReadOnly(new[] { ResultContract });
        public byte[] Encode(ManagementConfigurationDecision? value)
            => ManagementConfigurationWire.EncodeResponse(value ?? throw new ArgumentNullException(nameof(value)));
        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out ManagementConfigurationDecision? decoded)
        {
            decoded = contract == ResultContract ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(value) : null;
            return decoded is not null;
        }
    }
}

internal sealed class ManagementConfigurationTarget
{
    internal string? ResourceType
    {
        get; private set;
    }
    internal string? ResourceId
    {
        get; private set;
    }

    internal void Bind(string type, string id)
    {
        if (ResourceId is not null && (ResourceId != id || ResourceType != type))
        {
            throw new InvalidOperationException("One request has one actual configuration operation target.");
        }
        ResourceType = type;
        ResourceId = id;
    }
}
