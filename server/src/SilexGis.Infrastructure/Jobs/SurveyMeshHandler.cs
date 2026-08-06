// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
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
/// </summary>
public sealed class SurveyMeshHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    SurveyMeshConverter converter) : IProcessingJobHandler
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

        model.Status = SurveyModelStatus.Processing;
        await db.SaveChangesAsync(ct);

        // The declaration the uploader made, read back off the row rather than carried in the job
        // payload: a job that is retried days later must convert the file the same way, and the row
        // is the only copy of that answer which survives an edit.
        var declaration = new MeshSourceDeclaration(
            model.SourceEpsg,
            model.Anchor?.X,
            model.Anchor?.Y,
            model.AnchorHeightM ?? 0);

        var workPath = Path.Combine(Path.GetTempPath(), $"silexgis-mesh-{Guid.NewGuid():N}.glb");
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

            string sha256;
            await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
            {
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
            }

            var converted = new StoredFile
            {
                StoragePath = storagePath,
                OriginalName = Path.GetFileNameWithoutExtension(upload.OriginalName) + ".glb",
                MimeType = "model/gltf-binary",
                SizeBytes = new FileInfo(fileStore.GetAbsolutePath(storagePath)).Length,
                Sha256 = sha256,
                UploadedBy = upload.UploadedBy,
                Kind = FileKind.Survey,
            };
            converted.VersionGroupId = converted.Id; // head of its own version chain
            db.StoredFiles.Add(converted);

            model.ConvertedFileId = converted.Id;
            model.Anchor = new Point(conversion.AnchorLongitude, conversion.AnchorLatitude) { SRID = 4326 };
            model.AnchorHeightM = conversion.AnchorHeightM;
            model.AppliedRotationDeg = conversion.AppliedRotationDeg;
            model.TriangleCount = conversion.TrianglesRead - conversion.DegenerateDropped;
            model.SourcePrecisionLost = conversion.SourcePrecisionLost;
            model.Status = SurveyModelStatus.Ready;
            model.ProcessingError = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            model.Status = SurveyModelStatus.Failed;
            // A mesh that cannot be read is nearly always a file the uploader can do something
            // about — the wrong format, an ASCII export, a coordinate system nobody here knows — so
            // that reason is kept. Anything else is ours and is not described to them.
            model.ProcessingError = e is MeshIOException
                ? e.Message
                : "The model could not be converted.";
            await db.SaveChangesAsync(CancellationToken.None);
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
