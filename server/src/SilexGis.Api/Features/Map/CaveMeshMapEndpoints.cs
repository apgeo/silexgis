// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// The wall mesh one cave is drawn by, and what a scene needs in order to decide whether to
/// fetch it.
/// </summary>
/// <param name="MeshUrl">Signed URL of the drawable mesh — fetch and hand to the scene as-is.</param>
/// <param name="AnchorLongitude">
/// Where the mesh's own zero point sits. This is the cave's exact position, which is why a cave
/// whose position is closed to the caller is not in the answer at all.
/// </param>
/// <param name="SizeBytes">
/// How many bytes the mesh is to fetch. Stated before anything is fetched because it is what a
/// scene budgets by: a mesh is held whole, and the largest are tens of megabytes.
/// </param>
public sealed record CaveMeshInViewDto(
    Guid CaveId,
    string? CaveName,
    Guid SurveyModelId,
    string ModelName,
    string MeshUrl,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    int? TriangleCount,
    long SizeBytes);

/// <summary>The wall meshes of the caves in a view, nearest the middle of it first.</summary>
/// <param name="Items">At most the installation's limit of caves, one mesh each.</param>
/// <param name="Total">
/// How many caves qualified in all, the ones described included, so a scene showing a few of
/// them can say a few of how many. It counts only what the caller may be told about: a cave that
/// is not theirs to read, or not theirs to place, is in neither number — a count that included
/// it would say that a protected cave stands in this box.
/// </param>
public sealed record CaveMeshesInViewDto(IReadOnlyList<CaveMeshInViewDto> Items, int Total);

/// <summary>
/// The wall meshes of every cave in a view, for a 3D scene that draws more than the one selected
/// cave's walls.
/// </summary>
public static class CaveMeshMapEndpoints
{
    public static RouteGroupBuilder MapMapCaveMeshEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/cave-meshes", CaveMeshesAsync)
            .WithTags("Map")
            .WithSummary("The wall mesh each cave is drawn by, for the caves whose mesh is anchored in the given bbox, nearest its middle first and capped; caves the caller may not read or may not place exactly are omitted and not counted.");
        return api;
    }

    /// <summary>
    /// One mesh per cave, for the caves anchored in the box.
    /// <para>
    /// Which caves appear is decided here and nowhere else. A mesh inherits everything from its
    /// cave: the cave must be readable by the caller, and its exact location must be open to them
    /// — the mesh's anchor is that location, and its triangles are a measured drawing of the
    /// passage. That is the rule a single cave's survey models are read under, asked of a set.
    /// </para>
    /// <para>
    /// The answer is capped by count only. The byte budget is published beside it and spent by
    /// the scene, because only the scene knows what it already holds and which cave the viewer
    /// has selected; the sizes it needs to do that are stated on every item.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<CaveMeshesInViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> CaveMeshesAsync(
        string bbox,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IFileAccessTokenService tokens,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        // Readable caves first, then the exact-location rule over exactly those ids, then meshes
        // for the ones that passed. Only the last statement reads an anchor out for the response.
        var caveIds = await CaveMeshMapSql.CaveIdsWithMeshInViewAsync(db, ctx, box, ct);
        var exactViewIds = await protection.ExactViewIdsAsync(ctx, caveIds, ct);
        var eligibleIds = caveIds.Where(exactViewIds.Contains).ToList();

        var rows = await CaveMeshMapSql.QueryAsync(db, ctx, box, eligibleIds, ct);

        var items = rows
            .Take(Math.Max(0, mapOptions.Value.MeshesInViewMaxCaves))
            .Select(row => new CaveMeshInViewDto(
                row.CaveId,
                row.CaveName,
                row.SurveyModelId,
                row.ModelName,
                MeshUrl(tokens, row.ConvertedFileId),
                row.AnchorLongitude,
                row.AnchorLatitude,
                row.AnchorHeightM,
                row.TriangleCount,
                row.SizeBytes))
            .ToList();

        return TypedResults.Ok(new CaveMeshesInViewDto(items, rows.Count));
    }

    // Signed for the wider reach, as a converted mesh is on the cave's own routes: it holds
    // nothing but offsets from its anchor, and the anchor travels only in a response that has
    // already decided the caller may have it.
    private static string MeshUrl(IFileAccessTokenService tokens, Guid fileId) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.Full))}";
}
