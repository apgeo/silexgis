// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents.Extraction;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Finds stored files whose text this installation has not read, and queues one reading each.
/// <para>
/// An upload queues its own reading, so this exists for the two cases an upload cannot cover.
/// The first is a file that was already there: an installation that stored documents before
/// anything here could read them holds rows saying, wrongly, that the format carries no text —
/// and that sentence is shown to whoever opens the document. The second is a reader that has
/// improved: page text records which reader and which version produced it precisely so that a
/// better reading is a decidable question, and without something that asks the question the
/// answer is never acted on.
/// </para>
/// <para>
/// Safe to run as often as anyone likes. It queues; the reading itself decides whether there is
/// work to do, and a file already read by the reader registered now costs that reading one query.
/// </para>
/// </summary>
public sealed class TextExtractionBackfillHandler(
    SilexGisDbContext db,
    TextExtractorSelector selector) : IProcessingJobHandler
{
    private const int BatchSize = 200;

    public string Kind => ProcessingJobKinds.TextExtractionBackfill;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        // Both passes ask the state column first — the two questions the sweep exists to answer
        // are "what has never been read" and "what was read by something older", and the state
        // is what separates them. Which formats carry text is then applied here rather than in
        // the query: that list is a fact about formats and lives with them, and restating it in
        // SQL would mean two copies that disagree the first time one changes.
        var neverRead = await ReadableAsync(
            f => f.TextExtraction != TextExtractionState.Extracted
                && f.TextExtraction != TextExtractionState.NoText,
            ct);

        var alreadyRead = await ReadableAsync(
            f => f.TextExtraction == TextExtractionState.Extracted
                || f.TextExtraction == TextExtractionState.NoText,
            ct);

        var stale = await StaleFileIdsAsync(alreadyRead, ct);
        var due = neverRead.Concat(stale).ToList();

        foreach (var chunk in due.Chunk(BatchSize))
        {
            var files = await db.StoredFiles.Where(f => chunk.Contains(f.Id)).ToListAsync(ct);
            foreach (var file in files)
            {
                // A file waiting to be read says so. A file that has already been read keeps
                // its answer until a better reading has actually produced one: "there is
                // nothing in this document" is a truer sentence meanwhile than "reading it".
                if (file.TextExtraction is not (TextExtractionState.Extracted or TextExtractionState.NoText))
                {
                    file.TextExtraction = TextExtractionState.Pending;
                }

                db.ProcessingJobs.Add(new ProcessingJob
                {
                    Kind = ProcessingJobKinds.TextExtraction,
                    Payload = JsonSerializer.Serialize(
                        new TextExtractionPayload(file.Id), JsonSerializerOptions.Web),

                    // Deliberately nobody, as at upload: the queue mails its requester on every
                    // outcome, and a sweep over an archive is not a request for one mail per
                    // document in it.
                    RequestedBy = null,
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The files matching a state test whose recorded format is one that carries text.
    /// </summary>
    private async Task<List<Guid>> ReadableAsync(
        Expression<Func<StoredFile, bool>> state, CancellationToken ct)
    {
        var rows = await db.StoredFiles.AsNoTracking()
            .Where(state)
            .Select(f => new { f.Id, f.MimeType })
            .ToListAsync(ct);

        return rows
            .Where(r => TextExtractionFormats.CarriesText(r.MimeType))
            .Select(r => r.Id)
            .ToList();
    }

    /// <summary>
    /// Which of the already-read files carry text the readers registered now would not have
    /// produced — a page stamped by an older version, by a reader since withdrawn, or by
    /// nothing at all, and a file recorded as read that holds no pages to have been read.
    /// </summary>
    private async Task<List<Guid>> StaleFileIdsAsync(List<Guid> fileIds, CancellationToken ct)
    {
        var stale = new List<Guid>();
        foreach (var chunk in fileIds.Chunk(BatchSize))
        {
            var stamps = await db.DocumentPages.AsNoTracking()
                .Where(p => chunk.Contains(p.FileId))
                .Select(p => new { p.FileId, p.Extractor, p.ExtractorVersion })
                .Distinct()
                .ToListAsync(ct);

            var byFile = stamps.ToLookup(s => s.FileId);
            foreach (var id in chunk)
            {
                var pages = byFile[id].ToList();
                if (pages.Count == 0
                    || pages.Exists(s => !selector.IsCurrent(s.Extractor, s.ExtractorVersion)))
                {
                    stale.Add(id);
                }
            }
        }

        return stale;
    }
}
