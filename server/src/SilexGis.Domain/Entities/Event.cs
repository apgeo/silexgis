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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
