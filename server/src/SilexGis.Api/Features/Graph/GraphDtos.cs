// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SilexGis.Api.Common;
using SilexGis.Api.Common.Graph;

namespace SilexGis.Api.Features.Graph;

/// <summary>
/// One hop around one item: the item, its neighbours in the asked categories as the viewer
/// may see them, the edges between them, and per category how many top-level items there are.
/// Carries no position of any kind — no coordinate, geometry, extent or distance — and its
/// order is never derived from one.
/// </summary>
public sealed record GraphNeighbourhoodDto(
    GraphNodeDto Seed,
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges,
    IReadOnlyList<GraphCategoryCountDto> Counts);

public sealed record GraphNodeDto(
    /// <summary>"&lt;type&gt;:&lt;id&gt;"; a link hub "resLink:&lt;linkId&gt;"; a restricted
    /// placeholder "restricted:&lt;memberId&gt;", one per link member, never shared.</summary>
    string Key,
    /// <summary>The node type's wire name; a placeholder carries the type of what it stands for.</summary>
    string Type,
    /// <summary>The item's id; null for a placeholder.</summary>
    string? Id,
    string? Title,
    string? Subtitle,
    /// <summary>A feature's kind (cave, caveEntrance, centerline, generic) for styling; null otherwise.</summary>
    string? Kind,
    bool Restricted,
    /// <summary>Whether asking for this node's neighbourhood can answer anything: false for
    /// placeholders, for link hubs (their members are already present) and for plain files.</summary>
    bool Expandable,
    string? Route,
    /// <summary>Link hubs only.</summary>
    string? ShortCode,
    /// <summary>Link hubs only: the members the viewer is shown, placeholders included.</summary>
    int? MemberCount);

public sealed record GraphEdgeDto(
    string Key,
    string Category,
    /// <summary>The relation code within the category; "unspecified" for a link without a relation type.</summary>
    string Relation,
    /// <summary>Custom link relations only — seeded ones are translated by code.</summary>
    string? RelationName,
    string? InverseName,
    bool Directed,
    /// <summary>Resource links only: whether the member is the link's main member.</summary>
    bool? IsMain,
    string Source,
    string Target,
    /// <summary>Resource links only: the member's anchor kind; null when the member is the whole target.</summary>
    string? AnchorKind);

/// <summary>Shown = top-level items in this answer; Total = all of them the viewer would reach by paging.</summary>
public sealed record GraphCategoryCountDto(string Category, int Shown, int Total);

/// <summary>Query of the neighbourhood route; see the route's summary for each parameter.</summary>
public sealed record GraphNeighbourhoodRequest(
    [property: FromQuery(Name = "type")] string? Type,
    [property: FromQuery(Name = "id")] string? Id,
    [property: FromQuery(Name = "categories")] string? Categories,
    [property: FromQuery(Name = "category")] string? Category,
    [property: FromQuery(Name = "offset")] int? Offset,
    [property: FromQuery(Name = "limit")] int? Limit)
{
    public const int DefaultLimit = 40;
    public const int MaxLimit = 200;
}

/// <summary>
/// Each rule carries the stable code its refusal is answered with. The route runs this through the
/// shared validation filter, which answers with that code when every failing rule names the same
/// one, and with the generic validation code when a request is wrong in more than one way.
/// </summary>
public sealed class GraphNeighbourhoodRequestValidator : AbstractValidator<GraphNeighbourhoodRequest>
{
    public const string TypeUnknownCode = "graph.type.unknown";
    public const string IdInvalidCode = "graph.id.invalid";
    public const string CategoryUnknownCode = "graph.category.unknown";
    public const string PagingInvalidCode = "graph.paging.invalid";

