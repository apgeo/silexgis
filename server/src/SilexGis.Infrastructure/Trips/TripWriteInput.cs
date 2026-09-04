// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// Someone to put on a trip: an existing roster entry, or a name to add one for. A role of null
/// means the role of the list the entry arrived in.
/// </summary>
public sealed record TripRosterEntry
{
    public Guid? CaverId { get; init; }

    public string? NewCaverName { get; init; }

    public long? RoleId { get; init; }

    public TimeOnly? EntryTime { get; init; }

    public TimeOnly? ExitTime { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Everything a trip write states, in terms this layer owns: geometry as geometry, sections as
/// raw text, no wire format anywhere. One shape for every caller — the two creation doors, the
/// edit door, and anything loading trips in bulk — so a rule applied to one is applied to all.
/// </summary>
/// <remarks>
/// <para>
/// Named properties rather than a positional record, deliberately. It carries several runs of
/// same-typed members — three nullable decimals, two nullable times, three nullable identifiers —
/// and in a positional shape inserting a member anywhere but the end silently re-seats every
/// argument after it wherever the types happen to line up, with nothing failing to compile. The
/// wire shape a request arrives in has to live with that constraint; this one does not, and a
/// caller that assembles a trip row by row is exactly the caller most likely to be extended.
/// </para>
/// <para>
/// The nullability carries meaning and it is the same meaning the wire shape gives it. A null
/// cave list means "not editing which caves this trip is about" and an empty one means "none of
/// them"; a null audience means "not deciding who may read it", which on creation is answered by
/// the rule for the door and on an edit leaves what is stored alone; an absent section leaves the
/// stored section alone while an empty object clears it.
/// </para>
/// </remarks>
public sealed record TripWriteInput
{
    public required string Title { get; init; }

    public long? TripTypeId { get; init; }

    public DateOnly TripDate { get; init; }

    public DateOnly? TripDateEnd { get; init; }

    public TimeOnly? EntryTime { get; init; }

    public TimeOnly? ExitTime { get; init; }

    public string? Description { get; init; }

    public string? Results { get; init; }

    public string? WeatherConditions { get; init; }

    public string? LocationText { get; init; }

    public Guid? OrganizingCavingGroupId { get; init; }

    public Geometry? Geom { get; init; }

    public Geometry? MeetingGeom { get; init; }

    /// <summary>
    /// Set when a shape did arrive but could not be read as a geometry, which is a different
    /// answer from no shape at all. Whoever parsed the wire format knows this and the write core
    /// does not, but the refusal belongs here: it is ordered among the other reference checks and
    /// therefore comes after the decision about whether this caller may create a trip, so a
    /// caller who may not is told that rather than being told about their geometry.
    /// </summary>
    public bool GeometryMalformed { get; init; }

    /// <summary>
    /// The caves the trip is about, or null to leave the caves its roles already name alone.
    /// </summary>
    public IReadOnlyList<Guid>? CaveIds { get; init; }

    /// <summary>
    /// The roster, whole: this list and <see cref="Proposers"/> together replace every row the
    /// trip has, in every role, so a job left out of them is a job withdrawn.
    /// </summary>
    public IReadOnlyList<TripRosterEntry> Participants { get; init; } = [];

    public IReadOnlyList<TripRosterEntry>? Proposers { get; init; }

    public Guid? CavingGroupId { get; init; }

    public Visibility? Visibility { get; init; }

    public decimal? DepthReachedM { get; init; }

    public decimal? LengthSurveyedM { get; init; }

    public int? SurveyStations { get; init; }

    public decimal? RopeMetres { get; init; }

    public bool HadIncident { get; init; }

    public int? MaxParticipants { get; init; }

    /// <summary>The three sections as raw text, or null for one the write does not mention.</summary>
    public TripSectionWrite Sections { get; init; } = new(null, null, null);
}

/// <summary>
/// What an edit turned out to have done, for the notices the caller sends that this core does
/// not: who the write newly named, which caves it newly named, and whether it moved anything at
/// all.
/// </summary>
public sealed record TripWriteOutcome(
    IReadOnlyList<Guid> NewlyNamedUserIds,
    IReadOnlyList<Guid> AddedCaveIds,
    bool SomethingChanged);
