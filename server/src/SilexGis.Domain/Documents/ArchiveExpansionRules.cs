// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// The ceilings an archive expansion works under. Defaults are sized for the thing this
/// feature exists for — a club handing over its scanned paper archive — and are configuration
/// so an installation with a genuinely larger one can raise them knowingly.
/// </summary>
/// <param name="MaxEntries">How many entries an archive may hold.</param>
/// <param name="MaxTotalUncompressedBytes">
/// How much the archive may come to once expanded. This is the limit that matters: a
/// compressed archive states its own size, and the whole point of a decompression bomb is
/// that the stated size is a small fraction of the real one.
/// </param>
/// <param name="MaxCompressionRatio">
/// How much larger than its compressed form a single entry may expand to. A scanned TIFF
/// compresses perhaps five to one and a text file perhaps ten; a thousand to one is not a
/// document. Checked while the entry is being read rather than from the header, because the
/// header is written by whoever made the archive.
/// </param>
public sealed record ArchiveLimits(
    int MaxEntries = 5000,
    long MaxTotalUncompressedBytes = 20L * 1024 * 1024 * 1024,
    int MaxCompressionRatio = 200)
{
    public static ArchiveLimits Default { get; } = new();
}

/// <summary>
/// Which entries of an archive become documents, and when an expansion must be abandoned.
///
/// <para>
/// An uploaded archive is the most hostile input this application takes: it is a file
/// structure written entirely by somebody else, expanded by the server, onto the server's own
/// disk. The two classic attacks are a path that escapes the destination and a small file
/// that expands to fill the disk, and both are refused here rather than in the handler that
/// does the reading — a rule sitting in a loop that also opens streams and writes rows is a
/// rule nobody can test the edges of.
/// </para>
/// </summary>
public static class ArchiveExpansionRules
{
    /// <summary>The whole archive was abandoned: it holds more entries than the cap allows.</summary>
    public const string TooManyEntriesCode = "archive.too_many_entries";

    /// <summary>The whole archive was abandoned: expanding it passed the total-size cap.</summary>
    public const string TooLargeCode = "archive.too_large";

    /// <summary>The whole archive was abandoned: an entry expanded far beyond its stated size.</summary>
    public const string BombCode = "archive.suspicious_compression";

    /// <summary>The archive itself would not open.</summary>
    public const string UnreadableCode = "archive.unreadable";

    /// <summary>Extensions this recognises as an archive it can expand.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".zip"];

    /// <summary>Whether a file name is one of the archives this can expand.</summary>
    public static bool IsArchive(string? fileName) =>
        Extensions.Contains(UploadLimits.ExtensionOf(fileName), StringComparer.Ordinal);

    /// <summary>
    /// Whether an entry is one to store, given its name inside the archive.
    /// </summary>
    /// <remarks>
    /// Directory entries are skipped rather than refused: the folders they describe are
    /// created from the paths of the files inside them, so an archive that lists its
    /// directories and one that does not expand identically. An empty folder therefore leaves
    /// no shelf behind, which is the right trade — a shelf with nothing on it is not what
    /// somebody handing over an archive is asking for.
    /// <para>
    /// A nested archive is stored as a file rather than expanded. Expanding it would make the
    /// entry count and size caps meaningless (they bound one archive, not a chain of them) and
    /// turns a bounded walk into a recursive one whose depth the attacker chooses.
    /// </para>
    /// </remarks>
    public static bool IsStorable(string entryName) =>
        !string.IsNullOrWhiteSpace(entryName)
        && !entryName.EndsWith('/')
        && !entryName.EndsWith('\\')
        && !string.IsNullOrWhiteSpace(FilingPaths.FileNameOf(entryName));

    /// <summary>
    /// Why the expansion of an entry that has just been read must abandon the whole archive,
    /// or null when it may carry on.
    /// </summary>
    /// <param name="totalUncompressedBytes">What the archive has expanded to so far, this entry included.</param>
    /// <param name="entryCompressedBytes">What this entry claimed to occupy in the archive.</param>
    /// <param name="entryUncompressedBytes">What it actually came to.</param>
    /// <remarks>
    /// Abandoning the archive rather than skipping the entry is deliberate. Both conditions
    /// mean the archive is not what it says it is, and an expansion that quietly dropped the
    /// hostile entries and reported success on the rest would leave somebody believing their
    /// archive had been filed.
    /// </remarks>
    public static string? AbandonReason(
        ArchiveLimits limits,
        long totalUncompressedBytes,
        long entryCompressedBytes,
        long entryUncompressedBytes)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (totalUncompressedBytes > limits.MaxTotalUncompressedBytes)
        {
            return TooLargeCode;
        }

        // A tiny compressed size is where the ratio test would misfire — a 40-byte entry
        // expanding to 8 KB is a 200:1 ratio and is also completely ordinary — so the test is
        // only applied once an entry is large enough for the ratio to mean anything.
        const long ratioFloorBytes = 64 * 1024;
        if (entryUncompressedBytes > ratioFloorBytes
            && entryCompressedBytes > 0
            && entryUncompressedBytes / entryCompressedBytes > limits.MaxCompressionRatio)
        {
            return BombCode;
        }

        return null;
    }

    /// <summary>Whether the entry about to be read would pass the count cap.</summary>
    public static bool WithinEntryCap(ArchiveLimits limits, int entriesSoFar)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return entriesSoFar < limits.MaxEntries;
    }
}
