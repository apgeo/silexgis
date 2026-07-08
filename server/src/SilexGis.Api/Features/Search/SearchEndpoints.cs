// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Caves;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Search;

/// <summary>A surface-feature hit; Center is a representative point for map fly-to.</summary>
public sealed record SearchFeatureItemDto(Guid Id, string? Name, long FeatureTypeId, GeoJsonPoint Center);

public sealed record SearchResultDto(
    IReadOnlyList<CaveListItemDto> Caves,
    IReadOnlyList<SearchFeatureItemDto> Features);

/// <summary>
/// Unified search over caves and surface features. Accent-insensitive (unaccent) so
/// "pestera" matches "Peștera". Word matches use the GIN-indexed generated search_vector
/// columns; substring/code matches fall back to ILIKE.
/// </summary>
public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", SearchAsync)
            .WithTags("Search")
            .WithSummary("Searches caves by name/toponyms (accent-insensitive).");
        return api;
    }

    private static async Task<Results<Ok<SearchResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> SearchAsync(
        string q,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return ApiProblems.BadRequest("search.query_too_short", "Provide at least 2 characters.");
        }

        var term = q.Trim();
        var pattern = $"%{term}%";
        var caves = await db.Caves.AsNoTracking()
            .VisibleTo(user)
            .Where(c =>
                // Indexed word search (GIN over the generated search_vector)…
                EF.Property<NpgsqlTypes.NpgsqlTsVector>(c, "SearchVector")
                    .Matches(EF.Functions.PlainToTsQuery("simple", EF.Functions.Unaccent(term)))
                // …plus substring fallback for partial words and codes.
                || EF.Functions.ILike(EF.Functions.Unaccent(c.Name), EF.Functions.Unaccent(pattern))
                || (c.IdentificationCode != null && EF.Functions.ILike(c.IdentificationCode, pattern)))
            .OrderBy(c => c.Name)
            .Take(20)
            .ToListAsync(ct);

        var features = await db.SurfaceFeatures.AsNoTracking()
            .VisibleTo(user)
            .Where(f =>
                EF.Property<NpgsqlTypes.NpgsqlTsVector>(f, "SearchVector")
                    .Matches(EF.Functions.PlainToTsQuery("simple", EF.Functions.Unaccent(term)))
                || (f.Name != null && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern))))
            .OrderBy(f => f.Name)
            .Take(10)
            .ToListAsync(ct);

        return TypedResults.Ok(new SearchResultDto(
            [.. caves.Select(c => c.ToListItem(user, access.Value.LocationGridMeters))],
            [.. features.Select(f => new SearchFeatureItemDto(
                f.Id, f.Name, f.FeatureTypeId, GeoJsonPoint.From((NetTopologySuite.Geometries.Point)f.Geom.Centroid)))]));
    }
}
