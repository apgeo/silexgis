// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>What archive to expand, and into which batch.</summary>
public sealed record ArchiveExpansionPayload(Guid FileId, Guid BatchId);

/// <summary>
/// Expands an uploaded archive into filed documents, mirroring its folders as cabinets.
///
/// <para>
/// This is how a club's existing archive arrives: one upload of a few gigabytes rather than
/// four hundred drag-and-drops, with the folder scheme somebody spent years maintaining
/// preserved rather than flattened.
/// </para>
/// <para>
/// It is also the most hostile input this application takes, because the whole structure —
/// entry names, declared sizes, nesting — is written by somebody else and read by the server
/// onto its own disk. Every ceiling and every refusal is decided by the pure rules, which are
/// tested at their edges; this handler does the reading and stops when it is told to. Two
/// failures are told apart deliberately: an entry that will not read is recorded and stepped
/// over, and an archive that is not what it claims to be abandons the whole run with the
/// reason on the batch.
/// </para>
/// </summary>
public sealed class ArchiveExpansionHandler(
    SilexGisDbContext db,
    BulkIngestRunner runner,
    UploadBatchService batches,
    IFileStore fileStore,
    IOptions<FilesOptions> options) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.ArchiveExpansion;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        var payload = JsonSerializer.Deserialize<ArchiveExpansionPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Archive expansion payload is not readable.");

        var batch = await db.UploadBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == payload.BatchId, ct);
        if (batch is null)
        {
            return; // the batch was pruned; there is nothing to report into
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == payload.FileId, ct);

        // Rebuilt for the person who asked, rather than carried from the request that queued
        // this: a queued walk must grant exactly what its requester holds now, and the request
        // itself is long gone by the time this runs.
        var ctx = file is null
            ? null
            : await AccessContextResolver.ResolveAsync(db, batch.StartedByUserId, ct);
        if (file is null || ctx is null)
        {
            await batches.CompleteAsync(batch.Id, ArchiveExpansionRules.UnreadableCode, ct);
            return;
        }

        var guard = new ExpansionGuard(
            new ArchiveLimits(
                options.Value.MaxArchiveEntries,
                options.Value.MaxArchiveExpandedBytes,
                options.Value.MaxArchiveCompressionRatio),
            options.Value.MaxUploadBytes);

        try
        {
            await runner.RunAsync(batch, ctx, ReadEntriesAsync(file.StoragePath, guard, ct), ct);
        }
        catch (InvalidDataException)
        {
            // Not a readable archive at all. The upload itself is untouched and stays where
            // it is — the person can look at it and see what they sent.
            await batches.CompleteAsync(batch.Id, ArchiveExpansionRules.UnreadableCode, ct);
            return;
        }

        // Whatever was filed before a cap was passed stays filed and is listed on the batch —
        // undoing it would be a second, larger surprise — and the batch says why it stopped.
        await batches.CompleteAsync(batch.Id, guard.AbandonedFor, ct);
    }

    /// <summary>
    /// The archive's storable entries, one at a time, each opened only when it is reached and
    /// stopping the moment a cap is passed.
    /// </summary>
    /// <remarks>
    /// The declared length is what the limit checks before the read have to use, because it is
    /// the only figure available then; what actually came out is measured while it is read and
    /// checked here, after the entry has been consumed and its stream closed. Trusting the
    /// declaration alone is precisely how a decompression bomb gets in — it declares whatever
    /// it likes.
    /// <para>
    /// Abandoning by ending the sequence rather than by throwing is deliberate: the consumer
    /// wraps each file in a try/catch so that one bad file does not stop four hundred good
    /// ones, and an exception raised from inside a file's own stream would be caught by
    /// exactly that guard and reported as "this one file was unreadable".
    /// </para>
    /// </remarks>
    private async IAsyncEnumerable<BulkSourceFile> ReadEntriesAsync(
        string storagePath,
        ExpansionGuard guard,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var content = await fileStore.OpenReadAsync(storagePath, ct);
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (guard.AbandonedFor is not null)
            {
                yield break;
            }

            if (!ArchiveExpansionRules.IsStorable(entry.FullName))
            {
                continue;
            }

            if (!ArchiveExpansionRules.WithinEntryCap(guard.Limits, guard.EntryCount))
            {
                guard.Abandon(ArchiveExpansionRules.TooManyEntriesCode);
                yield break;
            }

            guard.CountEntry();

            var captured = entry;
            yield return new BulkSourceFile(
                captured.FullName,
                captured.Length,
                _ => Task.FromResult<Stream>(new MeasuringStream(
                    captured.Open(),
                    guard.CapFor(captured.Length),
                    read => guard.AfterEntry(captured.CompressedLength, read),
                    guard.OverflowedCap)));
        }
    }

    /// <summary>The running totals the caps are measured against.</summary>
    private sealed class ExpansionGuard(ArchiveLimits limits, long maxUploadBytes)
    {
        private long expandedBytes;

        public ArchiveLimits Limits { get; } = limits;

        public int EntryCount { get; private set; }

        /// <summary>Why the run stopped, or null while it may carry on.</summary>
        public string? AbandonedFor { get; private set; }

        public void CountEntry() => EntryCount++;

        public void Abandon(string code) => AbandonedFor ??= code;

        /// <summary>
        /// The most this entry may be allowed to produce before the read is cut off.
        /// </summary>
        /// <remarks>
        /// This is the check that actually protects the disk, and it has to happen while the
        /// bytes are moving rather than after. The expanded content is written into the file
        /// store as it is read, so a rule applied once the entry finished would already have
        /// let a single entry declaring 4 KB and expanding to 40 GB write all forty of them.
        /// <para>
        /// Three ceilings, whichever is tightest: what is left of the whole archive's budget,
        /// the largest single file this installation takes, and the entry's own declared size.
        /// The third is what makes an entry that lies about itself detectable at all — an
        /// entry expanding past its own declaration is malformed whatever the other two allow.
        /// </para>
        /// </remarks>
        public long CapFor(long declaredLength)
        {
            var cap = Math.Min(
                maxUploadBytes,
                Math.Max(0, Limits.MaxTotalUncompressedBytes - expandedBytes));

            return declaredLength > 0 ? Math.Min(cap, declaredLength) : cap;
        }

        /// <summary>
        /// The read was cut off at its cap: the archive produced more than it may. Recorded
        /// before the exception is raised, so that the entry's own failure — which the walk
        /// catches and steps over — is followed by the whole run stopping.
        /// </summary>
        public void OverflowedCap() => Abandon(TooLargeOrBomb());

        /// <summary>Called once an entry's stream has been read and closed.</summary>
        public void AfterEntry(long compressedBytes, long uncompressedBytes)
        {
            expandedBytes += uncompressedBytes;
            if (ArchiveExpansionRules.AbandonReason(
                    Limits, expandedBytes, compressedBytes, uncompressedBytes) is { } reason)
            {
                Abandon(reason);
            }
        }

        /// <summary>
        /// Which of the two the cut-off was. An entry that overran while the archive still had
        /// budget left overran its own declaration, which is the bomb shape; one that overran
        /// with the budget spent is simply an archive too large for this installation.
        /// </summary>
        private string TooLargeOrBomb() =>
            expandedBytes >= Limits.MaxTotalUncompressedBytes
                ? ArchiveExpansionRules.TooLargeCode
                : ArchiveExpansionRules.BombCode;
    }

    /// <summary>An entry produced more bytes than it was allowed to.</summary>
    private sealed class EntryOverflowException() : IOException("The archive entry exceeded its allowance.");

    /// <summary>
    /// Counts what passes through it, cuts the read off at a cap, and reports the total when
    /// it closes. Closing happens whether the read finished or failed, which is what makes the
    /// count reliable.
    /// </summary>
    private sealed class MeasuringStream(
        Stream inner, long maxBytes, Action<long> onClosed, Action onOverflow) : Stream
    {
        private long bytesRead;
        private bool closed;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            return Count(read);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await inner.ReadAsync(buffer, ct);
            return Count(read);
        }

        /// <summary>
        /// Adds to the running total and refuses to go past the cap. Throwing mid-read is the
        /// point: the copy into the file store stops here, so the bytes past the cap are never
        /// written at all.
        /// </summary>
        private int Count(int read)
        {
            bytesRead += read;
            if (bytesRead <= maxBytes)
            {
                return read;
            }

            // Recorded before the exception is raised. The walk catches the exception and
            // records this one entry as failed; the run itself stops because the reason is
            // already on the guard when the enumerator is asked for the next entry.
            onOverflow();
            throw new EntryOverflowException();
        }

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !closed)
            {
                closed = true;
                inner.Dispose();
                onClosed(bytesRead);
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!closed)
            {
                closed = true;
                await inner.DisposeAsync();
                onClosed(bytesRead);
            }

            await base.DisposeAsync();
        }
    }
}
