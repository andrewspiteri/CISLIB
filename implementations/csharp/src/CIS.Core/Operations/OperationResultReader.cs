using CIS.Operations.Hosting;

namespace CIS.Operations;

internal static class OperationResultReader
{
    internal static bool TryDecode<TResult>(OperationDefinition definition,
        IOperationResultCodec<TResult> codec, OperationStoredResult stored, out OperationDecision<TResult>? decision)
    {
        decision = null;
        if (!definition.ReadableResultContracts.Contains(stored.Contract)
            || stored.PayloadBytes > definition.MaximumResultBytes)
        {
            return false;
        }

        if (stored.Decision == OperationBusinessDecision.Rejected)
        {
            decision = OperationDecision<TResult>.Reject(stored.Error!.Code, stored.Error.Message);
            return true;
        }

        if (!codec.TryDecode(stored.Contract, stored.Value!.Value, out TResult? value))
        {
            return false;
        }

        decision = OperationDecision<TResult>.Succeed(value);
        return true;
    }

    internal static OperationStoredResult Encode<TResult>(OperationDefinition definition,
        IOperationResultCodec<TResult> codec, OperationDecision<TResult> decision)
    {
        OperationStoredResult stored = decision.Decision == OperationBusinessDecision.Succeeded
            ? new(definition.ResultContract, decision.Decision,
                codec.Encode(decision.Value) ?? throw new InvalidOperationException("The operation codec returned no encoded bytes."))
            : new(definition.ResultContract, decision.Decision, error: decision.Error);
        if (stored.PayloadBytes > definition.MaximumResultBytes)
        {
            throw new InvalidOperationException("The encoded operation result exceeds its configured byte limit.");
        }

        return stored;
    }
}
