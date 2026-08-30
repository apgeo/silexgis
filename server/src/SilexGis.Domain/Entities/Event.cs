// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What kind of thing a calendar event is. A closed list, stored as smallint and append-only:
/// values are part of the schema contract — never renumber, never reuse.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately an enum rather than a table of kinds anybody can add to. A controlled vocabulary
/// earns its table when it <i>carries</i> something — the trip type does, and that is why it is
/// one: a type names the field-data, logistics and safety schemas a trip is measured against and
/// the list its party works through. An event kind carries nothing at all; it is a word on a chip
/// and a filter value. A table for it would buy an admin screen, a seeder and a migration and
/// would change no behaviour anywhere.
/// </para>
/// <para>
/// A camp is not a kind here. It is already an entity of its own with its own audience, its own
/// lifecycle and its own listing, and the calendar reads it as a source rather than copying it —
/// so a kind for it would be a second, emptier way of saying the same thing, free to disagree
/// with the first. A trip is not a kind here for the same reason.
/// </para>
/// </remarks>
public enum EventKind : short
{
    /// <summary>The club's own meeting — the recurring evening in the calendar.</summary>
    ClubMeeting = 0,

    /// <summary>Training or a course: somebody teaching, somebody learning.</summary>
    Training = 1,

    /// <summary>A working day on the cave, the hut or the club's gear.</summary>
    MaintenanceDay = 2,

    /// <summary>Checking the club's equipment over — ropes, hangers, lamps.</summary>
    GearCheck = 3,

    /// <summary>A congress, a symposium, a meet somebody travels to.</summary>
    Conference = 4,

    /// <summary>
    /// A date something is due by — a permit application, a report to a body, a subscription.
    /// One kind and not two: "permit deadline" and "report deadline" differ in what the text
    /// says and in nothing the application does.
    /// </summary>
    Deadline = 5,
}

/// <summary>
/// The rules that follow from what kind of event a row is, in one place.
/// </summary>
public static class EventKinds
{
    /// <summary>
    /// Whether people are asked to say whether they are coming to an event of this kind.
    /// </summary>
    /// <remarks>
    /// A deadline is a date, not a gathering: there is nobody to come to it, so there is nothing
    /// to answer. It is a first-class row with nothing on it rather than an ordinary event with a
    /// flag turned off, because a flag is a column somebody can set the wrong way, while this is
    /// a property of the kind itself and is the same answer for every row of it.
    /// <para>
    /// The default is <c>false</c> deliberately: a kind added to the vocabulary and not named
    /// here accepts nothing until somebody decides that it should. Silence costs a decision; the
    /// other default would quietly open a sign-up sheet on something nobody meant to run one for.
    /// </para>
    /// </remarks>
    public static bool AcceptsResponses(EventKind kind) => kind switch
    {
        EventKind.ClubMeeting => true,
        EventKind.Training => true,
        EventKind.MaintenanceDay => true,
        EventKind.GearCheck => true,
        EventKind.Conference => true,
        EventKind.Deadline => false,
        _ => false,
    };
}

