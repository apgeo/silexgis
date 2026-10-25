// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Surveys;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>What reading a TopoDroid survey archive found.</summary>
/// <param name="Manifest">The archive's manifest.</param>
/// <param name="Survey">What the survey dump says about the survey as a whole.</param>
/// <param name="EntryCount">How many entries the archive holds, read or not.</param>
public sealed record TopoDroidArchiveReading(TopoDroidManifest Manifest, TopoDroidSurveyFacts Survey, int EntryCount);

/// <summary>
/// Reads a TopoDroid survey archive a field device sent: its manifest, and the survey row and
/// fixed stations of its SQL dump — and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The archive is somebody's phone's output and is read as untrusted. Every refusal happens before
/// anything is inflated wherever the archive's own directory can answer: the entry count, a name
/// that climbs out of the archive, a declared total over this installation's cap, an entry whose
/// declared expansion is the shape of a bomb. Only the two entries that are read are ever
/// inflated, each through a counter that cuts the read off at the smallest of its declared size,
/// its own ceiling and what is left of the budget — because a declaration is whatever the archive
/// says it is, and an entry that expands past its own is a crafted one. Nothing is written to disk
/// under an entry's name; the photos, sketches and notes stay inside the archive, unread.
/// </para>
/// <para>
/// The dump is SQL text read by a line scanner. No database engine is run over it: an engine
/// opened on a file a phone sent is an engine whose every parser path the phone gets to drive.
/// </para>
/// </remarks>
public static class TopoDroidArchiveReader
{
    /// <summary>
    /// The compression ratio past which an entry large enough for the ratio to mean anything is
    /// taken for a bomb. A survey's own text compresses by a factor of ten or so, photos not at all.
    /// </summary>
    private const int MaxCompressionRatio = 200;

    /// <summary>Reads the archive, or throws a <see cref="SurveyPipelineException"/> naming why it cannot be read.</summary>
    /// <param name="archive">The archive's bytes, seekable. It is read only, and left open.</param>
    /// <param name="maxUnpackedBytes">The most the whole archive may declare it unpacks to.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="maxEntries">The most entries the archive may hold.</param>
    public static async Task<TopoDroidArchiveReading> ReadAsync(
        Stream archive,
        long maxUnpackedBytes,
        CancellationToken ct,
        int maxEntries = TopoDroidArchive.MaxEntries)
    {
        ArgumentNullException.ThrowIfNull(archive);

        try
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            var entries = zip.Entries;

            if (entries.Count > maxEntries)
            {
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.ArchiveTooLarge,
                    $"The survey archive holds more than {maxEntries} entries.");
            }

            if (entries.Count == 0 || !entries.Any(e => e.FullName == TopoDroidArchive.ManifestEntry))
            {
                throw Invalid("The survey archive has no manifest; it was not written by TopoDroid's survey export.");
            }

            if (entries[0].FullName != TopoDroidArchive.ManifestEntry)
            {
                throw Invalid("The survey archive's first entry is not its manifest.");
            }

            var limits = new ArchiveLimits(maxEntries, maxUnpackedBytes, MaxCompressionRatio);
            var declared = 0L;
            foreach (var entry in entries)
            {
                if (!TopoDroidArchive.IsSafeEntryName(entry.FullName))
                {
                    throw Invalid("The survey archive names an entry outside itself.");
                }

                declared += entry.Length;
                if (ArchiveExpansionRules.AbandonReason(limits, declared, entry.CompressedLength, entry.Length) is not null)
                {
                    throw TooLarge();
                }
            }

            var dump = entries.FirstOrDefault(e => e.FullName == "survey.sql")
                ?? entries.FirstOrDefault(e => TopoDroidArchive.IsDump(e.FullName))
                ?? throw Invalid("The survey archive has no survey dump.");

            var budget = new Budget(maxUnpackedBytes);
            var manifestText = await ReadManifestTextAsync(entries[0], budget, ct);
            var manifest = TopoDroidArchive.ReadManifest(manifestText, out var problem)
                ?? throw Invalid(problem ?? "The survey archive's manifest cannot be read.");

            var facts = TopoDroidArchive.ReadDump(await ReadDumpLinesAsync(dump, budget, ct))
                ?? throw Invalid("The survey archive's dump holds no survey.");

            return new TopoDroidArchiveReading(manifest, facts, entries.Count);
        }
        catch (InvalidDataException)
        {
            throw Invalid("The survey archive is not a ZIP archive that can be read.");
        }
        catch (NotSupportedException)
        {
            // An encrypted entry, or a compression method the runtime does not inflate.
            throw Invalid("The survey archive uses a ZIP feature this server does not read.");
        }
        catch (EntryOverflowException)
        {
            throw TooLarge();
        }
    }

    private static async Task<string> ReadManifestTextAsync(ZipArchiveEntry entry, Budget budget, CancellationToken ct)
    {
        if (entry.Length > TopoDroidArchive.MaxManifestBytes)
        {
            throw Invalid("The survey archive's manifest is not a TopoDroid manifest.");
        }

        await using var stream = budget.Open(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// The dump's lines, read while it inflates: the scanner keeps the two kinds of row it needs
    /// and lets every other line go, so a large dump is never held whole.
    /// </summary>
    private static async Task<IEnumerable<string>> ReadDumpLinesAsync(ZipArchiveEntry entry, Budget budget, CancellationToken ct)
    {
        if (entry.Length > TopoDroidArchive.MaxDumpBytes)
        {
            throw TooLarge();
        }

        var kept = new List<string>();
        await using var stream = budget.Open(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Contains(" surveys ", StringComparison.OrdinalIgnoreCase)
                || line.Contains(" fixeds ", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(line);
            }
        }

        return kept;
    }

    private static SurveyPipelineException Invalid(string message) =>
        new(SurveyPipelineFailures.ArchiveInvalid, message);

    private static SurveyPipelineException TooLarge() =>
        new(SurveyPipelineFailures.ArchiveTooLarge, "The survey archive unpacks to more than this server accepts.");

    /// <summary>What is left of the archive's unpacking budget, spent as entries are actually read.</summary>
    private sealed class Budget(long total)
    {
        private long spent;

        /// <summary>
        /// Opens an entry behind a counter. Its ceiling is the smaller of what it declares and what
        /// is left: an entry that produces more than it declared is lying about itself, and one that
        /// produces more than is left is more than the archive was allowed.
        /// </summary>
        public Stream Open(ZipArchiveEntry entry)
        {
            var left = Math.Max(0, total - spent);
            var ceiling = Math.Min(left, entry.Length);
            return new CappedStream(entry.Open(), ceiling, read => spent += read);
        }
    }

    /// <summary>An entry produced more bytes than it was allowed.</summary>
    private sealed class EntryOverflowException() : IOException("The archive entry exceeded its allowance.");

    /// <summary>Counts what passes through it and stops the read the moment it passes its ceiling.</summary>
    private sealed class CappedStream(Stream inner, long ceiling, Action<long> onClosed) : Stream
    {
        private long read;
        private bool closed;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Count(await inner.ReadAsync(buffer, ct));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            Count(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));

        private int Count(int bytes)
        {
            read += bytes;
            return read <= ceiling ? bytes : throw new EntryOverflowException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !closed)
            {
                closed = true;
                inner.Dispose();
                onClosed(read);
            }

            base.Dispose(disposing);
        }
    }
}
