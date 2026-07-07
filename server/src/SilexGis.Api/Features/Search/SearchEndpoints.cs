// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Caves;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Search;

public sealed record SearchResultDto(IReadOnlyList<CaveListItemDto> Caves);

/// <summary>
/// Unified search (03-api-spec.md §3) — Phase 1 covers caves. Accent-insensitive
/// (unaccent) so "pestera" matches "Peștera". Full-text tsvector ranking is a planned
/// upgrade within Phase 1 polish.
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

    private static async Task<IResult> SearchAsync(
        string q,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return ApiProblems.BadRequest("search.query_too_short", "Provide at least 2 characters.");
        }

        var pattern = $"%{q.Trim()}%";
        var caves = await db.Caves.AsNoTracking()
            .VisibleTo(user)
            .Where(c =>
                EF.Functions.ILike(EF.Functions.Unaccent(c.Name), EF.Functions.Unaccent(pattern))
                || (c.OtherToponyms != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(c.OtherToponyms), EF.Functions.Unaccent(pattern)))
                || (c.IdentificationCode != null && EF.Functions.ILike(c.IdentificationCode, pattern)))
            .OrderBy(c => c.Name)
            .Take(20)
            .ToListAsync(ct);

        return TypedResults.Ok(new SearchResultDto(
            [.. caves.Select(c => c.ToListItem(user, access.Value.LocationGridMeters))]));
    }
}
