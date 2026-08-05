// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Documents.Extraction;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.TextExtraction"/> jobs.</summary>
/// <param name="FileId">The stored file to read. Everything else is looked up from the row.</param>
public sealed record TextExtractionPayload(Guid FileId);

/// <summary>
/// Reads a stored file's text layer into its page rows.
/// <para>
/// The upload is never touched. What this produces is derived data hanging off the file — page
/// rows, a page count, and a state saying how the reading went — so the stored bytes, their
/// hash and their recorded format are exactly what was received, and a reading may be run again
/// as many times as it likes without the original having moved.
/// </para>
/// <para>
/// Which reader is used comes from the format decided from the file's own bytes when it was
/// stored, never from what the upload claimed to be: a reader handed a format it cannot read
/// produces nothing, and nothing downstream could tell that apart from a file that genuinely
/// has no text in it.
/// </para>
/// <para>
/// Safe to run again, and safe to run again after being interrupted. A file whose pages already
/// all carry this reader at this version is left alone and costs one query — which is what lets
/// the queue re-deliver a job (it does on every worker restart, for anything that was running)
/// without the work being done twice. When the reader or its version has moved on, or the
/// previous run got partway, the file is read and its rows updated in place: nothing that points
/// at a page loses its target, and no page is duplicated.
/// </para>
/// </summary>
public sealed class TextExtractionHandler(
    SilexGisDbContext db,
    DocumentWriteService documents,
    IFileStore fileStore,
    TextExtractorSelector selector) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.TextExtraction;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        var payload = JsonSerializer.Deserialize<TextExtractionPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty text-extraction payload.");

        var file = await db.StoredFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == payload.FileId, ct);
        if (file is null)
        {
            // A file deleted between being queued and being read is an ordinary outcome, not a
            // failure: the job describes work there is no longer any subject for.
            return;
        }

        var extractor = selector.For(file.MimeType);
        if (extractor is null)
        {
            // The format was judged worth reading where the file was stored, and nothing here
            // knows how. Recorded as its own state rather than as a failure, because nothing is
            // wrong with the file — the gap is on this side, and a reader added later will
            // find these rows waiting.
            await documents.RecordTextExtractionOutcomeAsync(
                file.Id, TextExtractionState.Unsupported, null, ct);
            return;
        }

        if (await AlreadyReadAsync(file, extractor, ct))
        {
            return;
        }

        try
        {
            await using var content = await fileStore.OpenReadAsync(file.StoragePath, ct);
            var extracted = await extractor.ExtractAsync(content, ct);
            await documents.RecordPageTextAsync(
                file.Id, extracted.Extractor, extracted.Version, extracted.Pages, ct);
        }
        catch (NotSupportedException)
        {
            // The reader opened the file, recognised it, and does not implement that variant —
            // a legacy word-processor document inside a container it shares with a spreadsheet
            // it can read. Nothing is wrong with the file and re-reading it will not help until
            // something here can, which is the same answer as finding no reader at all.
            await documents.RecordTextExtractionOutcomeAsync(
                file.Id, TextExtractionState.Unsupported, null, CancellationToken.None);
        }
        catch (ProtectedContentException)
        {
            // Locked rather than broken, and recorded as a failure because that is what the
            // states say a protected file is: something worth trying again, since the answer
            // changes the day an unlocked copy is uploaded over it. Not rethrown — the refusal
            // is deliberate and complete, and re-delivering the job would only repeat it.
            await documents.RecordTextExtractionOutcomeAsync(
                file.Id,
                TextExtractionState.Failed,
                "The file is password-protected, so its text cannot be read.",
                CancellationToken.None);
        }
        catch (ContentTooLargeException)
        {
            // Nothing is wrong with the file, so it is not called damaged; it simply could not
            // be held. Not rethrown, because the queue would deliver it again and the answer
            // cannot change until the reading itself does.
            await documents.RecordTextExtractionOutcomeAsync(
                file.Id,
                TextExtractionState.Failed,
                "The file is too large for its text to be read here.",
                CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The message is bounded and generic on purpose: it is shown to whoever has to
            // explain a document that will not open, and a parser's internal complaint about a
            // byte offset tells them nothing while being long enough to overflow the column.
            await documents.RecordTextExtractionOutcomeAsync(
                file.Id,
                TextExtractionState.Failed,
                "The file could not be read: its content does not match the format it was stored as, or it is damaged or protected.",
                CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Whether this exact reader at this exact version has already read every page of the file.
    /// Asked before the bytes are opened, so a re-delivered job is one query rather than a
    /// second reading, and a run that was interrupted partway is not mistaken for a finished one.
    /// </summary>
    private async Task<bool> AlreadyReadAsync(StoredFile file, ITextExtractor extractor, CancellationToken ct)
    {
        if (file.TextExtraction is not (TextExtractionState.Extracted or TextExtractionState.NoText)
            || file.PageCount is not { } expected)
        {
            return false;
        }

        var current = await db.DocumentPages
            .CountAsync(
                p => p.FileId == file.Id
                    && p.Extractor == extractor.Name
                    && p.ExtractorVersion == extractor.Version,
                ct);

        // Equality both ways: fewer stamped rows than the count means the last run stopped
        // partway or a newer reader has since arrived, and more means the file shrank and the
        // surplus has still to be cleared.
        var total = await db.DocumentPages.CountAsync(p => p.FileId == file.Id, ct);
        return current == expected && total == expected;
    }
}
