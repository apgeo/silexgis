// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// The area-normalised view of the registry: how thickly cave entrances sit over a window.
///
/// <para>
/// The counting itself lives below this slice, with the other query bodies, because that is where
/// raw SQL lives and because a slice's internals are not something another slice may read. What
/// lives here is the part that is a decision rather than a query: which cell sizes and which kernel
/// widths this installation will publish, and what happens to a request for something finer.
/// </para>
/// <para>
/// The karstification index next door is computed from an area-wide caves-per-square-kilometre
/// ratio and not from this grid. The two are different summaries of the same registry and they can
/// disagree — an area with one dense pocket and a great deal of empty ground reads low on the ratio
/// and shows a clear peak here — so neither is derived from the other, and the component the index
/// publishes is named for the ratio it actually uses rather than for a grid it does not read.
/// </para>
/// </summary>
public static class MapDensityEndpoints
{
    /// <summary>Refused: the requested cell is finer than the location-protection grid.</summary>
    private const string CellTooFineCode = "density.cell_below_protection_grid";

    /// <summary>Refused: the window at that cell size would be more cells than one answer holds.</summary>
    private const string TooManyCellsCode = "density.too_many_cells";

    /// <summary>Refused: the named study area is not an outline, so it has no area to divide by.</summary>
    private const string StudyAreaNotAnOutlineCode = "density.study_area_not_an_outline";

    /// <summary>Refused: the requested kernel is narrower than the cell the counts sit in.</summary>
    private const string BandwidthTooFineCode = "density.bandwidth_below_cell";

