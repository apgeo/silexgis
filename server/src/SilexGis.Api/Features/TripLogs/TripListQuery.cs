// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Reads the trip listing's query string into a filter, refusing a word it does not know.
/// </summary>
/// <remarks>
/// <para>
/// Parsed here rather than by route binding, for the reason the lifecycle word already was:
/// binding a bad word would answer with a bare 400 carrying no code, and a client cannot tell
/// that apart from any other refusal — nor say which control the reader has to put right.
/// </para>
/// <para>
/// A facet's chosen values travel as one comma-separated parameter rather than as a repeated
/// one. A shared listing is a link somebody pastes into a message, so the address is read by
/// people as well as by browsers, and one readable key per facet is what makes a filter
/// somebody can check by looking at it. Blank entries are dropped rather than refused, because
/// a control that clears its last choice by leaving a trailing comma has not made a mistake.
/// </para>
/// </remarks>
internal static class TripListQuery
{
    /// <summary>A lifecycle word a trip does not have.</summary>
    public const string StateInvalidCode = "trip_log.state_invalid";

    /// <summary>An audience word this application does not have.</summary>
    public const string VisibilityInvalidCode = "trip_log.visibility_invalid";

    /// <summary>A trip-type filter that is not a number.</summary>
    public const string TypeInvalidCode = "trip_log.type_invalid";

    /// <summary>An order the listing cannot be put in.</summary>
    public const string SortInvalidCode = "trip_log.sort_invalid";

    /// <summary>A person filter that is not an identifier.</summary>
    public const string ParticipantInvalidCode = "trip_log.participant_invalid";

    /// <summary>An area filter that is not an identifier.</summary>
    public const string AreaInvalidCode = "trip_log.area_invalid";

    /// <summary>
    /// How many identifiers one facet may name. The two facets that take them each cost a
    /// separate gated read per value — the roster's own rule for a person, the containment walk
    /// for an area — so an unbounded list is an unbounded number of statements. Fifty is what the
    /// panel offers, so a filter a reader can build stays well inside it and only a hand-written
    /// address reaches the bound at all.
    /// </summary>
    private const int MaxIdsPerFacet = 50;

    /// <summary>
    /// The orders a caller may ask for. Written out rather than derived, so an order is a
    /// decision somebody took and not whatever a column happens to be called.
    /// </summary>
    private static readonly Dictionary<string, TripListSort> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["date"] = TripListSort.DateAscending,
        ["-date"] = TripListSort.DateDescending,
        ["title"] = TripListSort.TitleAscending,
        ["-title"] = TripListSort.TitleDescending,
        ["created"] = TripListSort.CreatedAscending,
        ["-created"] = TripListSort.CreatedDescending,
        ["updated"] = TripListSort.UpdatedAscending,
        ["-updated"] = TripListSort.UpdatedDescending,
    };

    public static bool TryParse(
        DateOnly? from,
        DateOnly? to,
        Guid? caveId,
        Guid? expeditionId,
        string? participantIds,
        string? areaIds,
        string? types,
        string? states,
        string? visibilities,
        bool? hadIncident,
        string? search,
        string? sort,
        out TripLogListFilter filter,
        out ProblemHttpResult? problem)
    {
        filter = null!;
        problem = null;

        if (!TryIds(participantIds, ParticipantInvalidCode, out var participants, out problem)
            || !TryIds(areaIds, AreaInvalidCode, out var areas, out problem))
        {
            return false;
        }

        var typeIds = new List<long>();
        foreach (var word in Split(types))
        {
            if (!long.TryParse(word, out var typeId))
            {
                problem = ApiProblems.BadRequest(TypeInvalidCode, $"Unknown trip type '{word}'.");
                return false;
            }

            typeIds.Add(typeId);
        }

        var stateValues = new List<ActivityState>();
        foreach (var word in Split(states))
        {
            if (!Enum.TryParse<ActivityState>(word, ignoreCase: true, out var state)
                || !Enum.IsDefined(state)
                || !ActivityStates.IsTripLogState(state))
            {
                problem = ApiProblems.BadRequest(StateInvalidCode, $"Unknown state '{word}'.");
                return false;
            }

            stateValues.Add(state);
        }

        var visibilityValues = new List<Visibility>();
        foreach (var word in Split(visibilities))
        {
            if (!Enum.TryParse<Visibility>(word, ignoreCase: true, out var visibility) || !Enum.IsDefined(visibility))
            {
                problem = ApiProblems.BadRequest(VisibilityInvalidCode, $"Unknown visibility '{word}'.");
                return false;
            }

            visibilityValues.Add(visibility);
        }

        var order = TripListSort.DateDescending;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            if (!Sorts.TryGetValue(sort.Trim(), out order))
            {
                problem = ApiProblems.BadRequest(SortInvalidCode, $"Unknown sort '{sort}'.");
                return false;
            }
        }

        filter = new TripLogListFilter(
            from,
            to,
            caveId,
            expeditionId,
            participants,
            areas,
            [.. typeIds.Distinct()],
            [.. stateValues.Distinct()],
            [.. visibilityValues.Distinct()],
            hadIncident,
            search,
            order);
        return true;
    }

    /// <summary>
    /// One facet's chosen identifiers. A word that is not one is refused rather than dropped: a
    /// listing that quietly ignored half a filter would answer a question nobody asked, and the
    /// code says which control to put right.
    /// </summary>
    private static bool TryIds(
        string? value, string code, out IReadOnlyList<Guid> ids, out ProblemHttpResult? problem)
    {
        problem = null;
        var parsed = new List<Guid>();
        foreach (var word in Split(value))
        {
            if (!Guid.TryParse(word, out var id))
            {
                ids = [];
                problem = ApiProblems.BadRequest(code, $"Unknown identifier '{word}'.");
                return false;
            }

            parsed.Add(id);
        }

        ids = [.. parsed.Distinct()];
        if (ids.Count > MaxIdsPerFacet)
        {
            ids = [];
            problem = ApiProblems.BadRequest(code, $"At most {MaxIdsPerFacet} may be named at once.");
            return false;
        }

        return true;
    }

    private static IEnumerable<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
