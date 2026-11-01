// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;
using SilexGis.Domain.Access;

namespace SilexGis.Api.Features.Graph;

/// <summary>
/// The relations graph: one hop around one item per request, every category read from the
/// place that already holds it and filtered exactly as that relation's own route filters it.
/// The route writes nothing, so it refuses nobody who is signed in; what it returns is
/// filtered, and an item the caller may not read answers exactly like one that does not exist.
/// </summary>
public static class GraphEndpoints
{
    public const string SeedNotFoundCode = "graph.seed.not_found";

    public static RouteGroupBuilder MapGraphEndpoints(this RouteGroupBuilder api)
    {
        var graph = api.MapGroup("/graph").WithTags("Graph");

        graph.MapGet("/neighbourhood", NeighbourhoodAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "One hop around one item: its neighbours in each asked category as the caller may see them. "
                + "type + id name the item; categories (comma list, default all) narrows the families; "
                + "category + offset pages one family; limit (1-200, default 40) caps each family's top-level items.");

        graph.MapGet("/search", SearchAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary(
                "Items to start exploring from, found by name and offered only when the caller may read them. "
                + "q is the typed text (blank answers nothing); types (comma list, default every searchable kind) "
                + "narrows the kinds; limit (1-20, default 5) caps the hits per kind.");

        return api;
    }

    private static async Task<Results<Ok<GraphNeighbourhoodDto>, UnauthorizedHttpResult, ProblemHttpResult>> NeighbourhoodAsync(
        [AsParameters] GraphNeighbourhoodRequest request,
        IValidator<GraphNeighbourhoodRequest> validator,
        GraphNeighbourhoodService graph,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return ApiProblems.BadRequest(first.ErrorCode, first.ErrorMessage);
        }

        // Ids are compared as text from here on, so they are put in the one spelling every
        // resolver answers in: a Guid in its lower-case form, a tag id without leading zeros.
        GraphWire.TryParseType(request.Type, out var type);
        var id = request.Id!.Trim();
        var seed = new GraphSeed(
            type,
            type == GraphNodeType.Tag
                ? long.Parse(id, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Guid.Parse(id).ToString());
        if (await graph.SeedAsync(seed, ctx, ct) is not { } seedDisplay)
        {
            return ApiProblems.NotFound(SeedNotFoundCode);
        }

        IReadOnlyCollection<GraphCategory> categories = request.Categories is { } named
            && GraphNeighbourhoodRequestValidator.SplitCategories(named) is { Count: > 0 } list
                ? list
                : Enum.GetValues<GraphCategory>();
        GraphCategory? paged = GraphWire.TryParseCategory(request.Category, out var only) ? only : null;

        return TypedResults.Ok(await graph.NeighbourhoodAsync(
            seed,
            seedDisplay,
            ctx,
            categories,
            paged,
            request.Offset ?? 0,
            request.Limit ?? GraphNeighbourhoodRequest.DefaultLimit,
            ct));
    }

    private static async Task<Results<Ok<List<GraphSearchHitDto>>, UnauthorizedHttpResult, ProblemHttpResult>> SearchAsync(
        [AsParameters] GraphSearchRequest request,
        IValidator<GraphSearchRequest> validator,
        GraphSearchService search,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return ApiProblems.BadRequest(first.ErrorCode, first.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(request.Q))
        {
            return TypedResults.Ok(new List<GraphSearchHitDto>());
        }

        IReadOnlyCollection<GraphNodeType> types = request.Types is { } named
            && GraphSearchRequestValidator.SplitTypes(named) is { Count: > 0 } list
                ? list
                : GraphSearchService.Searchable;
        var hits = await search.SearchAsync(
            ctx, request.Q.Trim(), types, request.Limit ?? GraphSearchRequest.DefaultLimit, ct);
        return TypedResults.Ok(hits.ToList());
    }
}
