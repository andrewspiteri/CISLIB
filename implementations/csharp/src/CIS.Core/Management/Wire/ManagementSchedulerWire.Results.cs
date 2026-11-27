using System.Text.Json;
using CIS.Operations;

namespace CIS.Management.Wire;

public static partial class ManagementSchedulerWire
{
    private static void Transaction(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["outcome", "cleanupSucceeded"], "cleanupCodes");
        Token(value, "outcome");
        _ = Boolean(value, "cleanupSucceeded");
        if (Has(value, "cleanupCodes"))
        {
            foreach (JsonElement code in Items(value, "cleanupCodes", 16).EnumerateArray())
            {
                string text = Text(code);
                Require(text.Length is >= 1 and <= 128 && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':'));
            }
        }
    }
    private static void Rejection(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["code", "message"]);
        OperationText.Code(Text(value, "code"), "code");
        OperationText.Utf8(Text(value, "message"), 1024, "message", allowControls: true);
    }
    private static void Decision(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["commandVersion", "kind", "decision", "code", "decidedAtUtc", "applicationId", "objectId"],
            "revision", "eTag", "runId", "attemptId");
        Version(value);
        Token(value, "kind");
        Token(value, "decision");
        Bytes(value, "code", 128);
        Time(value, "decidedAtUtc");
        Id(value, "applicationId");
        Id(value, "objectId");
        Optional(value, "revision", _ => Integer(value, "revision"));
        Optional(value, "eTag", _ => ETag(value, "eTag"));
        Optional(value, "runId", _ => Id(value, "runId"));
        Optional(value, "attemptId", _ => Id(value, "attemptId"));
    }
    private static void OperationResult(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["operation", "resultSource", "originalCompletion"], "result", "attemptCompletion", "rejection");
        AdministrativeOperation(Field(value, "operation"), closed, limits);
        Token(value, "resultSource");
        Token(value, "originalCompletion");
        Require(Has(value, "result") != Has(value, "rejection"));
        Optional(value, "result", item => Decision(item, closed, limits));
        Optional(value, "rejection", item => Rejection(item, closed, limits));
        Optional(value, "attemptCompletion", item => Transaction(item, closed, limits));
        if (Text(value, "resultSource") is "replayed" or "found")
        {
            Require(Text(value, "originalCompletion") == "committed");
        }
    }
    private static void Problem(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["type", "title", "status", "code"], "detail", "correlationId", "operation", "result");
        Scalar(value, "type", 1024);
        Require(Uri.TryCreate(Text(value, "type"), UriKind.Absolute, out _));
        Bytes(value, "title", 256);
        Number(value, "status", 400, 599);
        Bytes(value, "code", 128);
        Optional(value, "detail", _ => Bytes(value, "detail", 1024, empty: true));
        Optional(value, "correlationId", _ => Scalar(value, "correlationId", 128));
        Optional(value, "operation", item => AdministrativeOperation(item, closed, limits));
        Optional(value, "result", item => OperationResult(item, closed, limits));
        if (Has(value, "operation") && Has(value, "result"))
        {
            Require(Field(value, "operation").Deserialize<ManagementOperationReference>(Json)
                == Field(Field(value, "result"), "operation").Deserialize<ManagementOperationReference>(Json));
        }
    }

    private static object Freeze(object value) => value switch
    {
        ManagementConfigurationShape declaration => ManagementCatalogValidation.Freeze(declaration),
        ManagementTransactionFacts transaction => transaction with { CleanupCodes = transaction.CleanupCodes is null ? null : Copy(transaction.CleanupCodes) },
        ManagementSchedulerOperationResult result => result with
        {
            AttemptCompletion = result.AttemptCompletion is null ? null : (ManagementTransactionFacts)Freeze(result.AttemptCompletion)
        },
        ManagementSchedulerProblem problem => problem with { Result = problem.Result is null ? null : (ManagementSchedulerOperationResult)Freeze(problem.Result) },
        ManagementPage<ManagementCapabilityDefinition> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementCapabilityParticipation> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementConfigurationCohortChoice> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementSchedulerTrigger> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementSchedulerTask> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementSchedulerRun> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementSchedulerAttempt> page => page with { Items = Copy(page.Items) },
        _ => value
    };

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values) => Array.AsReadOnly(values.ToArray());
}
