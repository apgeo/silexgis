// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Filters;

namespace SilexGis.Api.Features.Filters;

/// <summary>
/// What a person asked.
/// </summary>
/// <remarks>
/// No anchor. When proximity is served, the place a filter is measured from will arrive here with
/// the request rather than inside the document — a saved filter that stored where somebody was
/// looking would be a stored, shareable, negatable statement about a position.
/// </remarks>
public sealed record FilterQueryRequest(
    FilterDocument Document,
    int Page = 1,
    int PageSize = 25);

/// <summary>One row, as every world returns it.</summary>
/// <param name="Placeable">
/// Whether this caller may be shown where this is. Not a position and never accompanied by one:
/// anything that puts a result on a map asks the map, which is the single place that decides how
/// much of a protected position anybody sees.
/// </param>
public sealed record FilterHitDto(
    string World,
    Guid Id,
    string Title,
    string? Subtitle,
    string? Symbol,
    bool Placeable);

/// <summary>One world's answer.</summary>
/// <param name="Total">
/// Absent when it was not counted. Absent rather than zero, so a screen can tell "we did not count"
/// from "there are none" instead of showing a confident nought for both.
/// </param>
public sealed record FilterWorldResultDto(
    string World,
    IReadOnlyList<FilterHitDto> Hits,
    int? Total);

/// <param name="Counted">
/// Whether totals were computed at all. Said once for the whole answer rather than inferred from
/// every world's null, because "nobody counted" is a property of the request.
/// </param>
public sealed record FilterQueryResponse(
    IReadOnlyList<FilterWorldResultDto> Worlds,
    int Page,
    int PageSize,
    bool Counted);

/// <summary>Rows named directly, for a control that has to show what somebody already picked.</summary>
public sealed record FilterResolveRequest(string World, IReadOnlyList<Guid> Ids);

/// <summary>
/// What the builder can offer, and the limits it should enforce before sending anything.
/// </summary>
/// <remarks>
/// The limits are published rather than left for the server to discover, so a person who has built
/// something too large is told while they are building it. The server checks them again regardless:
/// this copy is a courtesy to the interface, never the enforcement.
/// </remarks>
public sealed record FilterVocabularyResponse(
    IReadOnlyList<FilterWorldVocabularyDto> Worlds,
    FilterLimitsDto Limits);

/// <summary>One world, as the builder needs to know it.</summary>
/// <param name="LabelKey">
/// A translation key, never a label. A filter built in Romanian and opened in English has to read
/// as English, and a stored or transmitted label would not.
/// </param>
public sealed record FilterWorldVocabularyDto(
    string World,
    string LabelKey,
    IReadOnlyList<FilterFieldDto> Fields,
    IReadOnlyList<SortKey> Sorts);

/// <param name="Ops">
/// Which operators this field admits, sent rather than derived from the kind. The client would
/// otherwise carry its own copy of that table, and the day the two disagree is the day a control
/// offers something the server refuses.
/// </param>
/// <param name="Options">
/// Where the choices come from for a field picked from a list, or null when it takes free input.
/// </param>
public sealed record FilterFieldDto(
    string Key,
    string LabelKey,
    FieldKind Kind,
    IReadOnlyList<FilterOp> Ops,
    string? Options,
    bool Sortable);

public sealed record FilterLimitsDto(
    int MaxNodes,
    int MaxDepth,
    int MaxValuesPerCondition,
    int MaxWorlds,
    int MaxTextValueLength,
    int MaxPageSize);
