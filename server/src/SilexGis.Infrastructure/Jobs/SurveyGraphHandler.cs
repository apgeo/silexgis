// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Documents;
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
///
/// <para>
/// The same reading also builds the survey's walls, where the file gives something real to build
/// them from, and stores them as the mesh the 3D scene draws. Here and not in a job of its own
/// because everything the walls need is in hand at this moment — the parsed file, which is tens of
/// megabytes to read a second time, and the placement its stations were just stored by — and
/// because a second job would publish the survey as finished and its walls some seconds later, to a
/// page that stops asking once it is told the work is done. The walls are the lesser half of this
/// job all the same: nothing that goes wrong building or storing them fails the reading, undoes it
/// or marks the model as failed. The stations and legs are what the file is uploaded for.
/// </para>
/// </summary>
public sealed class SurveyGraphHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    SurveyGraphExtractor extractor,
    FeatureWriteService writes,
    DocumentWriteService documents,
    ILogger<SurveyGraphHandler> logger) : IProcessingJobHandler
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

        // The walls this reading built, once their bytes are in the file store. Declared out here
        // because the bytes are written before the transaction that records them, and a reading
        // that then fails has to be able to take them back out.
        BuiltWalls? walls = null;

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

            // Which legs are shots at the passage wall, settled once for everything below. The
            // extraction works this out for itself as well — it has to, or a caller that is not
            // this job gets a different answer — but the graph the cave's morphometrics are
            // computed over is built from the parsed file rather than from the extracted rows, and
            // it decides what is passage by asking each leg's flags. On a file whose exporter set
            // no wall-shot flags, that graph would otherwise take every wall point for a dead end
            // of the cave and report the shape of the drawing instead of the shape of the cave.
            var file = SurveyWallShots.Flagged(parsed);

            var extraction = extractor.Extract(file, model.Id, declaration);

            // Measured before the transaction opens, not inside it. The length is answered by
            // PostGIS over a raw command on this context's own connection, and a read that has
            // nothing to do with the write below has no business sharing its transaction.
            var shape = await MeasureAsync(extraction, ct);

            // Measured before the transaction opens, not inside it. The contraction and the
            // betweenness behind it are superlinear in the station count, so on a large system this
            // is seconds to minutes of pure arithmetic with no database in it — and run inside the
            // transaction below it would hold the delete locks for that whole time with the
            // connection idle, which an installation that sets a timeout on idle transactions
            // kills outright. A file with no network at all is measured as nothing.
            var topology = SurveyTopologyAnalyzer.Measure(
                CenterlineGraph.Build(file), model.Id, DateTime.UtcNow);

            // Built before the transaction opens for the reason the topology is measured there:
            // it is arithmetic over the whole file followed by a write to the file store, and
            // neither belongs inside a transaction holding delete locks.
            walls = await BuildWallsAsync(file, extraction.Placement, upload, model.Id, ct);

            WallsOutcome outcome;

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

                // Measured above, before this transaction opened: written here, where it is part
                // of the one commit that replaces everything this reading produced.
                if (topology is not null) { db.SurveyTopologies.Add(topology); }

                model.Anchor = new Point(extraction.AnchorLongitude, extraction.AnchorLatitude) { SRID = 4326 };
                model.AnchorHeightM = extraction.AnchorHeightM;
                model.AppliedRotationDeg = extraction.AppliedRotationDeg;
                model.DroppedShotCount = extraction.DroppedShotCount;
                model.MergedStationCount = extraction.MergedStationCount;
                model.AnonymousStationCount = extraction.AnonymousStationCount;
                // Written on every reading, including back to null: a file replaced by one whose
                // root survey is named differently must not keep the old name, because that name is
                // what tells a stored station name from the way the viewer addresses the same
                // station, and a stale one silently misaddresses every station in the model.
                model.RootSurveyName = extraction.RootSurveyName;
                model.Status = SurveyModelStatus.Ready;
                model.ProcessingError = null;
                await db.SaveChangesAsync(ct);

                // After the reading is saved and before it is committed, so that the survey is
                // published as read and as walled in one step — and guarded, so that the commit
                // below happens whether or not the walls could be recorded.
                outcome = await ReplaceWallsAsync(tx, model, upload, walls, ct);
                await tx.CommitAsync(ct);
            }

            // Bytes are dropped only once the rows that named them are gone for good. The mesh a
            // previous reading built is no longer referenced by anything; the one this reading
            // built is in the same position if it could not be recorded.
            await DiscardAsync(outcome.ReplacedStoragePath, model.Id);
            if (!outcome.Recorded)
            {
                await DiscardAsync(walls?.StoragePath, model.Id);
            }
        }
        catch (Exception e)
        {
            // The reading left no row behind, so the mesh built for it belongs to nothing.
            await DiscardAsync(walls?.StoragePath, model.Id);

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

    /// <summary>The mesh one reading built, as the file store holds it.</summary>
    private sealed record BuiltWalls(string StoragePath, long SizeBytes, string Sha256, int TriangleCount);

    /// <summary>What became of the walls inside the reading's transaction.</summary>
    /// <param name="Recorded">The model now names the mesh this reading built.</param>
    /// <param name="ReplacedStoragePath">
    /// Where the mesh of a previous reading was kept, when this one took its place; its row is gone
    /// and its bytes are the caller's to drop once the transaction has committed.
    /// </param>
    private readonly record struct WallsOutcome(bool Recorded, string? ReplacedStoragePath);

    /// <summary>
    /// Builds the survey's walls and puts them in the file store, or answers null when the file
    /// gives nothing to build them from — or when building them failed.
    ///
    /// <para>
    /// Both are the same answer to the reading: carry on without walls. A file with no wall
    /// surfaces and no measured passage is the ordinary case and not an error, and a fault in here
    /// is logged and goes no further, because the stations and legs this job exists to store are
    /// already worked out and must not be lost to their own decoration.
    /// </para>
    ///
    /// <para>
    /// Nothing logged here names a station or a position. The survey may be of a cave whose
    /// location is protected, and a log is read by people the cave's record is closed to.
    /// </para>
    /// </summary>
    private async Task<BuiltWalls?> BuildWallsAsync(
        CaveModel file, SurveyPlacement placement, StoredFile upload, Guid surveyModelId, CancellationToken ct)
    {
        try
        {
            var built = SurveyWallBuilder.Build(file, placement);
            if (built.Mesh is not { } mesh)
            {
                logger.LogInformation(
                    "Survey model {SurveyModelId} has no walls: {Reason}", surveyModelId, built.Reason);
                return null;
            }

            // The vertices are already metres about the anchor, turned onto true north, so the mesh
            // is written about its own zero. Held in memory rather than in a scratch file: the cap
            // on a built mesh keeps this to tens of megabytes, beside a parsed upload that is
            // already larger.
            using var glb = new MemoryStream();
            GlbWriter.Write(mesh, (0, 0, 0), glb);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(glb.GetBuffer().AsSpan(0, (int)glb.Length)));

            glb.Position = 0;
            var storagePath = await fileStore.SaveAsync(glb, ".glb", ct);

            logger.LogInformation(
                "Survey model {SurveyModelId} has walls built from {Source}: {Triangles} triangles, {WalledLegs} of {PassageLegs} legs walled, {RingSides} sides a ring",
                surveyModelId, built.Source, mesh.TriangleCount, built.WalledLegs, built.PassageLegs, built.RingSides);

            return new BuiltWalls(storagePath, glb.Length, sha256, mesh.TriangleCount);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(
                e,
                "Could not build walls for survey model {SurveyModelId} from {UploadFileId}; it is read without them",
                surveyModelId, upload.Id);
            return null;
        }
    }

    /// <summary>
    /// Makes the model's mesh the one this reading built — or none, when it built none — inside the
    /// reading's own transaction, without ever being able to fail it.
    ///
    /// <para>
    /// <b>Replaced, not added to.</b> The mesh is a derived file of the upload's revision, and a
    /// revision holds at most one file derived from any upload, which the database enforces. So a
    /// second reading cannot simply record a second mesh: the first one's row is removed and the
    /// new one written, in that order and in two saves, because the three rows involved constrain
    /// one another in a circle — the model must stop naming the old file before the old file can
    /// go, the old file must go before the new one can claim the same upload, and the new one must
    /// exist before the model can name it. A reading that built nothing takes the old mesh away
    /// just the same: what is drawn for a survey is what its latest reading made of it.
    /// </para>
    ///
    /// <para>
    /// <b>Guarded by a savepoint, not by a transaction of its own.</b> A failed statement poisons a
    /// PostgreSQL transaction, and this transaction is carrying the reading. Rolling back to the
    /// savepoint undoes exactly the statements made here and leaves the reading's rows ready to
    /// commit — with whatever mesh the model had before, which was built from these same bytes and
    /// is still true of them. The commonest way into that branch is an old mesh somebody has since
    /// attached to something else: the attachment forbids its removal, quite rightly.
    /// </para>
    /// </summary>
    private async Task<WallsOutcome> ReplaceWallsAsync(
        IDbContextTransaction transaction,
        SurveyModel model,
        StoredFile upload,
        BuiltWalls? walls,
        CancellationToken ct)
    {
        const string savepoint = "survey_walls";
        var meshBefore = model.ConvertedFileId;
        var trianglesBefore = model.TriangleCount;

        await transaction.CreateSavepointAsync(savepoint, ct);
        try
        {
            string? replaced = null;
            var previous = await db.StoredFiles.FirstOrDefaultAsync(f => f.ConvertedFromFileId == upload.Id, ct);
            if (previous is not null || model.ConvertedFileId is not null)
            {
                model.ConvertedFileId = null;
                model.TriangleCount = null;
                if (previous is not null)
                {
                    replaced = previous.StoragePath;
                    db.StoredFiles.Remove(previous);
                }

                await db.SaveChangesAsync(ct);
            }

            if (walls is null)
            {
                return new WallsOutcome(Recorded: false, replaced);
            }

            // Another encoding of the uploaded file rather than a new revision of it, so it joins
            // the upload's own revision and says which file it was derived from — recorded through
            // the service that knows what a stored file has to carry, which also decides from the
            // format that a binary mesh has no text to read and no page to draw.
            var mesh = documents.AddFile(
                upload.DocumentVersionId,
                new StoredContent(
                    walls.StoragePath,
                    Path.GetFileNameWithoutExtension(upload.OriginalName) + ".glb",
                    "model/gltf-binary",
                    walls.SizeBytes,
                    walls.Sha256,
                    FileKind.Survey),
                convertedFromFileId: upload.Id);

            model.ConvertedFileId = mesh.Id;
            model.TriangleCount = walls.TriangleCount;
            await db.SaveChangesAsync(ct);

            return new WallsOutcome(Recorded: true, replaced);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(
                e,
                "Could not record the walls built for survey model {SurveyModelId}; it keeps the mesh it had",
                model.Id);

            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);

            // The database is back where the reading left it, and the change tracker has to agree
            // before the commit that follows: whatever this staged is forgotten, and the model's
            // two mesh columns are put back to what the database now holds again — one of the
            // saves above may have succeeded, in which case the tracker believes a change that the
            // rollback has just undone.
            ForgetPendingChanges();
            var mesh = db.Entry(model).Property(m => m.ConvertedFileId);
            mesh.CurrentValue = meshBefore;
            mesh.OriginalValue = meshBefore;
            mesh.IsModified = false;
            var triangles = db.Entry(model).Property(m => m.TriangleCount);
            triangles.CurrentValue = trianglesBefore;
            triangles.OriginalValue = trianglesBefore;
            triangles.IsModified = false;

            return new WallsOutcome(Recorded: false, ReplacedStoragePath: null);
        }
    }

    /// <summary>
    /// Drops stored bytes nothing names any more. Never throws: bytes left behind cost disk, while
    /// a failure here would be reported as a failure of the reading that has already succeeded.
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

    /// <summary>
    /// Throws away everything the change tracker is still waiting to write. A failed save leaves
    /// its entities pending, so the next save through the same tracker would send the identical
    /// failing statements again.
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
    /// Writes the failure onto the survey model, and nothing else.
    ///
    /// <para>
    /// Everything the failed read had staged is thrown away first. The exception may well have come
    /// out of the save that would have written those rows, and a failed save leaves its entities
    /// pending, so writing the failure through the same change tracker would re-send the identical
    /// failing statements and throw again. The model would then never be recorded as failed at all: it
    /// would sit as still being read for ever, be picked up again on every restart, and be polled
    /// by whoever uploaded it for just as long. The row carrying the failure is re-read rather than
    /// reused, so what is written is the failure and not half of an abandoned reading.
    /// </para>
    ///
    /// <para>
    /// The case this was written for — a file whose station names collide, refused by the unique
    /// index out of the save itself — is now settled before the save and arrives here as a reason
    /// the uploader can act on rather than as a constraint violation. This still runs for it, but
    /// what it writes is no longer the blank sentence. What the paragraph above still guards is the
    /// general shape: anything that fails at the save, for a reason nobody has anticipated, must
    /// leave the model saying it failed rather than saying it is still being read.
    /// </para>
    /// </summary>
    private async Task RecordFailureAsync(Guid surveyModelId, string reason)
    {
        ForgetPendingChanges();

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
    /// The legs that are passage: everything not marked as a shot at the wall. Surface and
    /// duplicate legs stay, because they are passage that was walked — one is above ground and
    /// one was surveyed twice, and both are drawn.
    ///
    /// <para>
    /// The mark is this application's reading of the file and not the file's own bit, which is what
    /// makes the paragraph on <see cref="MeasureAsync"/> true of every file rather than of the ones
    /// whose exporter was thorough. An exporter that flags nothing still writes its wall shots at
    /// points it declined to name, and those legs are marked on the way in.
    /// </para>
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
