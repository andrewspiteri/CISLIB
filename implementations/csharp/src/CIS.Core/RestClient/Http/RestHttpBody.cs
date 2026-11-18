using IOStream = System.IO.Stream;

namespace CIS.RestClient.Http;

/// <summary>C# stream adapters with explicit request-content ownership.</summary>
public static class RestHttpBody
{
    /// <summary>Declares one-shot input. Ownership transfers only when an admitted operation acquires it.</summary>
    public static RestBody Stream(IOStream stream, bool ownsStream = false, string mediaType = "application/octet-stream")
    { ArgumentNullException.ThrowIfNull(stream); return new StreamBody(stream, ownsStream, mediaType); }
    /// <summary>Declares a factory producing a fresh equivalent client-owned stream per attempt.</summary>
    public static RestBody Reopen(Func<CancellationToken, ValueTask<IOStream>> open, string mediaType = "application/octet-stream")
    { ArgumentNullException.ThrowIfNull(open); return new StreamBody(open, mediaType); }
    /// <summary>Creates fresh client-owned native content, such as multipart, for each acquired attempt.</summary>
    /// <remarks>The factory must await its work and return equivalent content when replayable is true.</remarks>
    public static RestBody Content(Func<CancellationToken, ValueTask<HttpContent>> create, bool replayable = true)
    { ArgumentNullException.ThrowIfNull(create); return new NativeBody(create, replayable); }

    internal sealed class NativeBody(Func<CancellationToken, ValueTask<HttpContent>> create, bool replayable)
        : RestBody(RestBodyKind.Custom, "application/octet-stream", replayable)
    { internal Func<CancellationToken, ValueTask<HttpContent>> Create { get; } = create; }

    internal sealed class StreamBody : RestBody
    {
        internal StreamBody(IOStream stream, bool owns, string mediaType) : base(RestBodyKind.Stream, mediaType, false)
        { Input = stream; OwnsInput = owns; }
        internal StreamBody(Func<CancellationToken, ValueTask<IOStream>> open, string mediaType) : base(RestBodyKind.Stream, mediaType, true)
        { Open = open; OwnsInput = true; }
        internal IOStream? Input { get; }
        internal Func<CancellationToken, ValueTask<IOStream>>? Open { get; }
        internal bool OwnsInput { get; }
    }
}
