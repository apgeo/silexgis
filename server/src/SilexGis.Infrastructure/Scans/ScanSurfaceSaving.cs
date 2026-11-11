// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// Saves a surface the worker made of a scan as a wall mesh of the cave somebody chose, through
/// the same path an uploaded wall mesh takes.
/// </summary>
/// <remarks>
/// <para>
/// The surface comes back in the scan's tile frame. It is rewritten in east-north-up metres about
/// the tiles' zero point by the scan's placement, stored as a document of its own owned by whoever
/// asked, and recorded as a new STL model of the chosen cave declared exactly as an uploaded local
/// file is: plain metres about a named point (the placement's zero point) at a named height (the
/// placement's). Its reading is queued as an upload's is, and that reading turns it into the one
/// file the 3D scene draws; from there it is shown wherever wall meshes are, under the same rules.
/// </para>
/// <para>
/// The wall mesh is a copy, not a link to the scan's own artefact: the scan may make another
/// surface, or be removed, and the cave's walls stay what they were. Each surface saved is a new
/// model; it takes the cave's current wall-mesh mark only when the cave has none, as an upload
/// does, and somebody can make it current afterwards.
/// </para>
/// <para>
/// Re-placing the scan later moves its tiles, not this mesh: a wall mesh's placement is in its
/// vertices, made once.
/// </para>
/// </remarks>
public sealed class ScanSurfaceSaving(
    SilexGisDbContext db,
    IFileStore fileStore,
    DocumentWriteService documents,
    ILogger<ScanSurfaceSaving> logger)
{
    /// <summary>The media type a surface is stored under, as the scan's artefact and as the wall mesh's file.</summary>
    public const string StlMediaType = "model/stl";

    /// <summary>
    /// Saves the surface among <paramref name="made"/> as the requested cave's wall mesh, then takes
    /// the request and the step out of the upload's stored recipe. Fails the run, naming why, when
    /// the scan is not placed or the cave is gone: a surface that cannot be set down is not saved
    /// anywhere it would be mistaken for walls.
    /// </summary>
    public async Task<SurveyModel> SaveAsync(
        SurveyUpload upload,
        SurveyModel scan,
        ScanRecipe recipe,
        IReadOnlyList<ScanArtifact> made,
        string directory,
        CancellationToken ct)
    {
        var surface = made.FirstOrDefault(a => a.Kind == ScanArtifactKind.MeshStl)
            ?? throw new SurveyPipelineException(SurveyPipelineFailures.OutputMissing, "The point-cloud worker made no surface.");
        var request = recipe.Surface ?? new ScanSurfaceRequest(scan.CaveFeatureId, upload.RequestedBy);

        // Read off the row, not the instance loaded when the run began: somebody may have placed
        // the scan, or moved it, while the worker ran.
        var placed = await db.SurveyModels.AsNoTracking().Where(m => m.Id == scan.Id).SingleAsync(ct);
        var placement = ScanAutoPlacement.Read(placed)
            ?? throw new SurveyPipelineException(
                SurveyPipelineFailures.SurfaceUnplaced,
                "The surface was made, but the scan is not placed, so it cannot be set down as a cave's walls. Place the scan, then make the surface again.");

        var caveExists = await db.Features.AsNoTracking()
            .AnyAsync(f => f.Id == request.CaveId && f.Kind == FeatureKind.Cave && f.DeletedAt == null, ct);
        if (!caveExists)
        {
            throw new SurveyPipelineException(
                SurveyPipelineFailures.SurfaceUnplaced, "The surface was made, but the cave it was to be saved for no longer exists.");
        }

        var sourcePath = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == surface.FileId).Select(f => f.StoragePath).SingleAsync(ct);
        var placedPath = Path.Combine(directory, "surface.placed.stl");
        long triangles;
        try
        {
            await using (var read = await fileStore.OpenReadAsync(sourcePath, ct))
            await using (var write = File.Create(placedPath))
            {
                triangles = ScanSurfaceStl.Place(read, write, placement);
            }
        }
        catch (InvalidDataException e)
        {
            throw new SurveyPipelineException(SurveyPipelineFailures.WorkerFailed, "The surface the worker made does not read as a binary STL: " + e.Message);
        }

        string storagePath;
        long size;
        string sha256;
        await using (var read = File.OpenRead(placedPath))
        {
            size = read.Length;
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
            read.Position = 0;
            storagePath = await fileStore.SaveAsync(read, ".stl", ct);
        }

        File.Delete(placedPath);

        var stem = FileFormats.FitFileName(upload.SurveyTitle);
        stem = string.IsNullOrWhiteSpace(stem) ? "scan" : stem;
        var name = stem + " surface";
        var stored = documents.Create(
            new StoredContent(storagePath, stem + ".surface.stl", StlMediaType, size, sha256, FileKind.Survey),
            name,
            request.RequestedBy,
            request.RequestedBy).File;

        var model = new SurveyModel
        {
            CaveFeatureId = request.CaveId,
            Name = name,
            FileId = stored.Id,
            Format = SurveyModelFormat.Stl,
            SourceEpsg = null,
            Anchor = new Point(placement.Longitude, placement.Latitude) { SRID = 4326 },
            AnchorHeightM = placement.HeightM,
        };
        db.SurveyModels.Add(model);
        SurveyModelReading.QueueFirst(db, model, request.RequestedBy);
        await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);

        // Saved with the recipe that no longer asks for it, so a later rerun makes no second mesh.
        var stripped = ScanRecipes.ToOptionsJson(PointCloudRecipes.WithoutSurface(ScanRecipes.FromOptionsJson(upload.Options) ?? recipe));
        upload.Options = stripped;
        await db.SurveyUploads.Where(u => u.Id == upload.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Options, stripped), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (model.IsCurrent)
        {
            // Another wall mesh of the cave took the mark at the same moment: this one is kept without it.
            model.IsCurrent = false;
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Survey upload {UploadId}: surface of {Triangles} triangles saved as wall mesh {ModelId} of cave {CaveId}",
            upload.Id, triangles, model.Id, request.CaveId);
        return model;
    }
}
