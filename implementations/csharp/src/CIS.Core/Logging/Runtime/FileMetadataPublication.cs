using System.Text.Json;

namespace CIS.Logging.Runtime;
// One private publication slot per metadata kind. Only this attempt's successfully
// created file may be removed; an occupied slot is never adopted or overwritten.
internal static class FileMetadataPublication
{
    internal static string CandidatePath(string path) => path + ".candidate.tmp";
    internal static void Publish(string path, Action<Utf8JsonWriter> write, bool overwrite, Action<string>? beforePublish = null)
    {
        string candidate = CandidatePath(path);
        bool ownsCandidate = false;
        try
        {
            FileStream file;
            try
            {
                file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                ownsCandidate = true;
            }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && (File.Exists(candidate) || Directory.Exists(candidate)))
            {
                throw new CoreLogInitializationException("file", "FileMetadataCandidateUnavailable");
            }

            using (file)
            {
                using (var writer = new Utf8JsonWriter(file))
                {
                    write(writer);
                    writer.Flush();
                }

                file.WriteByte((byte)'\n');
                file.Flush(flushToDisk: true);
            }

            // Trusted fault fixtures can hold the closed candidate before the real move.
            // Production supplies no callback; all publication/deletion remains actual I/O.
            beforePublish?.Invoke(candidate);
            File.Move(candidate, path, overwrite);
            ownsCandidate = false;
        }
        finally
        {
            if (ownsCandidate)
            {
                try
                {
                    File.Delete(candidate);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // A blocked slot stops later publication attempts instead of growing
                    // an unbounded set of private files. Published intent is never removed here.
                    throw new CoreLogInitializationException("file", "FileMetadataCandidateCleanupFailed");
                }
            }
        }
    }
}
