using System.Globalization;
using System.Text.Json;
using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

public sealed partial class ManagementRuleEngineClient : IManagementRuleEngineCounterClient
{
    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleCounterPage>> ListCountersAsync(string applicationId, string rulesetId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleCounterPage>(new(applicationId, rulesetId), "/counters",
            page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleCounterResetOperationResult>> ResetCountersAsync(string applicationId,
        string rulesetId, string operationKey, ManagementRuleCounterResetCommand command, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ManagementRuleCounterResetCommand captured = ManagementRuleEngineWire.CaptureCounterResetCommand(command);
        return CallAsync<ManagementRuleCounterResetOperationResult>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.CounterResetOperationName),
            "/counter-resets", write: new(RestBody.Json(captured), IfMatch: null, CounterReset: captured),
            options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleCounterResetOperationResult>> GetCounterResetOperationAsync(
        string applicationId, string rulesetId, string operationKey, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleCounterResetOperationResult>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.CounterResetOperationName),
            "/counter-reset-operations", options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleCounterResetOutcomesPage>> ListCounterResetOutcomesAsync(
        string applicationId, string rulesetId, string operationKey, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleCounterResetOutcomesPage>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.CounterResetOperationName,
                LookupSuffix: "/outcomes"), "/counter-reset-operations", page: page ?? new(), options: options, token: cancellationToken);
}

public static partial class ManagementRuleEngineWire
{
    /// <summary>The original administrative counter-reset operation name.</summary>
    public const string CounterResetOperationName = "management.rule-engine.counter.reset";

    /// <summary>The greatest number of explicit unique rule identities in one request.</summary>
    public const int MaximumExplicitCounterResetIds = 48;

    /// <summary>Encodes a closed bounded Rule Engine counter-reset command.</summary>
    public static byte[] EncodeRequest(ManagementRuleCounterResetCommand command)
        => Encode(CaptureCounterResetCommand(command), RequestJson, MaximumBodyBytes);

    /// <summary>Decodes a closed counter-reset command, returning null for invalid input.</summary>
    public static ManagementRuleCounterResetCommand? DecodeCounterResetRequest(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            return CaptureCounterResetCommand((ManagementRuleCounterResetCommand)Read(bytes,
                typeof(ManagementRuleCounterResetCommand), RequestJson, MaximumBodyBytes));
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            return null;
        }
    }

    internal static ManagementRuleCounterResetCommand CaptureCounterResetCommand(ManagementRuleCounterResetCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.RuleIds);
        string[] ids = command.RuleIds.Take(MaximumExplicitCounterResetIds + 1).ToArray();
        bool validSelection = command.Selection switch
        {
            "one" => ids.Length == 1,
            "selected" => ids.Length is >= 1 and <= MaximumExplicitCounterResetIds,
            "all" => ids.Length == 0,
            _ => false
        };
        Require(validSelection && ids.All(IsRuleId) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length
            && ManagementWire.IsUtf8Text(command.Reason, 1024), "Supply the exact reset selection and bounded original reason.");
        return command with
        {
            RuleIds = Array.AsReadOnly(ids)
        };
    }

    internal static bool MatchesCounterResetDecision(ManagementRuleCounterResetDecision retained, ManagementRuleCounterResetCommand requested)
    {
        if (retained.Selection != requested.Selection)
        {
            return false;
        }
        if (requested.Selection == "all")
        {
            return true;
        }
        return long.TryParse(retained.AppliedCount, NumberStyles.None, CultureInfo.InvariantCulture, out long applied)
            && long.TryParse(retained.RejectedCount, NumberStyles.None, CultureInfo.InvariantCulture, out long rejected)
            && applied <= requested.RuleIds.Count && rejected == requested.RuleIds.Count - applied;
    }

    private static void CheckCounterPage(ManagementRuleCounterPage page)
    {
        CheckScope(page.ApplicationId, page.Environment, page.RulesetId);
        Require(page.Availability is "fresh" or "stale" or "unavailable", "Invalid counter observation availability.");
        Require(page.ObservedAtUtc is null ? page.Availability != "fresh" : ManagementWire.IsTimestamp(page.ObservedAtUtc),
            "Invalid counter observation timestamp.");

        CheckPage(new ManagementPage<ManagementRuleCounterItem>(page.Items, page.NextCursor),
            item => item.RuleId, _ => page.Environment, item =>
            {
                Require(IsRuleId(item.RuleId)
                    && (item.LastResetAtUtc is null || ManagementWire.IsTimestamp(item.LastResetAtUtc)),
                    "Invalid rule counter identity or reset time.");
                Require(item.Count is null ? page.Availability != "fresh" : page.Availability != "unavailable"
                    && page.ObservedAtUtc is not null && ManagementWire.IsOrderedInteger(item.Count, allowZero: true),
                    "The counter count does not match the available evidence.");
            });
    }

    private static void CheckCounterResetResult(ManagementRuleCounterResetOperationResult result)
    {
        CheckScope(result.ApplicationId, result.Environment, result.RulesetId);
        CheckOperation(result.Operation, CounterResetOperationName);
        CheckOperationFacts(result.ResultSource, result.OriginalCompletion, result.Result is not null,
            result.Rejection, result.AttemptCompletion);
        if (result.Result is { } decision)
        {
            Require(decision.Selection is "one" or "selected" or "all", "Invalid original reset selection.");
            Require(ManagementWire.IsTimestamp(decision.ResetAtUtc)
                && ManagementWire.IsOrderedInteger(decision.AppliedCount, allowZero: true)
                && ManagementWire.IsOrderedInteger(decision.RejectedCount, allowZero: true), "Invalid original reset counts or timestamp.");
            if (decision.Selection != "all")
            {
                long applied = long.Parse(decision.AppliedCount, CultureInfo.InvariantCulture);
                long rejected = long.Parse(decision.RejectedCount, CultureInfo.InvariantCulture);
                int maximum = decision.Selection == "one" ? 1 : MaximumExplicitCounterResetIds;
                Require(applied <= maximum && rejected <= maximum - applied && applied + rejected > 0,
                    "The explicit reset counts exceed its bounded selection.");
            }
        }
    }

    private static void CheckCounterResetOutcomes(ManagementRuleCounterResetOutcomesPage page)
    {
        CheckScope(page.ApplicationId, page.Environment, page.RulesetId);
        CheckOperation(page.Operation, CounterResetOperationName);
        CheckPage(new ManagementPage<ManagementRuleCounterResetOutcome>(page.Items, page.NextCursor),
            item => item.RuleId, _ => page.Environment, item =>
                Require(IsRuleId(item.RuleId) && (item.Disposition is "reset" or "notFound"), "Invalid individual reset outcome."));
    }
}
