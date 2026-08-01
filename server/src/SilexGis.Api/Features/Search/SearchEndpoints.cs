// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Search;

/// <summary>
/// One hit from the feature registry, whatever its kind. <paramref name="Kind"/> tells the
/// client which detail route to open (every feature also resolves under /features/{id});
/// <paramref name="TypeCode"/> names the data-level kind of generic features and is null for
/// the kinds the schema types itself.
/// </summary>
public sealed record SearchFeatureItemDto(Guid Id, FeatureKind Kind, string? Name, string? TypeCode);

/// <summary>A trip-log hit.</summary>
public sealed record SearchTripItemDto(Guid Id, string Title, DateOnly TripDate);

public sealed record SearchResultDto(
    IReadOnlyList<SearchFeatureItemDto> Features,
    IReadOnlyList<SearchTripItemDto> Trips);

/// <summary>
/// Unified search over every feature kind — caves, their entrances and centerlines, and the
/// data-driven kinds — plus trip logs. Accent-insensitive (unaccent) so "pestera" matches
/// "Peștera". Word matches use the GIN-indexed generated search_vector columns;
/// substring/code matches fall back to ILIKE.
///
/// Results deliberately carry NO coordinates. Search is a navigation aid, so keeping
/// location out of it means protected features need no second obfuscation code path here —
/// the detail endpoint the client follows applies the location rules. Listing a protected
/// feature by name is not a location disclosure: which caves exist has always been public
/// on this installation, only where they are is guarded.
///
/// Centerlines are the exception, because that reasoning does not reach them: a centerline
/// IS the cave's course, so under protection it is withheld outright everywhere else — the
/// registry, the resolver, the cave's own list, the map overlay and exports all refuse to
/// admit it exists. Naming one here would disclose that a cave whose position is guarded has
/// a survey at all, so they are dropped from results the same way.
/// </summary>
public static class SearchEndpoints
{
    private const int MinimumQueryLength = 2;
    private const int FeatureLimit = 20;
    private const int TripLimit = 10;

    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", SearchAsync)
            .WithTags("Search")
            .WithSummary("Searches features of every kind and trip logs (accent-insensitive).");
        return api;
    }

    private static async Task<Results<Ok<SearchResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> SearchAsync(
        string q,
        string? kind,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < MinimumQueryLength)
        {
            return ApiProblems.BadRequest("search.query_too_short", $"Provide at least {MinimumQueryLength} characters.");
        }

        // Enum query parameters arrive as the camelCase strings the JSON contract uses;
        // parse case-insensitively (route binding's Enum.TryParse would not).
        FeatureKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<FeatureKind>(kind, ignoreCase: true, out var kindValue) || !Enum.IsDefined(kindValue))
            {
                return ApiProblems.BadRequest("feature.kind_invalid", $"Unknown feature kind '{kind}'.");
            }

            kindFilter = kindValue;
        }

        var term = q.Trim();
        var pattern = $"%{term}%";

        // One query over the supertype covers every kind. The supertype vector indexes
        // name + description; caves additionally carry their own vector on the subtype row
        // (other toponyms + cadastral code), so both are consulted — ORing them is what
        // makes a search for a cadastral code find its cave.
        // The hit budget is shared across kinds, so a caller after one kind (the cave
        // pickers) must narrow the query rather than sift the answer: twenty matching
        // dolines would otherwise push every cave out of the result and leave the picker
        // looking empty while a matching cave exists.
        var candidates = db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls);
        if (kindFilter is { } wanted)
        {
            candidates = candidates.Where(f => f.Kind == wanted);
        }

        var hits = await candidates
            .Where(f =>
                EF.Property<NpgsqlTsVector>(f, "SearchVector")
                    .Matches(EF.Functions.PlainToTsQuery("simple", EF.Functions.Unaccent(term)))
                // Substring fallback for partial words the tsquery would not match.
                || (f.Name != null && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)))
                || (f.Cave != null
                    && (EF.Property<NpgsqlTsVector>(f.Cave, "SearchVector")
                            .Matches(EF.Functions.PlainToTsQuery("simple", EF.Functions.Unaccent(term)))
                        || (f.Cave.IdentificationCode != null
                            && EF.Functions.ILike(f.Cave.IdentificationCode, pattern)))))
            .OrderBy(f => f.Name)
            .Take(FeatureLimit)
            .Select(f => new { f.Id, f.Kind, f.Name, f.FeatureTypeId })
            .ToListAsync(ct);

        // Withhold protected centerlines. Filtered after the query rather than before it:
        // the exclusion the registry does up front exists so its paging total stays right,
        // and search has no total — while re-running the match predicate to pre-exclude
        // would double the text-search work on every request for no gain. The cost here is
        // one id lookup, and only when a centerline actually matched.
        var centerlineIds = hits.Where(h => h.Kind == FeatureKind.Centerline).Select(h => h.Id).ToList();
        if (centerlineIds.Count > 0)
        {
            var exact = await protection.ExactViewIdsAsync(user, centerlineIds, ct);
            hits = hits.Where(h => h.Kind != FeatureKind.Centerline || exact.Contains(h.Id)).ToList();
        }

        var typeIds = hits.Where(h => h.FeatureTypeId != null).Select(h => h.FeatureTypeId!.Value).Distinct().ToList();
        var typeCodes = new Dictionary<long, string>();
        if (typeIds.Count > 0)
        {
            typeCodes = await db.FeatureTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        }

        // Trip logs are not features and keep their own query: they have no search_vector,
        // only free text to match.
        var trips = await db.TripLogs.AsNoTracking()
            .VisibleTo(user, db.ObjectAcls, AttachedEntityType.TripLog)
            .Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Title), EF.Functions.Unaccent(pattern))
                || (x.Description != null && EF.Functions.ILike(EF.Functions.Unaccent(x.Description), EF.Functions.Unaccent(pattern))))
            .OrderByDescending(x => x.TripDate)
            .Take(TripLimit)
            .Select(x => new SearchTripItemDto(x.Id, x.Title, x.TripDate))
            .ToListAsync(ct);

        return TypedResults.Ok(new SearchResultDto(
            [.. hits.Select(h => new SearchFeatureItemDto(
                h.Id,
                h.Kind,
                h.Name,
                h.FeatureTypeId is null ? null : typeCodes.GetValueOrDefault(h.FeatureTypeId.Value)))],
            trips));
    }
}
