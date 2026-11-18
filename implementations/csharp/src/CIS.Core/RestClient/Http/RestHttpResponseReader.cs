namespace CIS.RestClient.Http;

internal sealed record RestReadResult<T>(RestDecodedValue<T> Decoded, RestErrorBody? ErrorBody, bool ErrorBodyTruncated);

// One consumer owns body processing. Mapper input is detached after this reader;
// a second consumer cannot unknowingly decode an already consumed stream.
internal sealed class RestHttpResponseReader(IRestResponse response, RestLimitsSnapshot limits, IRestOperationObservation? observation = null)
{
    private int used;
    internal async ValueTask<RestReadResult<T>> ReadAsync<T>(RestResponseFormat format, bool requireValue,
        IRestContentCodec? codec, CancellationToken token)
    {
        if (Interlocked.Exchange(ref used, 1) != 0) { throw new InvalidOperationException("A response has one consumption owner."); }
        token.ThrowIfCancellationRequested();
        if (response.HttpStatusCode is < 200 or >= 300)
        {
            if (limits.MaxErrorBodyBytes == 0) { return new(RestDecodedValue<T>.Absent, null, false); }
            using var stream = new RestTransferStream(response.Body, true, long.MaxValue, observation is null ? null : observation.DownloadCompleted, status: response.HttpStatusCode);
            using var prefix = new MemoryStream(); byte[] buffer = new byte[Math.Min(limits.MaxErrorBodyBytes, 8192)];
            bool truncated = false;
            while (true)
            {
                int requested = (int)Math.Min(buffer.Length, (long)limits.MaxErrorBodyBytes - prefix.Length + 1);
                int count = await stream.ReadAsync(buffer.AsMemory(0, requested), token).ConfigureAwait(false);
                if (count == 0) { break; }
                int retained = (int)Math.Min(count, limits.MaxErrorBodyBytes - prefix.Length);
                prefix.Write(buffer, 0, retained);
                if (retained != count) { truncated = true; break; }
            }
            string? media = response.Headers.TryGetValue("Content-Type", out var types) ? types.FirstOrDefault() : null;
            return new(RestDecodedValue<T>.Absent, new(prefix.ToArray(), media), truncated);
        }
        if (typeof(T) == typeof(RestNoValue) || format == RestResponseFormat.NoValue)
        {
            if (requireValue) { throw new RestDecodingException(); }
            return new(RestDecodedValue<T>.Absent, null, false);
        }
        IRestContentCodec selected = codec ?? RestDefaultContentCodec.ForResponse(response.Headers, format);
        if (!selected.CanDecode(typeof(T), format)) { throw new ArgumentException("The result type and response format are incompatible."); }
        using var bounded = new RestTransferStream(response.Body, true, limits.MaxResponseBytes, observation is null ? null : observation.DownloadCompleted, status: response.HttpStatusCode);
        // Only the known default codec identifies its own expected parse errors.
        // Arbitrary custom codec exceptions propagate unchanged.
        RestDecodedValue<T> decoded = await selected.DecodeAsync<T>(bounded, format, token).ConfigureAwait(false);
        if (bounded.LimitExceeded) { throw new RestResponseLimitException(); }
        if (requireValue && !decoded.HasValue) { throw new RestDecodingException(); }
        return new(decoded, null, false);
    }
}
