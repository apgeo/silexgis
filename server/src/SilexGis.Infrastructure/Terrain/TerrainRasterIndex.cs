// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// What ground a build's prepared rasters cover, answered from a stored description rather than by
/// opening every file.
/// </summary>
/// <remarks>
/// <para>
/// Without this, working out which raster holds a point costs one dataset open — header, overview
/// layout, a read at the far corner — per raster per request. A single height would pay it; a
/// profile along a cave asks for hundreds and pays it hundreds of times. So each file is opened and
/// described once and the description is kept beside the build.
/// </para>
/// <para>
/// The store is a cache of the disk and never the authority over it. When a build has no rows —
/// because it was prepared before anything asked, or because its rows were taken away with a
/// rebuild — the directory is read and described, and what that finds is written down. That is also
/// why the description is built by listing the prepared directory rather than by asking the
/// preparation step what it would produce: that question is answered from the <i>source</i> rasters,
/// which an operator is free to clear away once a build is finished, and a build whose sources are
/// gone would otherwise report that it has no prepared rasters while its prepared rasters sit on
/// the disk.
/// </para>
/// </remarks>
public sealed class TerrainRasterIndex(
    SilexGisDbContext db,
    TerrainWorkspace workspace,
    ITerrainRasterPreparer preparer,
    ILogger<TerrainRasterIndex> logger)
{
    /// <summary>The rectangle every footprint is written in.</summary>
    private static readonly GeometryFactory Geometry =
        new(new PrecisionModel(), TerrainRasterPreparation.TargetEpsg);

    /// <summary>
    /// The rasters and datum of the build the installation currently serves terrain from, or a
    /// coverage holding nothing if there is no such build.
    /// </summary>
    /// <remarks>
    /// The active build, and deliberately not the narrower "active and already baked into tiles"
    /// that the scene's terrain source is resolved by. Prepared rasters exist from the moment the
    /// first step of a build finishes, long before there is a pyramid to draw, and they are what is
    /// read here — so gating on the pyramid would report no coverage over ground that has perfectly
    /// good elevation on disk.
    /// </remarks>
    public async Task<TerrainCoverage> ActiveCoverageAsync(CancellationToken ct)
    {
        var active = await db.TerrainBuilds
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new { b.Id, b.HeightDatum, b.GeoidHeightM })
            .FirstOrDefaultAsync(ct);

        return active is null
            ? TerrainCoverage.None
            : new TerrainCoverage(
                await RastersAsync(active.Id, ct), active.HeightDatum, active.GeoidHeightM);
    }

    /// <summary>
    /// The rasters and datum of one build, or a coverage holding nothing if there is no such build.
    /// </summary>
    public async Task<TerrainCoverage> CoverageForAsync(Guid buildId, CancellationToken ct)
    {
        var build = await db.TerrainBuilds
            .AsNoTracking()
            .Where(b => b.Id == buildId)
            .Select(b => new { b.Id, b.HeightDatum, b.GeoidHeightM })
            .FirstOrDefaultAsync(ct);

        return build is null
            ? TerrainCoverage.None
            : new TerrainCoverage(
                await RastersAsync(build.Id, ct), build.HeightDatum, build.GeoidHeightM);
    }

    /// <summary>
    /// Describes this build's prepared rasters and stores what it finds, replacing whatever was
    /// stored before.
    /// </summary>
    /// <remarks>
    /// Called when a build has finished preparing, and again by a read that finds nothing stored.
    /// Replacing rather than adding, because a build prepared a second time can produce fewer
    /// rasters than it did the first time and a row left behind would name a file that is gone.
    /// </remarks>
    public async Task<IReadOnlyList<PreparedTerrainRaster>> RefreshAsync(
        Guid buildId, CancellationToken ct)
    {
        var described = Describe(buildId);

        // One transaction around the pair of writes, and one lock inside it, because the delete and
        // the insert together are a replacement and a half-done replacement is visible to everybody
        // else. Two callers describing the same build at the same moment — the step that has just
        // prepared it and a request that found nothing stored — would otherwise both delete an
        // empty set and both insert the same rows, and the second would fail on the rule that one
        // path appears once per build. A caller already inside a transaction of its own keeps it:
        // its own boundary is the wider one and its own lock ordering is not this method's to
        // change.
        var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            await TerrainBuildSql.TakeRasterIndexLockAsync(db, buildId, ct);

            await db.TerrainBuildRasters
                .Where(r => r.TerrainBuildId == buildId)
                .ExecuteDeleteAsync(ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var raster in described)
            {
                db.TerrainBuildRasters.Add(new TerrainBuildRaster
                {
                    TerrainBuildId = buildId,
                    Path = raster.Path,
                    Width = raster.Width,
                    Height = raster.Height,
                    PixelSizeDegrees = raster.PixelSizeDegrees,
                    Footprint = Rectangle(raster),
                    VoidValue = raster.VoidValue,
                    SizeBytes = raster.SizeBytes,
                    DescribedAt = now,
                });
            }

            await db.SaveChangesAsync(ct);

            if (owned is not null)
            {
                await owned.CommitAsync(ct);
            }
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }

        return described;
    }

    private async Task<IReadOnlyList<PreparedTerrainRaster>> RastersAsync(
        Guid buildId, CancellationToken ct)
    {
        var stored = await db.TerrainBuildRasters
            .AsNoTracking()
            .Where(r => r.TerrainBuildId == buildId)
            .ToListAsync(ct);

        if (stored.Count > 0)
        {
            return [.. stored.Select(Prepared)];
        }

        return await RefreshAsync(buildId, ct);
    }

    /// <summary>
    /// Every whole prepared raster in this build's prepared directory, opened and read back.
    /// </summary>
    /// <remarks>
    /// The directory is named without being created. The one that creates what it names is right
    /// for a step about to write and exactly wrong here: asking a deleted build where its rasters
    /// were would put its directory back.
    /// </remarks>
    private List<PreparedTerrainRaster> Describe(Guid buildId)
    {
        var prepared = workspace.PreparedFor(buildId);
        if (!Directory.Exists(prepared))
        {
            return [];
        }

        var described = new List<PreparedTerrainRaster>();
        foreach (var path in Directory.EnumerateFiles(prepared).Order(StringComparer.Ordinal))
        {
            if (!TerrainRasterFiles.IsRaster(path))
            {
                continue;
            }

            // Never throws: a file that is not a whole prepared raster is simply not one, and a
            // fragment left by an interrupted run is left out rather than read as ground.
            if (preparer.Describe(path) is { } raster)
            {
                described.Add(raster);
            }
            else
            {
                logger.LogWarning(
                    "A file in a build's prepared directory is not a whole elevation raster and was "
                    + "left out of its coverage: {Path}", path);
            }
        }

        return described;
    }

    /// <summary>The stored row read back as the description it was written from.</summary>
    /// <remarks>
    /// The footprint is an axis-aligned rectangle written from four numbers, so its envelope gives
    /// those four numbers back exactly — the coordinates are stored as the doubles they are.
    /// </remarks>
    private static PreparedTerrainRaster Prepared(TerrainBuildRaster row)
    {
        var box = row.Footprint.EnvelopeInternal;
        return new PreparedTerrainRaster(
            row.Path,
            row.Width,
            row.Height,
            row.PixelSizeDegrees,
            box.MinX,
            box.MinY,
            box.MaxX,
            box.MaxY,
            row.VoidValue,
            row.SizeBytes);
    }

    private static Polygon Rectangle(PreparedTerrainRaster raster) =>
        Geometry.CreatePolygon(
        [
            new Coordinate(raster.West, raster.South),
            new Coordinate(raster.East, raster.South),
            new Coordinate(raster.East, raster.North),
            new Coordinate(raster.West, raster.North),
            new Coordinate(raster.West, raster.South),
        ]);
}
