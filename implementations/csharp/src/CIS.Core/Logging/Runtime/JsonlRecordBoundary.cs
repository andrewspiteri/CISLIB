using System.Text;
using System.Text.Json;

namespace CIS.Logging.Runtime;

internal enum JsonlBoundaryStatus { Empty, Complete, Partial, Invalid }
internal readonly record struct JsonlRecordBoundary(long Length, JsonlBoundaryStatus Status)
{
    internal static JsonlRecordBoundary Inspect(FileOutputOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner); owner.RequireOwnership();
        if (owner.Inventory?.Format != LogOutputFormat.JsonLines) { throw new InvalidOperationException("JSONL boundary inspection requires validated managed format identity."); }
        FileStream file = owner.ActiveFile; long originalPosition = file.Position, length = file.Length;
        try
        {
            if (length == 0) { return new(0, JsonlBoundaryStatus.Empty); }
            // Inventory declares no BOM. Check the actual prefix before interpreting any tail.
            Span<byte> prefix = stackalloc byte[3]; file.Position = 0; int prefixLength = (int)Math.Min(3, length); file.ReadExactly(prefix[..prefixLength]);
            if (prefixLength == 3 && prefix.SequenceEqual(new byte[] { 239, 187, 191 })) { return new(length, JsonlBoundaryStatus.Invalid); }
            file.Position = length - 1;
            // Preserve arbitrary partial UTF-8/JSON bytes for later damaged-segment recovery.
            if (file.ReadByte() != 10) { return new(length, JsonlBoundaryStatus.Partial); }
            // Find only the final record, bounded by the absolute supported entry cap,
            // rather than the caller's possibly lower new cap or the whole segment size.
            const int cap = LoggingOptions.MaximumMaxEntryBytes; long position = length - 1, start = 0; int scanned = 0; bool found = false;
            Span<byte> block = stackalloc byte[4096];
            while (position > 0 && scanned < cap)
            {
                int count = (int)Math.Min(Math.Min(block.Length, position), cap - scanned); long offset = position - count; file.Position = offset; file.ReadExactly(block[..count]);
                int previousLf = block[..count].LastIndexOf((byte)10);
                if (previousLf >= 0) { start = offset + previousLf + 1; found = true; break; }
                position = offset; scanned += count;
            }
            if (!found && position != 0) { return new(length, JsonlBoundaryStatus.Invalid); }
            int recordLength = checked((int)(length - start)); if (recordLength > cap) { return new(length, JsonlBoundaryStatus.Invalid); }
            byte[] record = new byte[recordLength]; file.Position = start; file.ReadExactly(record);
            if (record.AsSpan().Contains((byte)13)) { return new(length, JsonlBoundaryStatus.Invalid); }
            try
            {
                _ = new UTF8Encoding(false, true).GetCharCount(record);
                using JsonDocument json = JsonDocument.Parse(record, new JsonDocumentOptions { MaxDepth = 64 });
                return new(length, json.RootElement.ValueKind == JsonValueKind.Object ? JsonlBoundaryStatus.Complete : JsonlBoundaryStatus.Invalid);
            }
            catch (DecoderFallbackException) { return new(length, JsonlBoundaryStatus.Invalid); }
            catch (JsonException) { return new(length, JsonlBoundaryStatus.Invalid); }
        }
        finally { file.Position = originalPosition; }
    }
}
