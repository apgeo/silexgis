// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Terrain;

/// <summary>One raster a build was made from, and the credit the data it holds requires.</summary>
public sealed record TerrainBuildSourceDto(
    long Id,
    TerrainBuildSourceKind Kind,
    string Reference,
    string Attribution,
    string? Licence);

/// <summary>
/// A build as it appears in the list: what it covers, how far it got, what it costs on disk, and
/// whether it is the terrain the scene draws.
/// </summary>
/// <param name="Extent">The ground the build covers, as drawn (WGS84).</param>
/// <param name="SurveyHeightOffsetM">
/// Metres to add to a surveyed altitude so it sits on the ground this build draws. Served rather
/// than left to the caller to work out, because the rule that decides it has exactly one home and
/// a second copy of it in a browser would be a forty-metre error nothing on screen could explain.
/// </param>
public sealed record TerrainBuildDto(
    Guid Id,
    GeoJsonGeometry Extent,
    int RequestedMaxDepth,
    TerrainBuildStatus Status,
    TerrainBuildPhase Phase,
    int Progress,
    string? Message,
    string? ErrorCode,
    long? SizeBytes,
    string? PyramidVersion,
    TerrainHeightDatum HeightDatum,
    double GeoidHeightM,
    double SurveyHeightOffsetM,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One build in full: everything the list carries, plus the rasters it was made from and the tail
/// of what the tools said.
/// </summary>
/// <remarks>
/// The sources and the log tail are here and not in the list because both are unbounded per row —
/// a national lidar build can name hundreds of tiles, and a page of fifty builds would carry
/// megabytes of tool output nobody asked for.
/// </remarks>
public sealed record TerrainBuildDetailDto(
    TerrainBuildDto Build,
    string? LogTail,
    IReadOnlyList<TerrainBuildSourceDto> Sources);

/// <summary>
/// Reading the record of terrain builds. Installation-level throughout: a build has no owner, no
/// caving group and no audience, so the right to see one is held over the whole terrain domain
/// rather than derived from any single row, and the listing is unfiltered once that right is
/// established because there is no per-row audience to filter to.
/// </summary>
public static class TerrainBuildEndpoints
{
    /// <summary>The build asked for does not exist.</summary>
    public const string NotFoundCode = "terrain_build.not_found";

    /// <summary>The caller is signed in but holds nothing over the terrain domain.</summary>
    public const string ForbiddenCode = "access.forbidden";

    public static RouteGroupBuilder MapTerrainBuildEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/terrain/builds", ListAsync)
            .WithTags("Terrain")
            .WithSummary("Terrain builds, newest first, with their status and size; requires Read on the Terrain domain.");
        api.MapGet("/terrain/builds/{id:guid}", GetAsync)
            .WithTags("Terrain")
            .WithSummary("One terrain build with its sources and the tail of its log; requires Read on the Terrain domain.");
        return api;
    }

    private static async Task<Results<Ok<PagedResult<TerrainBuildDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.TerrainBuilds.AsNoTracking();
        var total = await query.CountAsync(ct);

        // Newest first, then by key. The timestamp alone is not an order: two builds submitted in
        // the same tick would sort differently between one page and the next, so a row could be
        // read twice or skipped entirely. The key breaks every tie, and because it is time-ordered
        // it breaks them in the direction the timestamp was already going.
        var rows = await query
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<TerrainBuildDto>([.. rows.Select(ToDto)], p, size, total));
    }

    private static async Task<Results<Ok<TerrainBuildDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The refusal is 403 and not a 404 standing in for one: unlike a job, a build belongs to
        // nobody, so there is no other person's row whose existence could be disclosed by saying
        // plainly that the caller lacks the right.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var build = await db.TerrainBuilds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (build is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var sources = await db.TerrainBuildSources.AsNoTracking()
            .Where(s => s.TerrainBuildId == id)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        return TypedResults.Ok(new TerrainBuildDetailDto(
            ToDto(build),
            build.LogTail,
            [.. sources.Select(s => new TerrainBuildSourceDto(
                s.Id, s.Kind, s.Reference, s.Attribution, s.Licence))]));
    }

    private static TerrainBuildDto ToDto(TerrainBuild build) => new(
        build.Id,
        GeoJsonGeometry.From(build.Extent),
        build.RequestedMaxDepth,
        build.Status,
        build.Phase,
        build.Progress,
        build.Message,
        build.ErrorCode,
        build.SizeBytes,
        build.PyramidVersion,
        build.HeightDatum,
        build.GeoidHeightM,
        build.SurveyHeightOffsetM,
        build.IsActive,
        build.CreatedAt,
        build.StartedAt,
        build.FinishedAt,
        build.UpdatedAt);
}
