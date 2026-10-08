// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.SurveyMesh"/> jobs.</summary>
public sealed record SurveyMeshPayload(Guid SurveyModelId);

/// <summary>
/// Turns an uploaded wall mesh into the one file the 3D scene draws, and records where it belongs.
///
/// <para>
/// Off the request thread because the largest real export measured takes about a second to convert
/// and a hundred megabytes of memory to do it in — fine for a background worker, not for a request
/// someone is waiting on with a phone in their hand.
/// </para>
///
/// <para>
/// Running it again replaces what the previous conversion produced. A stored file may have one
/// file derived from it and no more, so a second conversion that only added its result would be
/// refused at the save; the earlier mesh is taken out in the same transaction that puts the new
/// one in, and a conversion that fails leaves the model drawing the mesh it had.
/// </para>
/// </summary>
public sealed class SurveyMeshHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    DocumentWriteService documents,
    SurveyMeshConverter converter,
    ILogger<SurveyMeshHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.SurveyMesh;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SurveyMeshPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty survey-mesh payload.");

        var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == payload.SurveyModelId, ct)
            ?? throw new InvalidOperationException($"Survey model {payload.SurveyModelId} no longer exists.");
        var upload = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == model.FileId, ct)
            ?? throw new InvalidOperationException($"Stored file {model.FileId} no longer exists.");

        // A model that is ready has a mesh already, and this is another conversion of the same
        // file. It goes on saying it is ready, because it goes on being drawable: the mesh it has
        // stays until the transaction below replaces it. Only a model with nothing to draw is
        // marked as being worked on.
        var holdsAMesh = model.Status == SurveyModelStatus.Ready;
        if (!holdsAMesh)
        {
            model.Status = SurveyModelStatus.Processing;
            await db.SaveChangesAsync(ct);
        }

        // The declaration the uploader made, read back off the row rather than carried in the job
        // payload: a job that is retried days later must convert the file the same way, and the row
        // is the only copy of that answer which survives an edit.
        var declaration = new SurveySourceDeclaration(
            model.SourceEpsg,
            model.Anchor?.X,
            model.Anchor?.Y,
            model.AnchorHeightM ?? 0);

        var workPath = Path.Combine(Path.GetTempPath(), $"silexgis-mesh-{Guid.NewGuid():N}.glb");

        // Where this conversion's bytes went, once they are in the file store: a conversion that
        // then fails to be recorded has to be able to take them back out.
        string? stored = null;
        try
        {
            MeshConversionResult conversion;
            await using (var input = await fileStore.OpenReadAsync(upload.StoragePath, ct))
            await using (var work = File.Create(workPath))
            {
                conversion = converter.Convert(input, declaration, work);
            }

            string storagePath;
            await using (var produced = File.OpenRead(workPath))
            {
                storagePath = await fileStore.SaveAsync(produced, ".glb", ct);
            }

            stored = storagePath;

            string sha256;
            await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
            {
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
            }

            // Taking out the mesh of an earlier conversion and putting this one in are one change
            // to the model, so they are one transaction: undone together, the model still names
            // the mesh it had. Two saves inside it and not one, so that the earlier derived file
            // is gone before the new one claims the upload it was derived from.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            string? replaced = null;
            var previous = await db.StoredFiles.FirstOrDefaultAsync(f => f.ConvertedFromFileId == upload.Id, ct);
            if (previous is not null)
            {
                replaced = previous.StoragePath;
                model.ConvertedFileId = null;
                db.StoredFiles.Remove(previous);
                await db.SaveChangesAsync(ct);
            }

            // The mesh is another encoding of the file that was uploaded, not a new revision of
            // it, so it joins the upload's own revision as a derived file and says which file it
            // was derived from. Recorded through the document write service because that is the
            // single place that knows what a stored file has to carry — and because it is what
            // decides, from the format, that a binary mesh has no text to read and no page to
            // draw, so neither is queued for it.
            var converted = documents.AddFile(
                upload.DocumentVersionId,
                new StoredContent(
                    storagePath,
                    Path.GetFileNameWithoutExtension(upload.OriginalName) + ".glb",
                    "model/gltf-binary",
                    new FileInfo(fileStore.GetAbsolutePath(storagePath)).Length,
                    sha256,
                    FileKind.Survey),
                convertedFromFileId: upload.Id);

            model.ConvertedFileId = converted.Id;
            model.Anchor = new Point(conversion.AnchorLongitude, conversion.AnchorLatitude) { SRID = 4326 };
            model.AnchorHeightM = conversion.AnchorHeightM;
            model.AppliedRotationDeg = conversion.AppliedRotationDeg;
            model.TriangleCount = conversion.TrianglesRead - conversion.DegenerateDropped;
            model.SourcePrecisionLost = conversion.SourcePrecisionLost;
            model.Status = SurveyModelStatus.Ready;
            model.ProcessingError = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Bytes are dropped only once the row that named them is gone for good.
            stored = null;
            await DiscardAsync(replaced, model.Id);
        }
        catch (Exception e)
        {
            // Nothing recorded this conversion's bytes, so they belong to nothing.
            await DiscardAsync(stored, model.Id);

            // Everything the failed conversion had staged is thrown away first. The exception may
            // have come out of the very save that would have written it, and a failed save leaves
            // its entities pending: writing the outcome through the same change tracker would
            // send the identical failing statements again, the outcome would never be recorded,
            // and the model would say it was being converted for ever — as would the job, whose
            // own outcome is saved through this context after this returns.
            ForgetPendingChanges();

            if (holdsAMesh)
            {
                // The transaction never committed, so the model still names the mesh it had and
                // still draws it. It is left saying so; the failure is the job's, and reaches
                // whoever asked for the conversion from there.
                logger.LogWarning(
                    e, "Another conversion of survey model {SurveyModelId} failed and it keeps the mesh it had", model.Id);
                throw;
            }

            var failed = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == payload.SurveyModelId, CancellationToken.None);
            if (failed is not null)
            {
                failed.Status = SurveyModelStatus.Failed;
                // A mesh that cannot be read is nearly always a file the uploader can do something
                // about — the wrong format, an ASCII export, a coordinate system nobody here
                // knows — so that reason is kept. Anything else is ours and is not described to
                // them.
                failed.ProcessingError = e is SurveySourceException
                    ? e.Message
                    : "The model could not be converted.";
                await db.SaveChangesAsync(CancellationToken.None);
            }

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

    /// <summary>
    /// Puts every tracked entity back to what the database holds, so that the next save through
    /// this context writes only what is changed after this.
    /// </summary>
    private void ForgetPendingChanges()
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }

    /// <summary>
    /// Drops stored bytes nothing names any more. Never throws: bytes left behind cost disk, while
    /// a failure here would be reported as a failure of a conversion that has already succeeded.
    /// </summary>
    private async Task DiscardAsync(string? storagePath, Guid surveyModelId)
    {
        if (storagePath is null)
        {
            return;
        }

        try
        {
            await fileStore.DeleteAsync(storagePath, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e, "Could not delete a wall mesh survey model {SurveyModelId} no longer uses", surveyModelId);
        }
    }
}