    public GraphNeighbourhoodRequestValidator()
    {
        RuleFor(x => x.Type)
            .Must(t => GraphWire.TryParseType(t, out var type) && GraphWire.IsSeedType(type))
            .WithProblemCode(TypeUnknownCode)
            .WithMessage("type must name a graph node type that can be explored.");
        RuleFor(x => x.Id)
            .Must((request, id) => IsValidId(request.Type, id))
            .When(x => GraphWire.TryParseType(x.Type, out _))
            .WithProblemCode(IdInvalidCode)
            .WithMessage("id must be a uuid, or a positive integer for a tag.");
        RuleFor(x => x.Categories)
            .Must(c => c is null || SplitCategories(c) is not null)
            .WithProblemCode(CategoryUnknownCode)
            .WithMessage("categories must be a comma-separated list of graph categories.");
        RuleFor(x => x.Category)
            .Must(c => c is null || GraphWire.TryParseCategory(c, out _))
            .WithProblemCode(CategoryUnknownCode)
            .WithMessage("category must name a graph category.");
        RuleFor(x => x.Offset)
            .Must((request, offset) => offset is null || (offset >= 0 && request.Category is not null))
            .WithProblemCode(PagingInvalidCode)
            .WithMessage("offset must be zero or more, and is only accepted together with category.");
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, GraphNeighbourhoodRequest.MaxLimit)
            .When(x => x.Limit is not null)
            .WithProblemCode(PagingInvalidCode)
            .WithMessage($"limit must be between 1 and {GraphNeighbourhoodRequest.MaxLimit}.");
    }

    private static bool IsValidId(string? type, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !GraphWire.TryParseType(type, out var parsed))
        {
            return false;
        }

        return parsed == GraphNodeType.Tag
            ? long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var tagId) && tagId > 0
            : Guid.TryParse(id, out var guid) && guid != Guid.Empty;
    }

    /// <summary>The named categories, or null when any name is unknown. Blank entries are ignored.</summary>
    public static IReadOnlyList<GraphCategory>? SplitCategories(string value)
    {
        var parsed = new List<GraphCategory>();
        foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!GraphWire.TryParseCategory(name, out var category))
            {
                return null;
            }

            parsed.Add(category);
        }

        return parsed;
    }
}

/// <summary>
/// One item somebody may start exploring from: the node it would be drawn as, so a chosen hit is
/// asked about with exactly its <see cref="Type"/> and <see cref="Id"/>.
/// </summary>
public sealed record GraphSearchHitDto(
    /// <summary>"&lt;type&gt;:&lt;id&gt;", the key its node carries in a neighbourhood.</summary>
    string Key,
    string Type,
    string Id,
    string? Title,
    string? Subtitle,
    /// <summary>A feature's kind for styling; null otherwise.</summary>
    string? Kind,
    string? Route);

/// <summary>Query of the seed search; see the route's summary for each parameter.</summary>
public sealed record GraphSearchRequest(
    [property: FromQuery(Name = "q")] string? Q,
    [property: FromQuery(Name = "types")] string? Types,
    [property: FromQuery(Name = "limit")] int? Limit)
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 20;

    /// <summary>The longest text searched for; the same ceiling the filter query puts on a typed value.</summary>
    public const int MaxQueryLength = SilexGis.Domain.Filters.FilterValidation.MaxTextValueLength;
}

public sealed class GraphSearchRequestValidator : AbstractValidator<GraphSearchRequest>
{
    public const string QueryTooLongCode = "graph.query.too_long";

    public GraphSearchRequestValidator()
    {
        RuleFor(x => x.Q)
            .Must(q => q is null || q.Trim().Length <= GraphSearchRequest.MaxQueryLength)
            .WithProblemCode(QueryTooLongCode)
            .WithMessage($"q must be at most {GraphSearchRequest.MaxQueryLength} characters.");
        RuleFor(x => x.Types)
            .Must(t => t is null || SplitTypes(t) is not null)
            .WithProblemCode(GraphNeighbourhoodRequestValidator.TypeUnknownCode)
            .WithMessage("types must be a comma-separated list of graph node types that can be searched.");
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, GraphSearchRequest.MaxLimit)
            .When(x => x.Limit is not null)
            .WithProblemCode(GraphNeighbourhoodRequestValidator.PagingInvalidCode)
            .WithMessage($"limit must be between 1 and {GraphSearchRequest.MaxLimit}.");
    }

    /// <summary>The named types, or null when any name is unknown or cannot be searched. Blank entries are ignored.</summary>
    public static IReadOnlyList<GraphNodeType>? SplitTypes(string value)
    {
        var parsed = new List<GraphNodeType>();
        foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!GraphWire.TryParseType(name, out var type) || !GraphSearchService.Searchable.Contains(type))
            {
                return null;
            }

            if (!parsed.Contains(type))
            {
                parsed.Add(type);
            }
        }

        return parsed;
    }
}
