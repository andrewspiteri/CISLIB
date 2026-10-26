using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace CIS.Verification;

// Byte/framing only. The caller binds the complete physical inventory, exact
// ordered active/backup map, original path, PID/epoch/role/kernel and normality.
// This primitive neither infers a missing generation nor repairs trace text.
internal static class NativeDiagnosticExtentReader
{
    internal sealed record Extent(string Path, long Bytes, string Sha256);
    internal sealed record Line(int Number, long ByteOffset, int ExtentIndex, long ExtentByteOffset, string Text);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int MaximumRecordBytes = 32 * 1024 * 1024;

    internal static IEnumerable<Line> Read(IReadOnlyList<Extent> supplied)
    {
        Extent[] extents = supplied.ToArray();
        Need(extents.Length is 1 or 2, "Only one unchanged body or two explicit ordered retained extents are supported.");
        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Need(extents.Select(extent => Path.GetFullPath(extent.Path)).Distinct(pathComparer).Count() == extents.Length, "Repeated normalized physical extent.");
        foreach (Extent extent in extents)
        {
            Verify(extent);
        }

        try
        {
            byte[] buffer = new byte[65536]; var line = new ArrayBufferWriter<byte>(); int number = 0; long logicalOffset = 0;
            for (int part = 0; part < extents.Length; part++)
            {
                Extent extent = extents[part]; long physicalOffset = 0, readBytes = 0;
                using var input = File.OpenRead(extent.Path);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    hash.AppendData(buffer, 0, read); readBytes = checked(readBytes + read); int at = 0;
                    while (at < read)
                    {
                        int newline = Array.IndexOf(buffer, (byte)'\n', at, read - at), count = (newline < 0 ? read : newline) - at;
                        Need(line.WrittenCount <= MaximumRecordBytes - count, "Unsupported overbound diagnostic record.");
                        buffer.AsSpan(at, count).CopyTo(line.GetSpan(count)); line.Advance(count); at += count;
                        if (newline < 0)
                        {
                            continue;
                        }

                        int rawBytes = line.WrittenCount; string text = Utf8.GetString(line.WrittenSpan);
                        if (text.EndsWith('\r'))
                        {
                            text = text[..^1];
                        }

                        yield return new Line(checked(++number), logicalOffset, part, physicalOffset, text);
                        logicalOffset = checked(logicalOffset + rawBytes + 1L); physicalOffset = checked(physicalOffset + rawBytes + 1L);
                        line.Clear(); at++;
                    }
                }
                Need(readBytes == extent.Bytes && Convert.ToHexStringLower(hash.GetHashAndReset()) == extent.Sha256, "Extent changed while streamed.");
                Need(line.WrittenCount == 0 && physicalOffset == extent.Bytes, "Every physical extent must end at its actual LF boundary; no separator or partial-record repair.");
            }
            Need(logicalOffset == extents.Sum(extent => extent.Bytes), "Exact logical extent byte coverage.");
        }
        finally
        {
            // Also runs when a consumer stops early (First/Take/exception).
            foreach (Extent extent in extents)
            {
                Verify(extent);
            }
        }
    }

    private static void Verify(Extent extent)
    {
        Need(Path.IsPathFullyQualified(extent.Path) && extent.Bytes > 0 && extent.Sha256.Length == 64
            && extent.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Complete absolute nonempty extent pin required.");
        for (string? path = Path.GetFullPath(extent.Path); path is not null; path = Path.GetDirectoryName(path))
        {
            Need((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Linked extent unsupported.");
        }

        Need(new FileInfo(extent.Path).Length == extent.Bytes, "Extent byte length differs.");
        using var stream = File.OpenRead(extent.Path);
        Need(Convert.ToHexStringLower(SHA256.HashData(stream)) == extent.Sha256, "Extent body differs from exact pin.");
        stream.Position = extent.Bytes - 1;
        Need(stream.ReadByte() == '\n', "Every physical extent ends at an actual LF; an early consumer cannot waive this boundary.");
    }
    private static void Need(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidDataException(message);
        }
    }
}
