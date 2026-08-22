// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// Where a trip's overdue check stands: whether anybody is waiting to hear that the party is out,
/// and whether that wait has already run out.
/// </summary>
/// <remarks>
/// <para>
/// Stored as smallint and append-only — the values travel to a client and are selected on by a
/// scheduled pass, so a member keeps the number it was given.
/// </para>
/// <para>
/// This is the whole of the check's idempotence. The pass that notices an overdue party writes the
/// row and queues the message in one save, so a party is moved out of <see cref="Armed"/> exactly
/// once however many times the pass runs, and a second pass finds nothing to do. It is on the trip
/// rather than on the queued message because the queue has no key to ask "was this one already
/// sent about" with.
/// </para>
/// <para>
/// Standing an alarm down does not erase it: the row keeps the times that were armed, so what was
/// arranged is still readable after the party is home. Nothing here is ever consulted when deciding
/// who may read the trip.
/// </para>
/// </remarks>
public enum TripCalloutState : short
{
    /// <summary>
    /// Nobody arranged one. The state a trip is in until somebody says when they will be back, and
    /// the only safe reading of an absent value.
    /// </summary>
    None = 0,

    /// <summary>Somebody is expected back, and the check is live.</summary>
    Armed = 1,

    /// <summary>The time passed and nobody stood it down; whoever the trip names has been told.</summary>
    Overdue = 2,

    /// <summary>
    /// Somebody on the trip said they were out. The check is over — reaching it from
    /// <see cref="Overdue"/> as well as from <see cref="Armed"/>, because a party that surfaces
    /// late still surfaces.
    /// </summary>
    StoodDown = 3,
}

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

    /// <summary>
    /// How many people the trip has room for, or null when it has no stated limit — which is
    /// what a trip has until somebody says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It never refuses a write. Nothing is rejected for exceeding it: somebody who says they
    /// are coming to a full trip is recorded as having said so, because who else wanted to come
    /// and in what order they said it is precisely the record a limit makes worth keeping, and a
    /// write refused at the door destroys it. Whoever runs the trip then has something to choose
    /// from instead of a silence.
    /// </para>
    /// <para>
    /// Who is in and who is waiting is therefore worked out from the answers whenever it is
    /// asked, by taking them in the order they were given and counting up to this number. It is
    /// not written down anywhere: a stored place in a queue is a fact that begins disagreeing
    /// with the answers the moment somebody changes their mind, and there would then be two
    /// records of the same thing with no way to tell which was stale.
    /// </para>
    /// </remarks>
    public int? MaxParticipants { get; set; }

    /// <summary>
    /// When the party said they would be back out, or null while the trip has no callout. Held as
    /// an instant rather than as the wall-clock times the trip's own entry and exit use, because it
    /// is compared against "now" by something running with every browser closed, and a comparison
    /// like that cannot be made against a time with no day and no zone attached to it.
    /// </summary>
    public DateTimeOffset? ExpectedReturnAt { get; set; }

    /// <summary>
    /// When the alarm goes off if nobody has said the party is out, or null while the trip has no
    /// callout. A separate instant from <see cref="ExpectedReturnAt"/> and not derived from it: the
    /// grace somebody wants between being late and being reported is theirs to choose, and it is
    /// not the same hour for a half-day through a known system as for a first descent.
    /// </summary>
    public DateTimeOffset? CalloutAlarmAt { get; set; }

    /// <summary>
    /// Where the overdue check stands. Never null: a trip nobody arranged one for is
    /// <see cref="TripCalloutState.None"/>, so nothing reading this has to tell an absent answer
    /// from "no callout", and a party is never left in a state that could be read as either.
    /// </summary>
    public TripCalloutState CalloutState { get; set; } = TripCalloutState.None;

    /// <summary>
    /// When the people on this trip were last reminded that it is coming up, or null while they
    /// have not been. Written by the same scheduled pass that watches the callout, and the whole of
    /// that reminder's idempotence — without it every pass in the run-up would send another.
    /// </summary>
    /// <remarks>
    /// The reminder is not armed ahead of time as a queued message, for the same reason the alarm
    /// is not: a queued message cannot be recalled, so a trip put back or called off would still
    /// remind everybody about a date that is no longer true.
    /// </remarks>
    public DateTimeOffset? PlanReminderSentAt { get; set; }

    public Geometry? Geom { get; set; }

    /// <summary>
    /// Where the party gathers before it sets off, and — where a club draws one — the way in to
    /// it. A column rather than a line in the plan's written arrangements because a map has to
    /// find it: somebody looking at a week of trips on a map is asking where to be and when, and
    /// a position buried in a form's stored answers is a position no query can select on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Any geometry class, like the trip's own sketch: a meeting point is usually a point, and a
    /// club that draws the approach as well records the two together rather than in a second
    /// column. One shape, so there is one answer to "where does this trip start" and no rule
    /// about which of two to believe.
    /// </para>
    /// <para>
    /// It is served exactly to everybody who may read the trip, which is the same bargain the
    /// trip's own sketch has always carried, and it inherits that bargain's cost: a meeting point
    /// drawn two hundred metres from a guarded entrance places that entrance for every reader of
    /// the trip, including one the trip is at that moment refusing to tell which caves it names.
    /// Nothing here narrows it, and no surface may hand it to somebody the trip itself would not.
    /// </para>
    /// </remarks>
    public Geometry? MeetingGeom { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>
    /// Where the trip has got to in its lifecycle. A new trip starts as a draft whichever it is
    /// going to be — a plan somebody is still writing or a report of an outing already run —
    /// because the state it moves to next is the act that decides which, and nobody named on it
    /// is told about it while it is being written.
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

/// <summary>
/// A person tied to a trip, named through the roster, doing one job on it. The same identity
/// model serves everybody the trip names — whoever was simply there, whoever put it forward,
/// whoever led or surveyed or drove — told apart by <see cref="RoleId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pointing at a caver rather than carrying an account id beside a free-text name is what makes
/// per-person history work for the majority who never sign in: "which caves has this person been
/// to" is one join whether or not they have an account. A caver named on a trip cannot be
/// deleted — the roster offers merging two entries for the same person instead.
/// </para>
/// <para>
/// One person on one trip is one row per job, not one row: somebody can be the leader and the
/// surveyor, and asking them to pick would lose one of the two facts. What a row is unique on is
/// therefore the trip, the role and the person together.
/// </para>
/// </remarks>
public class TripLogParticipant : IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    /// <summary>What they did on the trip, from the club-extensible role vocabulary.</summary>
    public long RoleId { get; set; }

    public Guid CaverId { get; set; }

    /// <summary>
    /// When this person went underground and came back out, wall-clock and without a zone, the
    /// same reading the trip's own times carry — so the two can never disagree about what a time
    /// means. Null is not "unknown": it means the trip's own time stands for them, which is the
    /// true answer for almost everybody and is why recording an ordinary roster stays a list of
    /// names. The day a time belongs to comes from the trip's date range, never from the time.
    /// </summary>
    public TimeOnly? EntryTime { get; set; }

    /// <inheritdoc cref="EntryTime"/>
    public TimeOnly? ExitTime { get; set; }

    /// <summary>
    /// What was particular about this person's part in the trip — "turned back at the pitch
    /// head", "surfaced early with the second group". Free text on purpose: it is what stops a
    /// one-off circumstance being invented as a role and left in the vocabulary forever.
    /// </summary>
    public string? Note { get; set; }

    public string AuditId => Id.ToString();

    // Participants surface in their trip's timeline.
    public string RootEntityType => nameof(TripLog);

    public string RootEntityId => TripLogId.ToString();
}
