using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;

namespace CIS.RestClient.Http;

// Acquired only after logical admission. The lease owns sources returned by
// factories and explicitly transferred streams; descriptors/snapshots own none.
internal sealed class RestHttpContentLease : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim signingGate = new(1, 1);
    private readonly RestBody? body;
    private readonly IRestContentCodec codec;
    private readonly IRestOperationObservation? observation;
    private readonly CancellationToken operationToken;
    private readonly CancellationToken knownCallerToken;
    private readonly TaskCompletionSource serializationDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int serializations;
    private ExceptionDispatchInfo? encodingFault;
    private Stream? input;
    private HttpContent? native;
    private bool ownsInput;
    private byte[]? signingBytes;
    private RestBody? preparedReplay;
    private Task? disposal;
    private ExceptionDispatchInfo? preparationFailure;

    private RestHttpContentLease(RestBody? body, IRestContentCodec? codec, IRestOperationObservation? observation,
        CancellationToken token, CancellationToken knownCallerToken)
    {
        this.body = body; this.codec = codec ?? RestDefaultContentCodec.Instance;
        this.observation = observation; operationToken = token; this.knownCallerToken = knownCallerToken;
    }
    internal HttpContent? Content { get; private set; }
    internal ExceptionDispatchInfo? EncodingFault { get { lock (gate) { return encodingFault; } } }
    internal bool CanReplay => body is null || body.IsReplayable || signingBytes is not null;
    internal RestBody? ReplayBody => signingBytes is { } bytes
        ? preparedReplay ??= new PreparedBody(bytes, Content?.Headers.ContentType?.ToString() ?? body!.MediaType) : body;
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => new ReadOnlyDictionary<string, IReadOnlyList<string>>(
        Content?.Headers.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()), StringComparer.OrdinalIgnoreCase)
        ?? new(StringComparer.OrdinalIgnoreCase));

    internal static async ValueTask<RestHttpContentLease> AcquireAsync(RestBody? body, IRestContentCodec? codec,
        IRestOperationObservation? observation, CancellationToken token, CancellationToken knownCallerToken = default)
    {
        token.ThrowIfCancellationRequested();
        var lease = new RestHttpContentLease(body, codec, observation, token, knownCallerToken);
        if (body is null) { return lease; }
        // Header validation precedes ownership acquisition.
        MediaTypeHeaderValue media = MediaTypeHeaderValue.Parse(body.MediaType);
        try
        {
            if (body is RestHttpBody.StreamBody stream)
            {
                if (stream.Open is null && !stream.Input!.CanRead)
                { throw new ArgumentException("A request stream must be readable."); }
                lease.input = stream.Open is null ? stream.Input! : await stream.Open(token).ConfigureAwait(false);
                if (lease.input is null) { throw new InvalidOperationException("A request stream factory returned null."); }
                lease.ownsInput = stream.OwnsInput;
                if (!lease.input.CanRead) { throw new ArgumentException("A request stream must be readable."); }
            }
            else if (body is RestHttpBody.NativeBody content)
            {
                lease.native = await content.Create(token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A content factory returned null.");
            }
            token.ThrowIfCancellationRequested();
            var wrapper = new LeaseContent(lease);
            if (lease.native is { } provided)
            { foreach (var header in provided.Headers) { wrapper.Headers.TryAddWithoutValidation(header.Key, header.Value); } }
            else { wrapper.Headers.ContentType = media; }
            lease.Content = wrapper;
            return lease;
        }
        catch (Exception original)
        {
            try { await lease.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Request acquisition and owned cleanup both failed.", original, cleanup); }
            throw;
        }
    }

    internal async ValueTask<ReadOnlyMemory<byte>> GetSigningBytesAsync(int maximumBytes, CancellationToken token)
    {
        if (maximumBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumBytes)); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationToken, token);
        bool entered = false;
        try
        {
            await signingGate.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
            if (disposal is not null) { throw new ObjectDisposedException(nameof(RestHttpContentLease)); }
            preparationFailure?.Throw();
            if (signingBytes is null)
            {
                using var buffer = new MemoryStream();
                using var bounded = new RestTransferStream(buffer, false, maximumBytes, signing: true);
                await EncodeAsync(bounded, linked.Token).ConfigureAwait(false);
                if (bounded.LimitExceeded) { throw new RestContentPreparationException(); }
                signingBytes = buffer.ToArray();
            }
            if (signingBytes.Length > maximumBytes) { throw new RestContentPreparationException(); }
            return signingBytes;
        }
        catch (Exception error)
        {
            Exception failure = NormalizeCancellation(error, linked.Token, operationToken, token);
            if (failure is not RestHttpExpectedException && !IsIdentifiedCancellation(failure, token))
            { lock (gate) { encodingFault ??= ExceptionDispatchInfo.Capture(failure); } }
            preparationFailure ??= ExceptionDispatchInfo.Capture(failure);
            if (!ReferenceEquals(error, failure)) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            throw;
        }
        finally { if (entered) { signingGate.Release(); } }
    }

    private Task SerializeAsync(Stream destination, CancellationToken token)
    {
        lock (gate)
        {
            if (disposal is not null) { return Task.FromException(new ObjectDisposedException(nameof(RestHttpContentLease))); }
            serializations++;
        }
        return SerializeCoreAsync(destination, token);
    }

    private async Task SerializeCoreAsync(Stream destination, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationToken, token);
        try
        {
            preparationFailure?.Throw();
            using var counted = new RestTransferStream(destination, false, long.MaxValue, observation is null ? null : observation.UploadCompleted);
            if (signingBytes is { } bytes) { await counted.WriteAsync(bytes, linked.Token).ConfigureAwait(false); }
            else { await EncodeAsync(counted, linked.Token).ConfigureAwait(false); }
        }
        catch (RestHttpExpectedException) { throw; }
        catch (Exception error)
        {
            Exception failure = NormalizeCancellation(error, linked.Token, operationToken, token);
            if (!ReferenceEquals(error, failure)) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            if (IsIdentifiedCancellation(error, token)) { throw; }
            lock (gate) { encodingFault ??= ExceptionDispatchInfo.Capture(error); }
            throw new RestEncodingFaultException(error);
        }
        finally
        {
            lock (gate)
            {
                serializations--;
                if (disposal is not null && serializations == 0) { serializationDrained.TrySetResult(); }
            }
        }
    }
    private static Exception NormalizeCancellation(Exception error, CancellationToken linked, CancellationToken operation, CancellationToken additional)
    {
        if (error is not OperationCanceledException canceled || canceled.CancellationToken != linked || !linked.IsCancellationRequested) { return error; }
        if (operation.IsCancellationRequested) { return new OperationCanceledException("Request content processing was canceled.", error, operation); }
        if (additional.IsCancellationRequested) { return new OperationCanceledException("Request content processing was canceled.", error, additional); }
        return error;
    }
    private bool IsIdentifiedCancellation(Exception error, CancellationToken additional)
        => error is OperationCanceledException canceled
            && (canceled.CancellationToken == operationToken && operationToken.IsCancellationRequested
                || canceled.CancellationToken == additional && additional.IsCancellationRequested
                || canceled.CancellationToken == knownCallerToken && knownCallerToken.IsCancellationRequested);
    private async ValueTask EncodeAsync(Stream destination, CancellationToken token)
    {
        if (body is null) { return; }
        if (body is PreparedBody prepared) { await destination.WriteAsync(prepared.WireBytes, token).ConfigureAwait(false); }
        else if (native is not null) { await native.CopyToAsync(destination, token).ConfigureAwait(false); }
        else if (input is not null)
        {
            try { await input.CopyToAsync(destination, token).ConfigureAwait(false); }
            catch (IOException error) { throw new RestRequestWriteException(error); }
        }
        else { await codec.EncodeAsync(body, destination, token).ConfigureAwait(false); }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null; Task shared;
        lock (gate)
        {
            if (disposal is null) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); disposal = completion.Task; }
            if (serializations == 0) { serializationDrained.TrySetResult(); }
            shared = disposal;
        }
        if (completion is not null) { _ = DisposeCoreAsync(completion); }
        return new(shared);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            // Native cancellation can finish SendAsync while a custom encoder is
            // still unwinding. Its exact callback and original fault remain ours.
            await serializationDrained.Task.ConfigureAwait(false);
            Content?.Dispose(); native?.Dispose();
            if (ownsInput && input is not null) { await input.DisposeAsync().ConfigureAwait(false); }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
    // Only explicitly bounded signing creates this internal wire representation.
    // An ordinary caller Bytes descriptor still passes through its configured codec.
    private sealed class PreparedBody(ReadOnlyMemory<byte> bytes, string mediaType)
        : RestBody(RestBodyKind.Bytes, mediaType, true)
    {
        internal ReadOnlyMemory<byte> WireBytes { get; } = bytes;
    }

    private sealed class LeaseContent(RestHttpContentLease owner) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => owner.SerializeAsync(stream, owner.operationToken);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token) => owner.SerializeAsync(stream, token);
        protected override bool TryComputeLength(out long length)
        {
            if (owner.signingBytes is { } bytes) { length = bytes.Length; return true; }
            if (owner.body is PreparedBody prepared) { length = prepared.WireBytes.Length; return true; }
            if (owner.body?.Kind == RestBodyKind.Bytes && owner.codec is RestDefaultContentCodec)
            { length = ((ReadOnlyMemory<byte>)owner.body.Value!).Length; return true; }
            length = 0; return false;
        }
    }
}
