using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace CIS.RestClient.Http;

internal sealed class RestDefaultContentCodec : IRestContentCodec
{
    internal static RestDefaultContentCodec Instance { get; } = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Encoding textEncoding;
    private RestDefaultContentCodec(Encoding? encoding = null) { textEncoding = encoding ?? new UTF8Encoding(false, true); }
    internal static RestDefaultContentCodec ForResponse(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, RestResponseFormat format)
    {
        if (format != RestResponseFormat.Text || !headers.TryGetValue("Content-Type", out var values)
            || !MediaTypeHeaderValue.TryParse(values.FirstOrDefault(), out var media) || media.CharSet is null) { return Instance; }
        try { return new(Encoding.GetEncoding(media.CharSet.Trim('"'), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)); }
        catch (ArgumentException error) { throw new RestDecodingException(error); }
    }
    public bool CanDecode(Type type, RestResponseFormat format)
        => format switch { RestResponseFormat.Json => true, RestResponseFormat.Text => type == typeof(string),
            RestResponseFormat.Bytes => type == typeof(byte[]), RestResponseFormat.NoValue => type == typeof(RestNoValue), _ => false };

    public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            switch (body.Kind)
            {
                case RestBodyKind.Json:
                    await JsonSerializer.SerializeAsync(destination, body.Value, body.ValueType ?? throw new ArgumentException("JSON needs its declared type."), Json, token).ConfigureAwait(false);
                    break;
                case RestBodyKind.Text:
                    await destination.WriteAsync(new UTF8Encoding(false, true).GetBytes((string)body.Value!), token).ConfigureAwait(false); break;
                case RestBodyKind.Bytes:
                    await destination.WriteAsync((ReadOnlyMemory<byte>)body.Value!, token).ConfigureAwait(false); break;
                case RestBodyKind.Form:
                    using (var form = new FormUrlEncodedContent((IEnumerable<KeyValuePair<string, string>>)body.Value!))
                    { await form.CopyToAsync(destination, token).ConfigureAwait(false); }
                    break;
                default: throw new NotSupportedException("Configure a codec for this explicit content extension.");
            }
        }
        catch (JsonException error) { throw new RestContentPreparationException(error); }
        catch (EncoderFallbackException error) { throw new RestContentPreparationException(error); }
    }

    public async ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
    {
        if (!CanDecode(typeof(T), format)) { throw new ArgumentException("The result type does not support this explicit response format."); }
        if (format == RestResponseFormat.NoValue) { return RestDecodedValue<T>.Absent; }
        if (format == RestResponseFormat.Json)
        {
            byte[] first = new byte[1];
            if (await source.ReadAsync(first, token).ConfigureAwait(false) == 0) { return RestDecodedValue<T>.Absent; }
            using var prefixed = new PrefixStream(first[0], source);
            try
            {
                T? value = await JsonSerializer.DeserializeAsync<T>(prefixed, Json, token).ConfigureAwait(false);
                return RestDecodedValue<T>.Present(value!);
            }
            catch (JsonException error) { throw new RestDecodingException(error); }
        }
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes, token).ConfigureAwait(false);
        if (bytes.Length == 0) { return RestDecodedValue<T>.Absent; }
        byte[] valueBytes = bytes.ToArray();
        if (format == RestResponseFormat.Bytes) { return RestDecodedValue<T>.Present((T)(object)valueBytes); }
        try
        {
            using var text = new StreamReader(new MemoryStream(valueBytes, writable: false), textEncoding, detectEncodingFromByteOrderMarks: true);
            string value = await text.ReadToEndAsync(token).ConfigureAwait(false);
            return RestDecodedValue<T>.Present((T)(object)value);
        }
        catch (DecoderFallbackException error) { throw new RestDecodingException(error); }
    }

    private sealed class PrefixStream(byte first, Stream inner) : Stream
    {
        private bool pending = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        { if (pending && count > 0) { pending = false; buffer[offset] = first; return 1; } return inner.Read(buffer, offset, count); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (pending && buffer.Length > 0) { pending = false; buffer.Span[0] = first; return ValueTask.FromResult(1); }
            return inner.ReadAsync(buffer, token);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    }
}
