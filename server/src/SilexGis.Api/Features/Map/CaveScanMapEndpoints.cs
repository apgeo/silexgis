// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// The placed point cloud one cave is shown by, and what a scene needs in order to decide whether
/// to open it.
/// </summary>
/// <param name="TilesetUrl">
/// Signed address of the root tileset — hand it to a 3D Tiles reader as-is; it derives every
/// tile's address from it and the token holds for the session.
/// </param>
/// <param name="AnchorLongitude">
/// Where the tiles' own zero point sits on the earth. This is the cave's exact position, which is
/// why a cave whose position is closed to the caller is not in the answer at all.
/// </param>
/// <param name="AnchorHeightM">The altitude the tiles' zero plane sits at, in metres.</param>
/// <param name="HeadingDeg">
/// The compass bearing the tiles' own +Y axis points along, degrees clockwise from true north:
/// the turn a scene gives the tiles about the anchor. Zero when the frame's north is true north.
/// </param>
/// <param name="PitchDeg">
/// The small tilt a fit against surveyed stations may have found, raising the frame's +X end, in
/// degrees; zero for a level scan. The scene turns the tiles by it after the heading, exactly as
/// the fit that produced it composed the three angles.
/// </param>
/// <param name="RollDeg">The small tilt that raises the frame's +Y end, in degrees; zero for a level scan.</param>
/// <param name="PointCount">How many points the uploaded cloud holds, by its header; null when unknown.</param>
/// <param name="TiledPointCount">
/// How many points the tiles hold — the thinned copy's count when one was shown, else the
/// cloud's — which is what the scene's own point budget counts against.
/// </param>
/// <param name="SizeBytes">
/// The size of the tileset archive in bytes: the most a scene would fetch if it opened every
/// tile. Stated before anything is fetched because it is what a scene budgets by; a tiles reader
/// fetches by level of detail, so what it actually takes is usually less.
/// </param>
/// <param name="HasCentreline">
/// Whether the scan's run drew a centreline from the scanner's path; the line itself, set down
/// through the same placement, is read from the survey model's centreline route in the world frame.
/// </param>
public sealed record CaveScanInViewDto(
    Guid CaveId,
    string? CaveName,
    Guid SurveyModelId,
    string ModelName,
    string TilesetUrl,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    double HeadingDeg,
    double PitchDeg,
    double RollDeg,
    long? PointCount,
    long? TiledPointCount,
    long SizeBytes,
    bool HasCentreline);

/// <summary>The placed point clouds of the caves in a view, nearest the middle of it first.</summary>
/// <param name="Items">
/// At most the number of caves asked for, or the installation's default when none was, one scan
/// each.
/// </param>
/// <param name="Total">
/// How many caves qualified in all, the ones described included. It counts only what the caller
/// may be told about: a cave that is not theirs to read, or not theirs to place, is in neither
/// number — a count that included it would say that a protected cave stands in this box.
/// </param>
public sealed record CaveScansInViewDto(IReadOnlyList<CaveScanInViewDto> Items, int Total);

/// <summary>
/// The placed point clouds of every cave in a view, for a 3D scene that draws scans on the globe.
/// </summary>
public static class CaveScanMapEndpoints
{
    public static RouteGroupBuilder MapMapCaveScanEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/cave-scans", CaveScansAsync)
            .WithTags("Map")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The placed point cloud each cave is shown by, for the caves whose scan is anchored in the given bbox, nearest its middle first and capped at maxCaves (the installation's default when absent, never above its ceiling); a scan that has not been placed is never listed, and caves the caller may not read or may not place exactly are omitted and not counted. A filter (`filter` token or `savedFilterId`) keeps the caves it keeps on the entrance layer, and the total counts only those.");
        return api;
    }

    /// <summary>
    /// One scan per cave, for the caves whose scan is anchored in the box.
    /// <para>
    /// Which caves appear is decided here and nowhere else, by the rule the wall-mesh listing
    /// uses: the cave must be readable by the caller, and its exact location must be open to them.
    /// The tiles themselves are in the cloud's own frame and carry no coordinates, which is why a
    /// scan's card hands its address to every reader of the upload; the anchor that sets those
    /// tiles down on the earth is the cave's position, and it travels only here, after the
    /// exact-location rule has been asked over every candidate.
    /// </para>
    /// <para>
    /// The answer is capped by count only. The byte budget is published beside it and spent by
    /// the scene, because only the scene knows what it already holds; the sizes it needs to do
    /// that are stated on every item. <c>maxCaves</c> is the caller's own count, held between
    /// one cave and the installation's ceiling; it moves the cap and nothing else.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<CaveScansInViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> CaveScansAsync(
        string bbox,
        int? maxCaves,
        string? filter,
        Guid? savedFilterId,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        SurfaceFilters surfaceFilters,
        IFileAccessTokenService tokens,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (WindowProblems.Required(bbox, out var box) is { } windowProblem)
        {
            return windowProblem;
        }

        var narrowing = await surfaceFilters.ForAsync<Feature>(
            ctx, FeatureFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        // Readable caves first, then the exact-location rule over exactly those ids, then scans
        // for the ones that passed. Only the last statement reads an anchor out for the response.
        var caveIds = await CaveScanMapSql.CaveIdsWithScanInViewAsync(db, ctx, box, ct);
        var exactViewIds = await protection.ExactViewIdsAsync(ctx, caveIds, ct);
        var eligibleIds = caveIds.Where(exactViewIds.Contains).ToList();
        if (!narrowing.Narrowing!.NarrowsNothing)
        {
            // After the protection rule, so the filter only takes caves away from a decided set.
            var kept = await MapEndpoints.CavesTheFilterKeepsAsync(db, ctx, narrowing.Narrowing!, eligibleIds, ct);
            eligibleIds = [.. eligibleIds.Where(kept.Contains)];
        }

        var rows = await CaveScanMapSql.QueryAsync(db, ctx, box, eligibleIds, ct);

        var items = rows
            .Take(mapOptions.Value.ScansInViewCaves(maxCaves))
            .Select(row => new CaveScanInViewDto(
                row.CaveId,
                row.CaveName,
                row.SurveyModelId,
                row.ModelName,
                SurveyTileAddresses.Root(tokens, row.SurveyModelId, row.ConvertedFileId),
                row.AnchorLongitude,
                row.AnchorLatitude,
                row.AnchorHeightM,
                row.HeadingDeg,
                row.PitchDeg,
                row.RollDeg,
                row.PointCount,
                row.TiledPointCount,
                row.SizeBytes,
                row.HasCentreline))
            .ToList();

        return TypedResults.Ok(new CaveScansInViewDto(items, rows.Count));
    }
}
