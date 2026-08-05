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

    /// <summary>
    /// Most pages of one document the language sample will look at. The sample is bounded in
    /// characters, and that alone is enough for anything written as prose; this is the bound for
    /// the shape it cannot reach — thousands of pages holding a word or two each, a scanned
    /// register or a table of survey figures — where the character budget would be spent one row
    /// at a time. Well past the point where the answer stops changing.
    /// </summary>
    private const int MaxSamplePages = 200;

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

        await StampLanguagesAsync(ct);
    }

    /// <summary>
    /// Gives a language to documents that were read before anything here detected one.
    /// </summary>
    /// <remarks>
    /// Reading the file again is not needed and would be the expensive way round: the words are
    /// already stored as page text, which is the same text the detector would be handed. This
    /// is also the only route by which an installation that read its archive before this
    /// existed gets stemming at all — the reading sweep skips a file that is already read by
    /// the reader registered now, so hanging detection off re-reading alone would leave every
    /// such document unstemmed for good.
    /// <para>
    /// Only documents with no language are considered, so a correction somebody made is never
    /// undone by a later sweep. Writing the code re-derives every page vector under the
    /// document, which the database does by itself; that is one page update per document, which
    /// is why documents are taken in batches rather than all at once.
    /// </para>
    /// <para>
    /// The text itself is fetched in two steps, and that is the point of the second query. A
    /// page may hold two million characters and the detector reads forty thousand, so asking
    /// for whole pages would move up to fifty times more text across the wire than anything
    /// looks at — and this runs inside the process serving requests, over as many documents as
    /// the archive holds. The first query asks only how long each page is, which moves no text
    /// at all; the second asks for exactly the pages that fit in the sample, cut to the sample
    /// length by the database. What a batch can hold is then bounded by the batch size times
    /// the sample, whatever the archive is made of.
    /// </para>
    /// </remarks>
    private async Task StampLanguagesAsync(CancellationToken ct)
    {
        var unstated = await db.Documents.AsNoTracking()
            .Where(d => d.Language == null)
            .Select(d => d.Id)
            .ToListAsync(ct);

        foreach (var chunk in unstated.Chunk(BatchSize))
        {
            var sample = await SamplePagesAsync(chunk, ct);
            if (sample.Count == 0)
            {
                continue;
            }

            var ids = sample.Keys.ToList();
            var documents = await db.Documents
                .Where(d => ids.Contains(d.Id) && d.Language == null)
                .ToListAsync(ct);

            foreach (var document in documents)
            {
                document.Language = LanguageDetection.Detect(sample[document.Id]);
            }

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The opening of each document's text, in reading order, cut to what the detector reads.
    /// </summary>
    private async Task<Dictionary<Guid, List<string?>>> SamplePagesAsync(
        Guid[] documentIds, CancellationToken ct)
    {
        // Ordered by page so the detector reads a document's opening rather than an arbitrary
        // slice of it, and taken across every revision because a document's language is a fact
        // about the document, not about whichever file is current. Only the length of each page
        // is asked for here — enough to work out which pages the sample reaches without moving
        // a single character of them.
        var lengths = await db.DocumentPages.AsNoTracking()
            .Where(p => p.Text != null)
            .Join(db.StoredFiles.AsNoTracking(), p => p.FileId, f => f.Id, (p, f) => new { p, f })
            .Join(
                db.DocumentVersions.AsNoTracking(),
                x => x.f.DocumentVersionId,
                v => v.Id,
                (x, v) => new
                {
                    v.DocumentId,
                    v.VersionNumber,
                    x.p.PageNumber,
                    PageId = x.p.Id,
                    Length = x.p.Text!.Length,
                })
            .Where(x => documentIds.Contains(x.DocumentId))
            .OrderBy(x => x.DocumentId).ThenBy(x => x.VersionNumber).ThenBy(x => x.PageNumber)
            .ToListAsync(ct);

        var order = new Dictionary<Guid, List<long>>();
        var wanted = new List<long>();
        foreach (var document in lengths.GroupBy(x => x.DocumentId))
        {
            var remaining = LanguageDetection.SampleCharacters;
            var pages = new List<long>();
            foreach (var page in document)
            {
                if (remaining <= 0 || pages.Count >= MaxSamplePages)
                {
                    break;
                }

                pages.Add(page.PageId);
                remaining -= page.Length;
            }

            order[document.Key] = pages;
            wanted.AddRange(pages);
        }

        if (wanted.Count == 0)
        {
            return [];
        }

        // Cut by the database rather than after it arrives: a single page may be fifty times
        // the sample on its own, and the detector would throw the rest away.
        var text = await db.DocumentPages.AsNoTracking()
            .Where(p => wanted.Contains(p.Id))
            .Select(p => new { p.Id, Text = p.Text!.Substring(0, LanguageDetection.SampleCharacters) })
            .ToDictionaryAsync(x => x.Id, x => x.Text, ct);

        return order.ToDictionary(
            d => d.Key,
            d => d.Value.Select(id => text.GetValueOrDefault(id)).ToList());
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
