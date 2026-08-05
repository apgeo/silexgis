// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.DocumentConversion"/> jobs.</summary>
public sealed record DocumentConversionPayload(Guid FileId);

/// <summary>
/// Turns an office-suite document into a portable one, stored beside the upload it came from,
/// so that the document has pages a reader would recognise and a picture of each can be drawn.
/// <para>
/// The upload is never touched. The converted copy joins the same revision rather than starting
/// a document of its own or superseding anything: it is the same content in another encoding,
/// produced by the server, with no author of its own to name — exactly as a cloud-optimized
/// copy of a raster is.
/// </para>
/// <para>
/// Every outcome is recorded as a state on the upload, and the three that are not the document's
/// fault are kept apart from each other as well as from the one that is. No converter deployed is
/// a standing fact about the installation; a deployed converter that did not answer is a fact
/// about one attempt; only a converter that answered and refused the bytes is a fact about the
/// document. Recording any of the first three as damage would tell people their file is broken
/// when it is not, which is the error this whole path exists to avoid.
/// </para>
/// </summary>
public sealed class DocumentConversionHandler(
    SilexGisDbContext db,
    DocumentWriteService documents,
    IFileStore fileStore,
    IDocumentConverter converter) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.DocumentConversion;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<DocumentConversionPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty document-conversion payload.");

        var upload = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == payload.FileId, ct);
        if (upload is null)
        {
            // The document was deleted while this waited. An ordinary outcome for queued work.
            return;
        }

        // Interrupted jobs are re-delivered on restart, so arriving at a file that already has
        // its copy has to cost one query and nothing else.
        if (await db.StoredFiles.AnyAsync(f => f.ConvertedFromFileId == upload.Id, ct))
        {
            await documents.RecordConversionOutcomeAsync(upload.Id, ConversionState.Converted, null, ct);
            return;
        }

        if (!converter.IsConfigured)
        {
            // The operator turned the converter off between the upload and now. Nothing is
            // wrong with the file, and saying so is the only honest answer available.
            await documents.RecordConversionOutcomeAsync(upload.Id, ConversionState.Unavailable, null, ct);
            return;
        }

        var workPath = Path.Combine(Path.GetTempPath(), $"silexgis-convert-{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var source = await fileStore.OpenReadAsync(upload.StoragePath, ct))
            await using (var work = File.Create(workPath))
            {
                await converter.ConvertToPortableAsync(source, upload.OriginalName, work, ct);
            }

            string storagePath;
            string sha256;
            await using (var produced = File.OpenRead(workPath))
            {
                storagePath = await fileStore.SaveAsync(produced, ".pdf", ct);
            }

            await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
            {
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
            }

            await documents.RecordConversionOutcomeAsync(
                upload.Id,
                ConversionState.Converted,
                new StoredContent(
                    storagePath,
                    Path.GetFileNameWithoutExtension(upload.OriginalName) + ".pdf",
                    ConvertibleFormats.TargetMediaType,
                    new FileInfo(fileStore.GetAbsolutePath(storagePath)).Length,
                    sha256,
                    FileKind.Document),
                ct);
        }
        catch (DocumentConversionException)
        {
            // The converter looked at the bytes and could not lay them out. That is a fact
            // about this document, so it is recorded as one and the job is not retried by
            // being thrown: asking the same service the same question again would answer
            // the same way.
            await documents.RecordConversionOutcomeAsync(upload.Id, ConversionState.Failed, null, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Unreachable service, exhausted patience, full disk, anything else: not the
            // document's fault, and not a statement about the deployment either. A converter
            // that is deployed and blinked is a different sentence from one that was never
            // deployed, so it gets a different state — otherwise a container restarting during
            // one upload would leave that document telling every reader, for good, that this
            // installation cannot lay out office documents while the next upload converts fine.
            // The job then fails loudly so the operator sees it in the queue, where a problem
            // with the installation belongs, and the conversion sweep picks the file up again.
            await documents.RecordConversionOutcomeAsync(upload.Id, ConversionState.Deferred, null, ct);
            throw;
        }
        finally
        {
            if (File.Exists(workPath))
            {
                File.Delete(workPath);
            }
        }
    }
}
