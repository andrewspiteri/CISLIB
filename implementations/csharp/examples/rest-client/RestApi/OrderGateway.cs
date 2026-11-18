using CIS.RestClient;

namespace CIS.RestClient.Examples.RestApi;

internal sealed class OrderGateway([FromKeyedServices("orders")] IRestClient client)
{
    internal Task<RestResult<OrderReceipt>> SubmitAsync(OrderCommand command, CancellationToken token)
        => client.Post<OrderReceipt>("orders", new() { Body = RestBody.Json(command), OperationName = "submit" }, token);

    internal Task<RestResult<OrderReceipt>> ReadAsync(string id, CancellationToken token)
        => client.Get<OrderReceipt>("orders/" + Uri.EscapeDataString(id), new()
        { OperationName = "retry", RetryPolicy = RestRetrySelection.Named(RestRetryPolicy.TransientHttp.Name) }, token);

    internal async Task<ExerciseReport> ExerciseAsync(CancellationToken token)
    {
        RestResult<OrderReceipt> posted = await SubmitAsync(new("sample-item", 2), token);
        RestResult<OrderReceipt> retried = await ReadAsync("retry", token);
        RestResult<OrderReceipt> business = await client.Get<OrderReceipt>("orders/declined", new() { OperationName = "business" }, token);
        RestResult<OrderReceipt> empty = await client.Get<OrderReceipt>("orders/empty", new() { OperationName = "empty" }, token);
        bool cancellation = await CancelPreparedCallAsync(token);
        if (!retried.Success || !empty.Success || business.Success || business.Context.HttpStatusCode != 200)
        { throw new InvalidOperationException("The origin did not satisfy the example response contract."); }
        return new(posted.Success && posted.Context.HasValue && posted.Value is { Accepted: true },
            retried.Context.AttemptCount, business.ErrorCode, empty.Context.HasValue, cancellation);
    }

    private async Task<bool> CancelPreparedCallAsync(CancellationToken parent)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(parent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // This admitted callback is actual cancellable work; it never dispatches.
        Task<RestResult<OrderReceipt>> operation = client.Get<OrderReceipt>("orders/cancel", new()
        {
            OperationName = "cancel",
            ConfigureRequest = async (_, effective) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, effective); }
        }, caller.Token);
        try
        {
            await entered.Task.WaitAsync(parent); await caller.CancelAsync();
            try { await operation; return false; }
            catch (OperationCanceledException error) when (error.CancellationToken == caller.Token && caller.IsCancellationRequested) { return true; }
        }
        finally
        {
            await caller.CancelAsync();
            try { await operation; }
            catch (OperationCanceledException) when (operation.IsCanceled) { }
        }
    }
}

internal sealed class OrderResponseMapper : IRestResponseMapper
{
    public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(response.Decoded.HasValue && response.Decoded.Value is OrderReceipt { Accepted: false } declined
            ? RestResponseDecision.RejectBusiness(declined.Reason ?? "Order declined.", "orders.declined")
            : RestResponseDecision.Continue);
    }
}

internal sealed record OrderCommand(string Item, int Quantity);
internal sealed record OrderReceipt(string Id, bool Accepted, string? Reason = null);
internal sealed record ExerciseReport(bool PostSuccess, int RetryAttempts, string? BusinessCode, bool EmptyHasValue, bool CancellationPreserved);
