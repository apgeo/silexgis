// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
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

/// <summary>
/// A document whose text matches, quoted at the stretch that matched it.
/// </summary>
/// <param name="FileId">
/// The file somebody uploaded — what a reader downloads, whatever this installation had to make
/// of it in order to draw its pages.
/// </param>
/// <param name="Division">
/// What <paramref name="PageNumber"/> counts, and it counts divisions of the artifact this
/// installation draws pages from, so that the number and the picture shown for it are the same
/// thing. That is the upload itself where the format paginates: a PDF has pages, a spreadsheet
/// read as it stands has sheets and a presentation slides. Where an optional converter has laid
/// an office document out into a portable copy, the words were read off that copy and the number
/// is one of its pages. Where nothing numbers anything the whole text arrives as one row however
/// long it is, and this says so rather than letting an interface announce "page 1 of 1" about a
/// forty-page report.
/// </param>
/// <param name="Snippet">
/// The matching stretch of that division, matched words wrapped in <c>[[</c>…<c>]]</c>. Markers
/// rather than markup: the value is data, and whatever renders it decides what a match looks
/// like without being tempted to trust the text. The quotation keeps the diacritics its author
/// wrote even when the search that found it did not.
/// </param>
public sealed record SearchDocumentItemDto(
    Guid Id,
    string Title,
    Guid FileId,
    string MimeType,
    Guid VersionId,
    int VersionNumber,
    bool IsCurrentVersion,
    int PageNumber,
    PageDivision Division,
    string Snippet);

public sealed record SearchResultDto(
    IReadOnlyList<SearchFeatureItemDto> Features,
    IReadOnlyList<SearchTripItemDto> Trips,
    PagedResult<SearchDocumentItemDto> Documents);

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
///
/// Documents are searched by what they say, and that is a third question rather than a third
/// spelling of the first: the text of a document is matched by the content query, whose whole
/// answer — which rows, in what order, and how many of them — is decided inside one statement
/// that already carries the caller's read walk. Being attached to a cave whose position is
/// guarded is not a reason to withhold a document, hide it from this list or strip its text; a
/// document a caller may read is found by its words. What must not be disclosed is the pairing,
/// and a hit here names no feature, carries no geometry and says nothing about what the document
/// hangs off, so there is no pairing in it to withhold.
/// </summary>
public static class SearchEndpoints
{
    private const int MinimumQueryLength = 2;
    private const int FeatureLimit = 20;
    private const int TripLimit = 10;

    /// <summary>
    /// How many documents one page of content hits holds. Smaller than the feature budget and
    /// paged where the other two sections are not, because this is the only section where the
    /// interesting answer can honestly be the hundredth one: a phrase in a survey archive is
    /// looked for, while a place is looked up.
    /// </summary>
    private const int DocumentPageSize = 10;

    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", SearchAsync)
            .WithTags("Search")
            .WithSummary("Searches features of every kind, trip logs and document text (accent-insensitive).");
        return api;
    }

    /// <param name="documentPage">
    /// Which page of document hits to return, 1-based. The features and trip sections are
    /// capped budgets and ignore it.
    /// </param>
    /// <param name="includeSuperseded">
    /// Whether to search revisions that have been replaced. Off by default, and never a way past
    /// the rule that an old revision is readable only by someone who may also replace the
    /// document: the walk that decides it runs inside the content query, so asking for them
    /// widens nothing for a caller who may only read.
    /// </param>
    private static async Task<Results<Ok<SearchResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> SearchAsync(
        string q,
        string? kind,
        int? documentPage,
        bool? includeSuperseded,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
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
        var candidates = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
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
        // and this section publishes no total to skew — while re-running the match predicate
        // to pre-exclude would double the text-search work on every request for no gain. The
        // cost here is
        // one id lookup, and only when a centerline actually matched.
        var centerlineIds = hits.Where(h => h.Kind == FeatureKind.Centerline).Select(h => h.Id).ToList();
        if (centerlineIds.Count > 0)
        {
            var exact = await protection.ExactViewIdsAsync(ctx, centerlineIds, ct);
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
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Title), EF.Functions.Unaccent(pattern))
                || (x.Description != null && EF.Functions.ILike(EF.Functions.Unaccent(x.Description), EF.Functions.Unaccent(pattern))))
            .OrderByDescending(x => x.TripDate)
            .Take(TripLimit)
            .Select(x => new SearchTripItemDto(x.Id, x.Title, x.TripDate))
            .ToListAsync(ct);

        // Document text is the one section with a total beside it, and the total comes out of
        // the same statement as the rows for the reason a number beside a filtered list always
        // has to: computed separately it would count rows the list declined to show and announce
        // exactly what the rules had withheld. The other two sections publish no total because
        // they have none — they are budgets, not pages.
        //
        // Reach through an attached object is part of the document read rule, so it is part of
        // this query too: a document found by opening the cave it hangs off must also be found by
        // a phrase in it, or the archive answers two different questions depending on which door
        // was used. It is resolved before the statement and passed into it, never applied to the
        // rows afterwards — a filter over results would leave the total, the ranking and the page
        // boundaries computed over documents the caller never sees.
        //
        // The question is narrowed to the documents that match the words, that no earlier band of
        // the rule already admits, and whose file hangs on something at all — everything else is
        // an answer already known to be no. What remains scales with how much of the archive
        // matched and is withheld, not with the twenty rows returned, so a common word typed by a
        // caller who may read almost nothing is the expensive case and stays expensive: capping it
        // would make whether a document is found depend on how many others happened to match.
        var page = Math.Max(1, documentPage ?? 1);
        var withheldMatches = await DocumentContentSql.WithheldMatchIdsAsync(
            db, ctx, term, includeSuperseded ?? false, ct);
        var reached = await DocumentAccessRules.ReachedByAttachmentAsync(db, ctx, withheldMatches, ct);
        var contentHits = await DocumentContentSql.SearchAsync(
            db,
            ctx,
            term,
            DocumentPageSize,
            (page - 1) * DocumentPageSize,
            includeSuperseded ?? false,
            reached,
            ct);

        var documents = new PagedResult<SearchDocumentItemDto>(
            [.. contentHits.Select(h => new SearchDocumentItemDto(
                h.DocumentId,
                h.Title,
                h.FileId,
                h.MimeType,
                h.VersionId,
                h.VersionNumber,
                h.IsCurrentVersion,
                h.PageNumber,
                h.Division,
                h.Snippet))],
            page,
            DocumentPageSize,
            contentHits.Count > 0 ? contentHits[0].TotalDocuments : 0);

        return TypedResults.Ok(new SearchResultDto(
            [.. hits.Select(h => new SearchFeatureItemDto(
                h.Id,
                h.Kind,
                h.Name,
                h.FeatureTypeId is null ? null : typeCodes.GetValueOrDefault(h.FeatureTypeId.Value)))],
            trips,
            documents));
    }
}
