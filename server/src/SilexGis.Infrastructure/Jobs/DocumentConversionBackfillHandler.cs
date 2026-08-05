// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Finds office documents that have no portable copy and could have one, and queues a
/// conversion for each.
/// <para>
/// An upload queues its own conversion, so this exists for the cases an upload cannot cover.
/// The first is an installation that deployed a converter after the fact: every document stored
/// before that is recorded as one nothing here can lay out, and that sentence is shown to
/// whoever opens it — nothing else would ever revisit it. The second is a document whose own
/// attempt did not finish, because the converter was restarting or the disk was full; the
/// conversion job is not retried on its own, deliberately, so that a failure is visible rather
/// than looping, and this is how it is picked up once the cause is gone.
/// </para>
/// <para>
/// Safe to run as often as anyone likes. A document that already has a copy is skipped here, and
/// the conversion itself checks again before doing any work.
/// </para>
/// </summary>
public sealed class DocumentConversionBackfillHandler(
    SilexGisDbContext db,
    IDocumentConverter converter) : IProcessingJobHandler
{
    private const int BatchSize = 200;

    public string Kind => ProcessingJobKinds.DocumentConversionBackfill;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        if (!converter.IsConfigured)
        {
            // Queuing conversions no one can perform would replace every state with the same
            // one it already holds, at the cost of a job per document.
            return;
        }

        // Which formats can be converted is a fact about formats and lives with them, so the
        // query asks the state column and the list is applied here — restating it in SQL would
        // be a second copy that disagrees the first time one changes. A file that is itself a
        // converted copy is never converted again.
        var candidates = await db.StoredFiles.AsNoTracking()
            .Where(f => f.ConvertedFromFileId == null
                && f.Conversion != ConversionState.NotApplicable
                && f.Conversion != ConversionState.Converted
                && !db.StoredFiles.Any(c => c.ConvertedFromFileId == f.Id))
            .Select(f => new { f.Id, f.MimeType })
            .ToListAsync(ct);

        var due = candidates
            .Where(c => ConvertibleFormats.CanConvert(c.MimeType))
            .Select(c => c.Id)
            .ToList();

        foreach (var chunk in due.Chunk(BatchSize))
        {
            var files = await db.StoredFiles.Where(f => chunk.Contains(f.Id)).ToListAsync(ct);
            foreach (var file in files)
            {
                file.Conversion = ConversionState.Pending;
                db.ProcessingJobs.Add(new ProcessingJob
                {
                    Kind = ProcessingJobKinds.DocumentConversion,
                    Payload = JsonSerializer.Serialize(
                        new DocumentConversionPayload(file.Id), JsonSerializerOptions.Web),

                    // Deliberately nobody, as at upload: the queue mails its requester on every
                    // outcome, and a sweep over an archive is not a request for one mail per
                    // document in it.
                    RequestedBy = null,
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
