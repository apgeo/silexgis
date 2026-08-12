// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A dated exploration/visit report: who went where and what happened. Optionally
/// carries a location geometry (point or area) and links to the caves involved.
/// </summary>
public class TripLog : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    /// <summary>
    /// What the trip was for, as a row in the trip-purpose vocabulary rather than a value fixed
    /// at build time: a club that runs a kind of trip nobody thought of adds it themselves.
    /// Null while a trip does not say — a half-written record is a real state.
    /// </summary>
    public long? TripTypeId { get; set; }

    public DateOnly TripDate { get; set; }

    public DateOnly? TripDateEnd { get; set; }

    /// <summary>Underground entry/exit wall-clock times (no time zone; duration is derived).</summary>
    public TimeOnly? EntryTime { get; set; }

    public TimeOnly? ExitTime { get; set; }

    public string? Description { get; set; }

    /// <summary>Outcomes/observations, kept distinct from the narrative <see cref="Description"/>.</summary>
    public string? Results { get; set; }

    public string? WeatherConditions { get; set; }

    public string? LocationText { get; set; }

    /// <summary>
    /// How deep the trip got, in metres below the entrance. Null while the trip does not say.
    /// </summary>
    /// <remarks>
    /// Metres, always, for this and every other measurement on a trip — one unit stored, formatted
    /// to whatever a reader's locale wants. A unit column beside the number would mean every sum,
    /// ranking and comparison had to convert first, and one row with the wrong unit would poison
    /// all three silently. Stored to a decimetre; a finer figure is rounded to it.
    /// </remarks>
    public decimal? DepthReachedM { get; set; }

    /// <summary>New passage surveyed on the trip, in metres. Null while the trip does not say.</summary>
    public decimal? LengthSurveyedM { get; set; }

    /// <summary>Survey stations set on the trip. A count, so a whole number.</summary>
    public int? SurveyStations { get; set; }

    /// <summary>Rope used, in metres. Null while the trip does not say.</summary>
    public decimal? RopeMetres { get; set; }

    /// <summary>
    /// Whether anything went wrong on the trip. A fact of the row, deliberately separate from any
    /// account of what happened: a club reviews <em>that</em> there was an incident — counts them,
    /// finds them, notices a run of them — before it reads <em>what</em> it was, and the two answer
    /// to different audiences. Never null: "nobody said" and "nothing happened" are not worth
    /// telling apart here, and a nullable flag would make every count ask which it meant.
    /// </summary>
    public bool HadIncident { get; set; }

    /// <summary>
    /// What the trip found underground, as a bag of values the purpose's field-data schema
    /// describes (jsonb). Always an object — a trip that says nothing says <c>{}</c>, so a
    /// reader never has to tell "no answers" from "no bag".
    /// </summary>
    public string FieldData { get; set; } = "{}";

    /// <inheritdoc cref="SafetySchemaVersion"/>
    public int? FieldDataSchemaVersion { get; set; }

    /// <summary>What the trip needed to happen — permits, keys, access, costs (jsonb).</summary>
    public string Logistics { get; set; } = "{}";

    /// <inheritdoc cref="SafetySchemaVersion"/>
    public int? LogisticsSchemaVersion { get; set; }

    /// <summary>
    /// What went wrong and what was learned (jsonb). Read by a narrower audience than the rest
    /// of the trip: <see cref="HadIncident"/> says that something happened and is told to
    /// everyone who may read the trip, while this says what it was and names identifiable
    /// people making mistakes.
    /// </summary>
    public string Safety { get; set; } = "{}";

    /// <summary>
    /// Which version of the purpose's schema for this section the stored bag was measured
    /// against, or null while it has never been measured — either because the purpose carries
    /// no schema for the section, or because nobody has supplied a value yet.
    /// </summary>
    /// <remarks>
    /// A write that supplies the bag is measured against the schema as it stands now; a write
    /// that leaves it alone is measured against the version stamped here. That is what keeps
    /// tightening a schema from invalidating reports already written under the looser one:
    /// they are re-measured only when somebody actually rewrites them.
    /// </remarks>
    public int? SafetySchemaVersion { get; set; }

    /// <summary>The caving group that organized the trip, when one did.</summary>
    public Guid? OrganizingCavingGroupId { get; set; }

    public Geometry? Geom { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>
    /// Where the trip has got to in its lifecycle. A new trip starts as a draft: it is being
    /// written, and nobody named on it is told about it until it is published.
    /// </summary>
    /// <remarks>
    /// This is never consulted when deciding who may read the trip. Visibility and the access
    /// entries answer that on their own, and a second rule saying who may read a row is how the
    /// two come to disagree — a draft with public visibility is public, and that is correct.
    /// </remarks>
    public ActivityState State { get; set; } = ActivityState.Draft;

    /// <summary>
    /// When the trip was first published, or null while it never has been. Stamped once and never
    /// cleared, so unpublishing and publishing again does not move it.
    /// </summary>
    /// <remarks>
    /// The change history could answer this, but it is read on every listing and reconstructing it
    /// per row from an audit trail is the wrong shape for a column that only ever gains a value.
    /// </remarks>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One of the three schema-carrying sections of a trip report. Stored as smallint, and the
/// discriminator on the published-schema history — the codes are a contract, so a value is
/// never re-numbered and a new section is appended.
/// </summary>
public enum TripSection : short
{
    FieldData = 0,
    Logistics = 1,
    Safety = 2,
}

/// <summary>Whether a person attended the trip or proposed it. Stored as smallint.</summary>
public enum TripParticipantKind : short
{
    Participant = 0,
    Proposer = 1,
}

/// <summary>
/// A person tied to a trip, named through the roster. The same identity model serves both
/// attendees and proposers, distinguished by <see cref="Kind"/>; one person may appear once
/// as each.
/// </summary>
/// <remarks>
/// Pointing at a caver rather than carrying an account id beside a free-text name is what makes
/// per-person history work for the majority who never sign in: "which caves has this person been
/// to" is one join whether or not they have an account. A caver named on a trip cannot be
/// deleted — the roster offers merging two entries for the same person instead.
/// </remarks>
public class TripLogParticipant : IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    public TripParticipantKind Kind { get; set; } = TripParticipantKind.Participant;

    public Guid CaverId { get; set; }

    public string AuditId => Id.ToString();

    // Participants surface in their trip's timeline.
    public string RootEntityType => nameof(TripLog);

    public string RootEntityId => TripLogId.ToString();
}