/// <summary>
/// Something dated the club puts in its calendar that is not a trip and not a camp — a meeting, a
/// training weekend, a working day, a deadline.
/// </summary>
/// <remarks>
/// <para>
/// It carries the owner/caving-group/visibility trio, so it is governed exactly like a trip or a
/// camp: by its owner, by its audience, and by the entries written against it. One caving-group
/// column and not two. A trip needs a second one because it is a record of <i>who went where</i>,
/// so the club that organised it and the audience that may read it are genuinely different facts;
/// an event is a thing a group runs, and the group that runs it is the group it is for. If one
/// club runs an event another club is to read, that is an entry scoped to this event, not a
/// second column every reader would then have to reconcile.
/// </para>
/// <para>
/// Its dates are floating calendar days and its times are wall-clock with no zone, the same
/// reading every other dated row on the calendar already carries. The cost is stated rather than
/// discovered: a 19:00 meeting reads as 19:00 to every reader wherever they are. That is right for
/// a club that meets in one place, and it is exactly what a trip's entry and exit times already
/// do. An instant would be worse than merely different — the grid asks "which cell is this in",
/// and a zoned value answers it one way in the query that bounds the window and another way in the
/// formatter that draws the cell.
/// </para>
/// <para>
/// There is no geometry. A meeting has an address, not a position: nobody navigates to a club
/// night by coordinate, and a column of them would put a fifth coordinate-bearing world into the
/// story about which positions may be disclosed to whom, in exchange for nothing anybody asked
/// for.
/// </para>
/// </remarks>
public class Event : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    /// <summary>What it is and what to bring — the narrative, free text.</summary>
    public string? Description { get; set; }

    public EventKind Kind { get; set; }

    /// <summary>The day it happens, or the first day when it runs on.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// The last day, or null when it lasts a single day. An end equal to the start is stored as
    /// null so that one day never reads as a range of itself, and every reader can ask "did this
    /// run on past its first day" by testing one column for null — the same rule and the same
    /// normalisation every other dated row on the calendar is written against.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// When it starts, wall-clock and without a zone, or null when the day is all anybody needs
    /// to know. The day a time belongs to comes from the dates above and never from the time.
    /// </summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>
    /// When it ends, read the same way as <see cref="StartTime"/>. Deliberately not required to
    /// follow the start: a time carries no day, so a meeting that runs from 21:00 to 00:30 is an
    /// ordinary evening rather than a mistake.
    /// </summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>
    /// Where it is, as somebody would write it down for a person to read — a club room, a hut, a
    /// street address. Text and not a position, deliberately: see the remarks on the class.
    /// </summary>
    public string? Place { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>
    /// Where the event has got to, from somebody's idea through to being announced. A new event
    /// starts as a draft: it is being written and nobody has been told.
    /// </summary>
    /// <remarks>
    /// Never consulted when deciding who may read the row. Visibility and the access entries
    /// answer that on their own, and a second rule saying who may read a row is how the two come
    /// to disagree — a draft with public visibility is public, and that is correct.
    /// </remarks>
    public ActivityState State { get; set; } = ActivityState.Draft;

    /// <summary>
    /// When the event was first announced, or null while it never has been. Stamped once and
    /// never cleared, so de-announcing and announcing again does not move it.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// When the people who answered about this event were last reminded that it is coming up, or
    /// null while they have not been. Written by the same scheduled pass that reminds people about
    /// a trip, and the whole of that reminder's idempotence — without it every pass in the run-up
    /// would send another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reminder is not armed ahead of time as a queued message, for the same reason a trip's is
    /// not: a queued message cannot be recalled, so an event put back or called off would still
    /// remind everybody about a date that is no longer true.
    /// </para>
    /// <para>
    /// Never cleared. An event already reminded about, then put back and planned again on a new
    /// date, is not reminded a second time — the same deliberate reading of "once" a trip carries.
    /// A repeating evening does not suffer by it, because each occurrence is a row of its own with
    /// a stamp of its own.
    /// </para>
    /// </remarks>
    public DateTimeOffset? PlanReminderSentAt { get; set; }

    /// <summary>
    /// How many people the event has room for, or null when it states no limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A limit here never refuses an answer. Somebody who says they are coming to a full evening
    /// is recorded as having said so and waits, because who else wanted to come and in what order
    /// they said so is exactly the record a limit is kept for — and a refusal would destroy it.
    /// </para>
    /// <para>
    /// Who is in and who is waiting is worked out from the answers whenever it is asked, by taking
    /// them in the order they were given and counting up to this number. It is not written down
    /// anywhere: a stored place in a queue starts disagreeing with the answers the moment somebody
    /// changes their mind, and there would then be two records of the same thing with no way to
    /// tell which was stale.
    /// </para>
    /// <para>
    /// A kind that accepts no answers has nothing to count, so the number is meaningless on one
    /// rather than forbidden: it is a room size somebody may have typed before the kind was
    /// changed, and refusing the save would lose the rest of what they wrote.
    /// </para>
    /// </remarks>
    public int? MaxParticipants { get; set; }

    /// <summary>
    /// The series this event is one occurrence of, or null when it stands on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A grouping key and nothing more. There is no series table and no series row: a series is
    /// the set of events that share this value, and every one of them is an ordinary event. That
    /// is the whole design. Everything that already keys on one event's identifier — the answer
    /// somebody gave, the rules anchored on it, its version token, the page it lands on, the
    /// trail its answers hang off — keeps keying on one identifier, because an occurrence is a
    /// row rather than a date computed from a rule. The alternative, a stored rule expanded when
    /// somebody looks, would have made every one of those mechanisms need to say <i>which</i>
    /// occurrence it meant, and none of them has anywhere to put it.
    /// </para>
    /// <para>
    /// It carries no foreign key, deliberately, because there is nothing to point at. Deleting
    /// every occurrence of a series leaves no orphan and nothing to tidy up: the series simply
    /// stops existing, the way a word stops existing when nobody says it.
    /// </para>
    /// </remarks>
    public Guid? SeriesId { get; set; }

    /// <summary>
    /// How the series repeats, in the words its author used — "every Tuesday", "first Monday of
    /// the month, term time". Null on an event that is not part of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing reads this but a person.</b> It is never parsed, never matched, and no dated row
    /// anywhere is derived from it: the days were worked out once, when the occurrences were
    /// written, from a repetition the author picked from a short list, and that choice is not
    /// stored because there is nothing left to do with it. What is stored is the sentence a reader
    /// needs in order to understand why the same evening appears twelve times.
    /// </para>
    /// <para>
    /// It sits on every occurrence rather than in one place, because there is no one place: the
    /// series is the rows. The cost is the same sentence written a dozen times; the gain is that
    /// an occurrence answers "what is this part of" out of the row somebody already loaded, with
    /// no second table to join, to authorise, to audit, or to leave behind when the last
    /// occurrence goes.
    /// </para>
    /// </remarks>
    public string? SeriesRule { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
