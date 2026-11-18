using System.Runtime.ExceptionServices;

namespace CIS.RestClient;

internal sealed class RestExchangePipeline(IReadOnlyList<IRestExchangeHandler> handlers, RestExchange primary,
    CancellationToken operationToken, bool captureUnexpectedCancellation = false, CancellationToken knownCallerToken = default) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly HashSet<IRestResponse> responses = new(ReferenceEqualityComparer.Instance);
    private readonly List<IRestResponse> order = [];
    private Task? disposal;
    private int run;

    internal ValueTask<IRestResponse> RunAsync(RestRequestContext request)
    {
        if (Interlocked.Exchange(ref run, 1) != 0) { throw new InvalidOperationException("An attempt has one exchange invocation."); }
        return InvokeAsync(0, request);
    }
    private async ValueTask<IRestResponse> InvokeAsync(int index, RestRequestContext request)
    {
        if (index == handlers.Count)
        {
            try { IRestResponse result = await primary(request, operationToken).ConfigureAwait(false); Track(result); return result; }
            catch (Exception error)
            {
                ExceptionDispatchInfo.Capture(RestCallbackFaultException.Capture(error, operationToken, captureUnexpectedCancellation, knownCallerToken)).Throw();
                throw;
            }
        }
        var invocationGate = new object();
        Task<IRestResponse>? nextTask = null;
        bool open = true, called = false;
        ExceptionDispatchInfo? fault = null;
        IRestResponse? selected = null;
        ValueTask<IRestResponse> Next(RestRequestContext supplied, CancellationToken token)
        {
            lock (invocationGate)
            {
                if (!open || called) { throw new InvalidOperationException("Next is usable once within the awaited handler invocation."); }
                called = true;
                if (!ReferenceEquals(request, supplied)) { throw new InvalidOperationException("Next must retain this operation's request context."); }
                if (token != operationToken) { throw new InvalidOperationException("Next must forward the provided operation cancellation token."); }
                nextTask = InvokeAsync(index + 1, supplied).AsTask();
                return new(nextTask);
            }
        }
        try { selected = await handlers[index].HandleAsync(request, Next, operationToken).ConfigureAwait(false); Track(selected); }
        catch (Exception error)
        { fault = ExceptionDispatchInfo.Capture(RestCallbackFaultException.Capture(error, operationToken, captureUnexpectedCancellation, knownCallerToken)); }
        finally { lock (invocationGate) { open = false; } }
        // A handler may start next and then throw or return another response.
        // Join the exact owned task before returning its value or fault.
        if (nextTask is not null)
        {
            try { Track(await nextTask.ConfigureAwait(false)); }
            catch (Exception error)
            {
                if (fault is null || IsKnownCancellation(fault.SourceException) && !IsKnownCancellation(error))
                { fault = ExceptionDispatchInfo.Capture(error); }
            }
        }
        fault?.Throw();
        return selected!;
    }
    private bool IsKnownCancellation(Exception error)
        => error is OperationCanceledException canceled && canceled.CancellationToken == operationToken && operationToken.IsCancellationRequested;
    private void Track(IRestResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (gate) { if (responses.Add(response)) { order.Add(response); } }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null; Task shared;
        lock (gate)
        {
            if (disposal is null) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); disposal = completion.Task; }
            shared = disposal;
        }
        if (completion is not null) { _ = DisposeCoreAsync(completion); }
        return new(shared);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        ExceptionDispatchInfo? fault = null;
        IRestResponse[] owned;
        lock (gate) { owned = order.ToArray(); }
        foreach (IRestResponse response in owned.Reverse())
        {
            try { await response.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { fault ??= ExceptionDispatchInfo.Capture(error); }
        }
        if (fault is not null) { completion.TrySetException(fault.SourceException); }
        else { completion.TrySetResult(); }
    }
}
