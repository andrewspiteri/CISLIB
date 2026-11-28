using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Operations;
using CIS.Operations.Hosting;

namespace CIS.Management.Service.Configuration;

internal sealed record RuleEngineCounterResetInput(RuleEngineResource Target, ManagementRuleCounterResetCommand Command);

internal sealed record RuleEngineCounterResetResponse(int Status, string? Code, ManagementOperationReference? Operation = null,
    ManagementRuleCounterResetOperationResult? Result = null);

internal static partial class ManagementRuleEngineOperations
{
    internal const string CounterResetName = ManagementRuleEngineWire.CounterResetOperationName;
    internal const string ReadCounters = "management.rule-engine.counters.read";
    internal const string ResetCounters = "management.rule-engine.counters.reset";
    private static readonly OperationContract CounterResetInputContract = new("cis.management.rule-engine.counter.reset.input", 1);
    private static readonly OperationContract CounterResetResultContract = new("cis.management.rule-engine.counter.reset.result", 1);

    internal static OperationDefinition<RuleEngineCounterResetInput, ManagementRuleCounterResetDecision> CounterResetDefinition()
        => new(CounterResetName, "management", CounterResetInputContract, new CounterResetNormalizer(), new CounterResetResultCodec(),
            services => new Resolver(services.GetRequiredService<RuleEngineOperationTarget>(), CounterResetName), new()
            {
                ExecuteCapabilities = [ResetCounters],
                LookupCapabilities = [ReadOperation],
                MaximumCanonicalBytes = 4096,
                MaximumResultBytes = 4096,
                LogCalls = false
            });

    private sealed class CounterResetNormalizer : IOperationRequestNormalizer<RuleEngineCounterResetInput>
    {
        public NormalizedOperationInput<RuleEngineCounterResetInput> Normalize(RuleEngineCounterResetInput input, OperationScope scope)
        {
            if (scope.ResourceType != ApplicationResource || scope.ResourceIdentifier != input.Target.Application.ToString("D")
                || !scope.Scope.StartsWith("management.rule-engine.v1/" + input.Target.Key.ToString("D") + "/", StringComparison.Ordinal))
            {
                throw new ArgumentException("A counter reset requires the exact authorized Rule Engine target.", nameof(input));
            }
            byte[] request = ManagementRuleEngineWire.EncodeRequest(input.Command);
            OperationCanonicalValue canonical = OperationCanonicalValue.Sequence([
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(input.Target.Key.ToString("D"))),
                OperationCanonicalValue.Bytes(SHA256.HashData(request))
            ]);
            return new(input, [new(CounterResetInputContract, canonical)]);
        }
    }

    private sealed class CounterResetResultCodec : IOperationResultCodec<ManagementRuleCounterResetDecision>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public OperationContract CurrentContract => CounterResetResultContract;
        public IReadOnlyList<OperationContract> ReadableContracts { get; } = Array.AsReadOnly(new[] { CounterResetResultContract });

        public byte[] Encode(ManagementRuleCounterResetDecision? value)
            => JsonSerializer.SerializeToUtf8Bytes(value ?? throw new ArgumentNullException(nameof(value)), Json);

        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out ManagementRuleCounterResetDecision? decoded)
        {
            decoded = null;
            if (contract != CounterResetResultContract || value.Length is < 1 or > 4096)
            {
                return false;
            }
            try
            {
                ManagementRuleCounterResetDecision? candidate = JsonSerializer.Deserialize<ManagementRuleCounterResetDecision>(value.Span, Json);
                if (candidate is { Selection: "one" or "selected" or "all" }
                    && ManagementWire.IsTimestamp(candidate.ResetAtUtc)
                    && ManagementWire.IsOrderedInteger(candidate.AppliedCount, allowZero: true)
                    && ManagementWire.IsOrderedInteger(candidate.RejectedCount, allowZero: true))
                {
                    decoded = candidate;
                }
            }
            catch (JsonException)
            {
                return false;
            }
            return decoded is not null;
        }
    }
}

internal sealed record RuleEngineCounterResetAuditDetails(string ResourceId, string RulesetId, string Selection,
    IReadOnlyList<string> RuleIds, string Reason, string Code, ManagementRuleCounterResetDecision? Decision);

internal static class RuleEngineCounterResetAudit
{
    internal static readonly AuditEventDefinition<RuleEngineCounterResetAuditDetails> Definition = new(
        "cis.management.rule-engine.counter.reset", 1, AuditCategory.Administration, new DetailsCodec(),
        requireTarget: true, requireOperationReference: true);

    private sealed class DetailsCodec : IAuditDetailsCodec<RuleEngineCounterResetAuditDetails>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public void Validate(RuleEngineCounterResetAuditDetails details)
        {
            if (!ManagementWire.IsUuid(details.ResourceId) || !ManagementRuleEngineWire.IsRuleId(details.RulesetId))
            {
                throw new ArgumentException("Use the exact resolved counter reset resource.", nameof(details));
            }
            _ = ManagementRuleEngineWire.EncodeRequest(new ManagementRuleCounterResetCommand(details.Selection, details.RuleIds, details.Reason));
        }

        public void Write(Utf8JsonWriter writer, RuleEngineCounterResetAuditDetails details)
            => JsonSerializer.Serialize(writer, details, Json);
    }
}