    public static RouteGroupBuilder MapMapDensityEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/density", DensityAsync)
            .WithTags("Map")
            .WithValidation<MapDensityRequest>()
            .WithSummary(
                "Cave-entrance counts and densities per grid cell over a bbox, optionally normalised "
                + "by a study-area outline. Cells finer than the location-protection grid are refused.");
        return api;
    }

    private static async Task<Results<Ok<DensityGridDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        DensityAsync(
            [AsParameters] MapDensityRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            IOptions<AccessOptions> accessOptions,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Parsed and believed are two things. A box of four finite numbers can still span a
        // thousand globes, and everything below measures work per unit of window — cells to
        // enumerate, edges to segmentise — so an unchecked box is a way to ask for centuries of
        // database time in one request. The limits are checked before any of that is sized.
        if (!Bbox.TryParse(request.Bbox, out var box) || !box.IsWithinWorld)
        {
            return ApiProblems.BadRequest(
                "map.invalid_bbox",
                "bbox must be 'west,south,east,north' within -180..180 and -90..90, with west "
                + "less than east and south less than north.");
        }

        var protectionGridMeters = accessOptions.Value.LocationGridMeters;
        var minimumCellMeters = DensityGrid.MinimumCellMeters(protectionGridMeters);
        var cellMeters = request.CellMetres ?? minimumCellMeters;

        // Refused, not widened. A grid finer than the lattice protected coordinates are rounded
        // onto would undo that rounding cell by cell, and quietly serving a coarser grid than was
        // asked for would put a number on screen that means something other than its label.
        if (!DensityGrid.IsCellPermitted(cellMeters, protectionGridMeters))
        {
            return ApiProblems.BadRequest(
                CellTooFineCode,
                $"cellMetres must be at least {minimumCellMeters:0.###} m, this installation's "
                + "location-protection grid. A finer grid is not served at any zoom.");
        }

        var minimumBandwidthMeters = DensityGrid.MinimumBandwidthMeters(cellMeters);
        var bandwidthMeters = request.BandwidthMetres ?? DensityGrid.DefaultBandwidthMeters(cellMeters);

        // Refused for the same reason a too-fine cell is, one step further along. Smoothing counts
        // that are already binned cannot recover anything the binning removed, so a kernel narrower
        // than the cell draws each cell as an isolated peak and reads as though the peaks were
        // where the caves are.
        if (!(bandwidthMeters >= minimumBandwidthMeters))
        {
            return ApiProblems.BadRequest(
                BandwidthTooFineCode,
                $"bandwidthMetres must be at least the cell size, {minimumBandwidthMeters:0.###} m. "
                + "A narrower kernel cannot show anything the cells do not already hold.");
        }

        var cellDegrees = DensityGrid.CellDegrees(cellMeters);
        var cellCount = DensityGrid.CellCount(box.West, box.South, box.East, box.North, cellDegrees);
        if (cellCount > MapDensityLimits.MaxCells)
        {
            return ApiProblems.BadRequest(
                TooManyCellsCode,
                $"that window at {cellMeters:0.###} m cells is {cellCount} cells; the limit is "
                + $"{MapDensityLimits.MaxCells}. Ask for a smaller window or a coarser cell.");
        }

        if (request.AreaId is { } areaId)
        {
            // Projected into a wrapper rather than selected bare, so a readable outline that has
            // no geometry is not indistinguishable from one this caller cannot read at all.
            var area = await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => f.Id == areaId)
                .Select(f => new { f.Geom })
                .FirstOrDefaultAsync(ct);

            if (area is null)
            {
                return ApiProblems.NotFound("feature.not_found");
            }

            // An outline places itself: dividing by it, cell by cell, would draw its edges for a
            // caller who may not be shown where it is. So the denominator is only available to a
            // caller who may place it exactly, and to everybody else the area answers as one that
            // is not there — the same answer they get for an outline that does not exist, so the
            // refusal itself discloses nothing.
            if (!(await protection.ExactViewIdsAsync(ctx, [areaId], ct)).Contains(areaId))
            {
                return ApiProblems.NotFound("feature.not_found");
            }

            if (area.Geom is not (Polygon or MultiPolygon))
            {
                return ApiProblems.BadRequest(
                    StudyAreaNotAnOutlineCode,
                    "the study area must be an outline; a point or a line encloses no ground to "
                    + "divide a count by.");
            }
        }

        var rows = await DensityGridSql.CellsAsync(
            db, ctx, box.West, box.South, box.East, box.North, cellDegrees, protectionGridMeters,
            request.AreaId, ct);

        // The kernel is evaluated over the cells rather than over the features, so it inherits the
        // grid's protection instead of needing its own: the counts reaching it have already been
        // snapped and already been binned, and smoothing can only ever spread that out further.
        var surface = DensityGrid.KernelSurface(
            [.. rows.Select(r => ((r.West + r.East) / 2d, (r.South + r.North) / 2d, r.Count))],
            bandwidthMeters);

        var cells = rows.Select((row, i) => ToDto(row, surface[i])).ToList();

        return TypedResults.Ok(new DensityGridDto(
            cellMeters,
            minimumCellMeters,
            protectionGridMeters,
            bandwidthMeters,
            minimumBandwidthMeters,
            request.AreaId,
            request.AreaId is null ? null : rows.Sum(r => r.StudyAreaM2 ?? 0d) / 1_000_000d,
            rows.Sum(r => r.Count),
            cells.Count,
            cells));
    }

    private static DensityCellDto ToDto(DensityCellRow row, double kernelDensityPerKm2)
    {
        // The denominator is the ground actually being asked about: the part of the cell inside the
        // study area when one was named, the whole cell otherwise. A cell that only clips the
        // outline's corner has no meaningful density, and reporting zero rather than an enormous
        // number is the honest reading of "nothing here to count over".
        var denominatorM2 = row.StudyAreaM2 ?? row.CellAreaM2;
        var density = denominatorM2 > 0d ? row.Count / (denominatorM2 / 1_000_000d) : 0d;

        return new DensityCellDto(
            row.West,
            row.South,
            row.East,
            row.North,
            row.Count,
            row.CellAreaM2 / 1_000_000d,
            density,
            row.CellAreaM2 > 0d ? row.StudyAreaM2 / row.CellAreaM2 : null,
            kernelDensityPerKm2);
    }
}
