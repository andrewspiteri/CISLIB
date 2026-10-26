using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationFiniteAssertionValidation;

namespace CIS.Verification;

// Physical PE/portable-PDB binding only. This helper cannot admit a mutation,
// normalize a role or assert that the runtime loaded either file.
internal static class HealthcheckMutationPortablePdbValidation
{
    internal sealed record Binding(string CodeViewGuid, uint CodeViewStamp, string PdbGuid, uint PdbStamp,
        string ExpectedChecksum, string ActualChecksum, bool ContentIdMatches, bool ChecksumMatches)
    {
        internal bool Matches => ContentIdMatches && ChecksumMatches;
        internal JsonObject Details() => new()
        {
            ["codeViewGuid"] = CodeViewGuid,
            ["codeViewStamp"] = CodeViewStamp,
            ["portablePdbGuid"] = PdbGuid,
            ["portablePdbStamp"] = PdbStamp,
            ["expectedSha256"] = ExpectedChecksum,
            ["actualZeroIdSha256"] = ActualChecksum,
            ["contentIdMatches"] = ContentIdMatches,
            ["checksumMatches"] = ChecksumMatches,
            ["loadedSymbolClaim"] = false
        };
    }

    internal static Binding Read(string assemblyPath, string pdbPath)
    {
        Need(new FileInfo(pdbPath).Length is > 20 and <= 64L * 1024 * 1024, "Unsupported portable-PDB body size.");
        try
        {
            using FileStream assembly = File.OpenRead(assemblyPath); using var pe = new PEReader(assembly);
            DebugDirectoryEntry[] entries = pe.ReadDebugDirectory().ToArray();
            DebugDirectoryEntry[] codeViews = entries.Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
            DebugDirectoryEntry[] checksums = entries.Where(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
            Need(codeViews.Length == 1 && codeViews[0].IsPortableCodeView && checksums.Length == 1,
                "Exactly one portable CodeView and one PDB-checksum entry are required.");
            CodeViewDebugDirectoryData codeView = pe.ReadCodeViewDebugDirectoryData(codeViews[0]);
            Need(codeView.Age == 1, "Portable CodeView age must be one.");
            PdbChecksumDebugDirectoryData checksum = pe.ReadPdbChecksumDebugDirectoryData(checksums[0]);
            Need(checksum.AlgorithmName == "SHA256" && checksum.Checksum.Length == 32, "Unsupported portable-PDB checksum algorithm/length.");
            byte[] bytes = File.ReadAllBytes(pdbPath);
            using var pdbStream = new MemoryStream(bytes, writable: false); using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            DebugMetadataHeader? header = provider.GetMetadataReader().DebugMetadataHeader;
            Need(header is not null && header.Id.Length == 20 && header.IdStartOffset >= 0 && header.IdStartOffset <= bytes.Length - 20,
                "Missing/invalid portable-PDB content-ID field.");
            var actualId = new BlobContentId(header.Id); var expectedId = new BlobContentId(codeView.Guid, codeViews[0].Stamp);
            // PE/COFF specification: hash the entire Portable PDB with only its
            // twenty-byte content ID zeroed. GUID equality alone is insufficient.
            bytes.AsSpan(header.IdStartOffset, 20).Clear();
            string actualChecksum = Convert.ToHexStringLower(SHA256.HashData(bytes)), expectedChecksum = Convert.ToHexStringLower(checksum.Checksum.AsSpan());
            return new(codeView.Guid.ToString(), codeViews[0].Stamp, actualId.Guid.ToString(), actualId.Stamp,
                expectedChecksum, actualChecksum, actualId == expectedId, actualChecksum == expectedChecksum);
        }
        catch (BadImageFormatException error) { throw new InvalidDataException("Malformed PE/portable-PDB metadata.", error); }
    }
}
