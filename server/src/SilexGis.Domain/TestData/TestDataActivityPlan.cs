// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;

namespace SilexGis.Domain.TestData;

/// <summary>What a run invents of what people did: trips, camps and club events.</summary>
public sealed record TestDataActivityPlan(
    IReadOnlyList<PlannedTrip> Trips,
    IReadOnlyList<PlannedExpedition> Expeditions,
    IReadOnlyList<PlannedEventSeries> Events)
{
    public static TestDataActivityPlan Empty { get; } = new([], [], []);
}

/// <summary>Somebody on a trip, by their place on the roster, and as what (a shipped role code; null for a plain participant).</summary>
public sealed record PlannedTripMember(int CaverIndex, string? RoleCode, TimeOnly? Entry = null, TimeOnly? Exit = null);

/// <summary>A caver asked to come on a planned trip or to an event, and what they answered.</summary>
public sealed record PlannedInvitation(int CaverIndex, TripInvitationResponse Response, string? Note);

public sealed record PlannedTrip
{
    public required int Index { get; init; }

    public required string Title { get; init; }

    /// <summary>A shipped trip type's code, or the run's added one.</summary>
    public required string TypeCode { get; init; }

    public required DateOnly Date { get; init; }

    public DateOnly? DateEnd { get; init; }

    public TimeOnly? Entry { get; init; }

    public TimeOnly? Exit { get; init; }

    public required ActivityState State { get; init; }

    /// <summary>Whether the trip lies in the run's future: planned rather than reported.</summary>
    public required bool Future { get; init; }

    public required IReadOnlyList<int> CaveIndices { get; init; }

    public required IReadOnlyList<PlannedTripMember> Members { get; init; }

    /// <summary>Who proposed a planned trip, by roster place.</summary>
    public IReadOnlyList<int> Proposers { get; init; } = [];

    public IReadOnlyList<PlannedInvitation> Invitations { get; init; } = [];

    /// <summary>The teams a large trip split into.</summary>
    public IReadOnlyList<string> Teams { get; init; } = [];

    /// <summary>The club the trip is bound to, by plan index; null for one bound to none.</summary>
    public int? ClubIndex { get; init; }

    public int? OrganizingClubIndex { get; init; }

    public required Visibility Visibility { get; init; }

    public string? Description { get; init; }

    public string? Results { get; init; }

    public string? Weather { get; init; }

    public decimal? DepthReachedM { get; init; }

    public decimal? LengthSurveyedM { get; init; }

    public int? SurveyStations { get; init; }

    public decimal? RopeMetres { get; init; }

    public bool HadIncident { get; init; }

    /// <summary>The camp the trip was part of, by plan index.</summary>
    public int? ExpeditionIndex { get; init; }

    /// <summary>Deleted by somebody since, and waiting in the bin.</summary>
    public bool Deleted { get; init; }
}

public sealed record PlannedRosterEntry(int CaverIndex, string RoleCode, DateOnly From, DateOnly? To);

public sealed record PlannedExpedition(
    int Index,
    string Name,
    string RegionCode,
    int? SubAreaIndex,
    DateOnly Start,
    DateOnly End,
    ActivityState State,
    int ClubIndex,
    Visibility Visibility,
    string Description,
    string? PublicNote,
    IReadOnlyList<PlannedRosterEntry> Roster);

/// <summary>One event, or a run of them repeating; a single event has a count of one.</summary>
public sealed record PlannedEventSeries(
    int Index,
    string Title,
    EventKind Kind,
    DateOnly Start,
    int SpanDays,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Place,
    int ClubIndex,
    Visibility Visibility,
    ActivityState State,
    EventRecurrenceFrequency? Frequency,
    int Count,
    string? Rule,
    string? Description,
    int? MaxParticipants,
    IReadOnlyList<PlannedInvitation> Invitations);
