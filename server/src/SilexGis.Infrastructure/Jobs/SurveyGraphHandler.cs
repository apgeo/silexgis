// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender;
using Therion.Blender.Parsing;
using Therion.Blender.Geometry;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.SurveyGraph"/> jobs.</summary>
public sealed record SurveyGraphPayload(Guid SurveyModelId);

/// <summary>
/// Reads an uploaded line-plot survey into station and shot rows carrying the file's own flags.
///
/// <para>
/// Off the request thread for the same reason the wall mesh is: a whole-system export runs to tens
/// of thousands of legs, and every one of them is a row. The viewer keeps drawing the file exactly
/// as it was uploaded while this runs, so nothing a person can see is waiting on it.
/// </para>
///
/// <para>
/// Re-running it replaces what the previous read produced rather than adding to it. A survey model
/// holds the rows of one file, and a second read of the same bytes is the same answer — but a job
/// can be run again after a crash, and rows appended a second time would double every count
/// computed over them without failing anything.
/// </para>
/// </summary>
public sealed class SurveyGraphHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    SurveyGraphExtractor extractor,
    FeatureWriteService writes) : IProcessingJobHandler
{
    /// <summary>What the feature name column holds; a survey file's own name can be longer.</summary>
    private const int MaxNameLength = 255;

    public string Kind => ProcessingJobKinds.SurveyGraph;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SurveyGraphPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty survey-graph payload.");

        var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == payload.SurveyModelId, ct)
            ?? throw new InvalidOperationException($"Survey model {payload.SurveyModelId} no longer exists.");
        var upload = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == model.FileId, ct)
            ?? throw new InvalidOperationException($"Stored file {model.FileId} no longer exists.");

        model.Status = SurveyModelStatus.Processing;
        await db.SaveChangesAsync(ct);

        try
        {
            var parsed = await ParseAsync(upload, ct);

            // Where the file sits, read back off the row rather than carried in the job payload: a
            // job re-run days later must place the file the same way, and the row is the only copy
            // of that answer which survives an edit.
            //
            // What the uploader said wins over what the file says, because it is the answer a
            // person chose; one of the two line-plot formats has a field naming its own coordinate
            // system, and where it is filled in that is the fallback. A file that neither names one
            // nor was given a position cannot be placed at all, and the extraction says so in those
            // words.
            var declaration = new SurveySourceDeclaration(
                model.SourceEpsg ?? SurveyPlacement.EpsgFrom(parsed.CoordinateSystem),
                model.Anchor?.X,
                model.Anchor?.Y,
                model.AnchorHeightM ?? 0);

            var extraction = extractor.Extract(parsed, model.Id, declaration);

            // Measured before the transaction opens, not inside it. The length is answered by
            // PostGIS over a raw command on this context's own connection, and a read that has
            // nothing to do with the write below has no business sharing its transaction.
            var shape = await MeasureAsync(extraction, ct);

            // Clearing what a previous read left behind and writing what this one found are one
            // change to the survey, so they are one transaction. Without it a read that fails on
            // the way in has already deleted the rows it was replacing, and the model is left
            // holding neither the old answer nor the new one.
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                // Whatever a previous read of this file left behind. Issued as statements of their
                // own rather than as tracked deletes, because loading tens of thousands of rows in
                // order to mark them deleted costs more than the read that produced them.
                //
                // The wall measurements go first. Those taken along a leg would follow their leg out
                // by the foreign key, but the ones the other format states name a station and no leg
                // at all, so deleting the legs would leave those behind — belonging to a reading of
                // the file that no longer exists, and indistinguishable from the ones this read is
                // about to write.
                // The measured shape of the network goes with them. It is derived from exactly
                // these rows, so a reading of it that outlived them would be a confident answer
                // about a file that had been read again since.
                await db.SurveyTopologies.Where(t => t.SurveyModelId == model.Id).ExecuteDeleteAsync(ct);
                await db.SurveyLruds.Where(l => l.SurveyModelId == model.Id).ExecuteDeleteAsync(ct);
                await db.SurveyShots.Where(s => s.SurveyModelId == model.Id).ExecuteDeleteAsync(ct);
                await db.SurveyStations.Where(s => s.SurveyModelId == model.Id).ExecuteDeleteAsync(ct);

                db.SurveyStations.AddRange(extraction.Stations);
                db.SurveyShots.AddRange(extraction.Shots);

                // A reading taken along a leg names its leg through the leg object rather than
                // through an id, because the id is the database's to assign and both rows are saved
                // in this one save.
                db.SurveyLruds.AddRange(extraction.Lrud);

                await WriteCenterlineAsync(model, shape, ct);

                // Measured now rather than when somebody asks, because the contraction and the
                // betweenness behind it are superlinear in the station count and a large system is
                // tens of thousands of stations — a figure a request has to wait for is a figure a
                // request times out on. A file with no network at all is measured as nothing.
                var topology = SurveyTopologyAnalyzer.Measure(
                    CenterlineGraph.Build(parsed), model.Id, DateTime.UtcNow);
                if (topology is not null) { db.SurveyTopologies.Add(topology); }

                model.Anchor = new Point(extraction.AnchorLongitude, extraction.AnchorLatitude) { SRID = 4326 };
                model.AnchorHeightM = extraction.AnchorHeightM;
                model.AppliedRotationDeg = extraction.AppliedRotationDeg;
                model.DroppedShotCount = extraction.DroppedShotCount;
                model.MergedStationCount = extraction.MergedStationCount;
                model.Status = SurveyModelStatus.Ready;
                model.ProcessingError = null;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }
        catch (Exception e)
        {
            // A survey that cannot be read or cannot be placed is nearly always something the
            // uploader can do something about — a truncated export, a format that is not what the
            // name says, a coordinate system nobody here knows, a file in plain metres with no
            // position given for its zero point — so that reason is kept. Anything else is ours
            // and is not described to them.
            await RecordFailureAsync(
                payload.SurveyModelId,
                e is SurveySourceException ? e.Message : "The survey could not be read.");
            throw;
        }
    }

    /// <summary>
    /// Writes the failure onto the survey model, and nothing else.
    ///
    /// <para>
    /// Everything the failed read had staged is thrown away first. The exception may well have come
    /// out of the save that would have written those rows — a file whose station names collide
    /// reaches the database before anything notices — and a failed save leaves its entities pending,
    /// so writing the failure through the same change tracker would re-send the identical failing
    /// statements and throw again. The model would then never be recorded as failed at all: it
    /// would sit as still being read for ever, be picked up again on every restart, and be polled
    /// by whoever uploaded it for just as long. The row carrying the failure is re-read rather than
    /// reused, so what is written is the failure and not half of an abandoned reading.
    /// </para>
    /// </summary>
    private async Task RecordFailureAsync(Guid surveyModelId, string reason)
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

        var model = await db.SurveyModels
            .FirstOrDefaultAsync(m => m.Id == surveyModelId, CancellationToken.None);
        if (model is null)
        {
            return;
        }

        model.Status = SurveyModelStatus.Failed;
        model.ProcessingError = reason;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Records the survey as a centerline of its cave: the line work exactly as surveyed, and
    /// beside it the display skeleton with the splays taken out.
    ///
    /// <para>
    /// The skeleton is built from the splay flag each leg carries, not from the shape of the
    /// network. That is the whole point of reading the file: the guess that has to be made for an
    /// uploaded GeoJSON or GPX is a guess about the surveyor's habits, and a statistic computed
    /// over 98% wall shots measures those habits rather than the cave.
    /// </para>
    ///
    /// <para>
    /// One extracted centerline per survey model, rewritten in place when the file is read again.
    /// Creating a second one on a re-run would leave the cave with two centerlines claiming to be
    /// the same survey, and the one that is the cave's shape on the map decided by which ran last.
    /// </para>
    /// </summary>
    private async Task WriteCenterlineAsync(SurveyModel model, CenterlineShape? shape, CancellationToken ct)
    {
        if (shape is null)
        {
            return;
        }

        var existing = await db.Centerlines
            .FirstOrDefaultAsync(c => c.SurveyModelId == model.Id && c.Source == CenterlineSource.Extracted, ct);

        var name = model.Name.Length > MaxNameLength ? model.Name[..MaxNameLength] : model.Name;

        if (existing is not null)
        {
            var feature = await db.Features.FirstAsync(f => f.Id == existing.Id, ct);
            feature.Name = name;
            feature.Geom = shape.Surveyed;
            existing.Skeleton = shape.Skeleton;
            existing.PathCount = shape.PathCount;
            existing.SkeletonPathCount = shape.SkeletonPathCount;
            existing.LengthM = shape.LengthM;
            return;
        }

        await writes.CreateCenterlineAsync(
            new Feature { Name = name, Geom = shape.Surveyed },
            new Centerline
            {
                CaveFeatureId = model.CaveFeatureId,
                SurveyModelId = model.Id,
                Source = CenterlineSource.Extracted,
                Skeleton = shape.Skeleton,
                PathCount = shape.PathCount,
                SkeletonPathCount = shape.SkeletonPathCount,
                LengthM = shape.LengthM,
            },
            ct);
    }

    /// <summary>What one reading makes of the cave's shape, before any of it is written.</summary>
    /// <param name="Surveyed">The survey as measured: every leg the file drew, splays included.</param>
    /// <param name="Skeleton">
    /// The display reduction, or null when it found nothing to remove and the geometry is already
    /// its own skeleton.
    /// </param>
    /// <param name="LengthM">
    /// How much passage the survey found — the traverse, with the wall shots left out.
    /// </param>
    private sealed record CenterlineShape(
        MultiLineString Surveyed,
        MultiLineString? Skeleton,
        int PathCount,
        int SkeletonPathCount,
        decimal LengthM);

    /// <summary>
    /// Works out the centerline this reading produces and asks the database what it measures.
    ///
    /// <para>
    /// The length is taken over the traverse and not over the geometry beside it, because they are
    /// two different questions and only one of them is ever asked out loud. A published length is
    /// read as how much cave was found; a whole-system export runs to some 98% wall shots, so a
    /// length measured over everything the file drew would report a 15 km cave as 240 km. Every
    /// other centerline here answers the same question — a centerline drawn by hand or imported as
    /// a track has no wall shots in it, so its geometry and its passage are the same line — and
    /// reading a file's own splay flags is exactly what makes that answerable here too.
    /// </para>
    /// </summary>
    private async Task<CenterlineShape?> MeasureAsync(SurveyGraphExtraction extraction, CancellationToken ct)
    {
        var surveyed = Surveyed(extraction);
        if (surveyed.IsEmpty)
        {
            return null;
        }

        var traverse = Traverse(extraction);
        var skeleton = CenterlineSkeleton.Sew(traverse);
        var storeSkeleton = CenterlineSkeleton.IsWorthStoring(surveyed, skeleton);
        var pathCount = CenterlineSkeleton.PathCount(surveyed);

        // A survey that is nothing but wall shots found no passage, and says so rather than being
        // measured over the shots that are not passage.
        var lengthM = traverse.IsEmpty
            ? 0m
            : await CenterlineSql.GeodesicLengthMetersAsync(db, traverse, ct);

        return new CenterlineShape(
            surveyed,
            storeSkeleton ? skeleton : null,
            pathCount,
            storeSkeleton ? CenterlineSkeleton.PathCount(skeleton) : pathCount,
            lengthM);
    }

    /// <summary>
    /// Every leg the file drew, splays included — the survey as it was measured, which is what a
    /// centerline's own geometry has always held.
    /// </summary>
    private static MultiLineString Surveyed(SurveyGraphExtraction extraction) =>
        Lines(extraction.Shots);

    /// <summary>
    /// The legs that are passage: everything the file did not flag as a splay. Surface and
    /// duplicate legs stay, because they are passage that was walked — one is above ground and
    /// one was surveyed twice, and both are drawn.
    /// </summary>
    private static MultiLineString Traverse(SurveyGraphExtraction extraction) =>
        Lines(extraction.Shots.Where(s => (s.Flags & SurveyShotFlags.Splay) == 0));

    /// <summary>
    /// Shot lines as one geometry. Zero-length legs are dropped: exports contain them (a station
    /// written twice) and PostGIS reports a geometry carrying them as invalid.
    /// </summary>
    private static MultiLineString Lines(IEnumerable<SurveyShot> shots) =>
        new([.. shots.Select(s => s.Geom).Where(g => g.Length > 0)]) { SRID = 4326 };

    /// <summary>
    /// The uploaded bytes, parsed. Read whole because the reader works on a span over the entire
    /// file — both formats are indexed rather than streamed — and the upload limit is what bounds
    /// how much that costs.
    /// </summary>
    private async Task<CaveModel> ParseAsync(StoredFile upload, CancellationToken ct)
    {
        byte[] bytes;
        await using (var input = await fileStore.OpenReadAsync(upload.StoragePath, ct))
        {
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        CaveModel model;
        try
        {
            model = CaveModelReader.Read(bytes, upload.OriginalName);
        }
        catch (CaveFileFormatException e)
        {
            // The reader's own reason for refusing a file is written for the person holding it,
            // and it is more specific than anything that could be said here.
            throw new SurveySourceException(e.Message);
        }

        // A drawing of a cave is not a record of where its passages are, and every figure taken
        // from the geometry afterwards would describe the drawing. The rule and the reasoning live
        // beside the exception it throws.
        SurveySourceRules.EnsureIsPlanView(model);

        return model;
    }
}
