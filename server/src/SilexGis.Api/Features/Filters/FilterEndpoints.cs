// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Filters;

namespace SilexGis.Api.Features.Filters;

/// <summary>
/// One way to ask what this installation holds, across every kind of thing it holds.
/// </summary>
/// <remarks>
/// <para>
/// The alternative was a filter per list screen, which is what was here before: each page with its
/// own parameters, its own idea of what "search" meant, and its own opportunity to forget a
/// visibility check. One route means one place where a question is validated, compiled and composed
/// around what the caller may see — and one place to read when somebody asks whether a filter can
/// be used to learn something it should not.
/// </para>
/// <para>
/// Nothing here decides what any caller may see. Each world does that for its own rows, and the
/// order in which that happens is fixed by the base they all share; this route checks the question
/// is well formed and hands it on.
/// </para>
/// </remarks>
public static class FilterEndpoints
{
    /// <summary>
    /// Above this many worlds a request stops counting.
    /// </summary>
    /// <remarks>
    /// A count is a whole pass over a world; asking for nine at once is nine of them for a number
    /// nobody reads, since a screen showing nine kinds of result has no room to show a total beside
    /// each. Two is where a person is still comparing "how many of these against how many of those",
    /// which is the case where the number is worth what it costs.
    /// </remarks>
    private const int CountingWorldLimit = 2;

    public static RouteGroupBuilder MapFilterEndpoints(this RouteGroupBuilder api)
    {
        var filters = api.MapGroup("/filters").WithTags("Filters");

        filters.MapGet("/vocabulary", VocabularyAsync)
            .WithSummary("What each kind of object can be filtered by, and the limits on a filter.");

        filters.MapPost("/query", QueryAsync)
            .WithSummary("Runs a filter across the kinds of object it names.");

        filters.MapPost("/resolve", ResolveAsync)
            .WithSummary("Describes rows named by id, for a control showing an existing choice.");

        return api;
    }

    private static async Task<Results<Ok<FilterVocabularyResponse>, ProblemHttpResult>> VocabularyAsync(
        FilterWorldRegistry registry,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return ApiProblems.Forbidden();
        }

        var worlds = await registry.VocabulariesAsync(ctx, ct);
        return TypedResults.Ok(new FilterVocabularyResponse([.. worlds.Select(ToDto)], Limits));
    }

    private static async Task<Results<Ok<FilterQueryResponse>, ProblemHttpResult>> QueryAsync(
        FilterQueryRequest request,
        FilterWorldRegistry registry,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return ApiProblems.Forbidden();
        }

        var document = request.Document;
        if (document is null)
        {
            return ApiProblems.BadRequest("filter.malformed", "A filter has to say what it is about.");
        }

        // Validated against the vocabularies this caller may ask about, not against a global list.
        // A world absent from theirs is refused in the same words as a world that does not exist,
        // so a refusal never distinguishes "not for you" from "no such thing".
        var vocabularies = (await registry.VocabulariesAsync(ctx, ct))
            .ToDictionary(v => v.World, StringComparer.Ordinal);

        var errors = FilterValidation.Validate(document, vocabularies);
        if (errors.Count > 0)
        {
            return ApiProblems.BadRequest("filter.invalid", string.Join(" ", errors));
        }

        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var counted = document.Scope.Count <= CountingWorldLimit;

        var results = new List<FilterWorldResultDto>(document.Scope.Count);
        foreach (var scope in document.Scope)
        {
            // Present: validation refused any world this caller cannot ask about.
            var world = registry.Find(scope.World)!;
            var answer = await world.QueryAsync(
                new WorldQuery(
                    ctx,
                    scope.Where,
                    document.Sort,
                    document.Descending,
                    Anchor: null,
                    (page - 1) * pageSize,
                    pageSize,
                    counted),
                ct);

            results.Add(new FilterWorldResultDto(
                scope.World,
                [.. answer.Hits.Select(ToDto)],
                answer.Total));
        }

        return TypedResults.Ok(new FilterQueryResponse(results, page, pageSize, counted));
    }

    /// <summary>
    /// Describes rows somebody already chose.
    /// </summary>
    /// <remarks>
    /// The same walk as a query, narrowed to the named rows, so a control showing a saved choice
    /// cannot become a way to confirm that a row exists. An id the caller may not see comes back
    /// missing rather than refused — a refusal would answer the question the omission avoids, and
    /// the number returned is therefore never a count of what is there.
    /// </remarks>
    private static async Task<Results<Ok<IReadOnlyList<FilterHitDto>>, ProblemHttpResult>> ResolveAsync(
        FilterResolveRequest request,
        FilterWorldRegistry registry,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return ApiProblems.Forbidden();
        }

        var world = registry.Find(request.World ?? string.Empty);
        if (world is null)
        {
            return ApiProblems.BadRequest("filter.unknown_world");
        }

        var ids = (request.Ids ?? []).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FilterHitDto>>([]);
        }

        if (ids.Length > FilterValidation.MaxValuesPerCondition)
        {
            return ApiProblems.BadRequest(
                "filter.too_many_ids",
                $"At most {FilterValidation.MaxValuesPerCondition} at a time.");
        }

        var answer = await world.QueryAsync(
            new WorldQuery(
                ctx,
                Where: null,
                SortKey.Updated,
                Descending: true,
                Anchor: null,
                Skip: 0,
                Take: ids.Length,
                WantTotal: false,
                Ids: ids),
            ct);

        return TypedResults.Ok<IReadOnlyList<FilterHitDto>>([.. answer.Hits.Select(ToDto)]);
    }

    private static FilterWorldVocabularyDto ToDto(WorldVocabulary vocabulary) =>
        new(vocabulary.World,
            vocabulary.LabelKey,
            // The operator list travels with each field rather than being looked up from the kind
            // on the far side, so there is one table and the builder cannot drift from it.
            [.. vocabulary.Fields.Select(f => new FilterFieldDto(
                f.Key, f.LabelKey, f.Kind, FilterOps.For(f.Kind), f.Options, f.Sortable))],
            vocabulary.Sorts);

    private static FilterHitDto ToDto(FilterHit hit) =>
        new(hit.World, hit.Id, hit.Title, hit.Subtitle, hit.Symbol, hit.Placeable);

    private static FilterLimitsDto Limits { get; } = new(
        FilterValidation.MaxNodes,
        FilterValidation.MaxDepth,
        FilterValidation.MaxValuesPerCondition,
        FilterValidation.MaxWorlds,
        FilterValidation.MaxTextValueLength,
        Paging.MaxPageSize);
}
