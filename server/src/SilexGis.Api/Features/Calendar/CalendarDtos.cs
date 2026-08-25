// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Calendar;

/// <summary>Which family of dated record a calendar row came from.</summary>
public enum CalendarSource
{
    /// <summary>A trip.</summary>
    TripLog = 0,

    /// <summary>A camp, whose span the calendar reads rather than copying.</summary>
    Expedition = 1,
}

/// <summary>
/// One row on a calendar: what it is called, when it is, and enough to click through to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no cave-derived field of any kind, and that is the rule this row is built
/// around.</b> Naming a cave is a read of the cave, decided by a walk of its own that also has to
/// say how many were held back, and every surface that names one pays for both. A row that names
/// none owes neither, which is what lets a whole month be answered from the dated tables alone
/// with no per-row disclosure decision. The moment a cave, or a count of caves, or anything
/// derived from one is wanted here, this stops being a projection and becomes another caller of
/// that walk — which is a piece of work in its own right, not a field.
/// </para>
/// <para>
/// The title is the row's own title, as written. Who may read the row has already been decided
/// before it is built, and the row is not shown to anybody else; rewriting the title for a reader
/// entitled to the row would withhold from them something the row's own page hands over.
/// </para>
/// <para>
/// Nothing here is a coordinate. <see cref="HasPosition"/> says only whether the source row
/// carries one, so a calendar can offer a map without the calendar itself being a second path
/// that emits positions.
/// </para>
/// </remarks>
/// <param name="Source">Which table the row was read from.</param>
/// <param name="Id">The source row's identifier, which is what a reader clicks through to.</param>
/// <param name="Title">The row's own title or name, as written.</param>
/// <param name="Start">The first day of the row's span.</param>
/// <param name="End">The last day, absent when the span lasted a single day.</param>
/// <param name="StartTime">
/// The wall-clock time the row carries for its first day, where it carries one. For a trip that is
/// the time the party goes underground, which is the only time of day a trip states; it is offered
/// so a day's rows can be ordered within the day rather than as a claim about when people meet.
/// </param>
/// <param name="EndTime">The wall-clock time the row carries for its last day, where it has one.</param>
/// <param name="State">Where the row has got to in its lifecycle.</param>
/// <param name="Placement">
/// Where that state puts the row on a calendar. Sent rather than derived on the client, so the
/// rule deciding what a grid may draw stays in one place.
/// </param>
/// <param name="CavingGroupId">
/// The group whose calendar this row belongs on — the organising group for a trip, the owning
/// group for a camp. Absent when no group is named.
/// </param>
/// <param name="HasPosition">Whether the source row carries a position at all. Never a position.</param>
/// <remarks>
/// <b>There is deliberately no kind-of-record member here yet.</b> Neither dated source this
/// answer reads carries anything of the sort, so the member could only ever be absent on every
/// row that exists — and a field that is always empty teaches a client to ignore it, which is
/// exactly the habit that makes a real value arriving later go unnoticed. The member lands with
/// the first source that has one to put in it. Widening this record is a deliberate act: two
/// tests pin the member list, one over the wire and one over the client's generated type, and
/// both have to be edited by hand for a new member to ship.
/// </remarks>
public sealed record CalendarEntryDto(
    CalendarSource Source,
    Guid Id,
    string Title,
    DateOnly Start,
    DateOnly? End,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    ActivityState State,
    CalendarPlacement Placement,
    Guid? CavingGroupId,
    bool HasPosition);

/// <summary>
/// Every row of the asked-for window this caller may read, and — when the backstop cap bit — how
/// many more there were.
/// </summary>
/// <param name="Entries">The rows, ordered as asked.</param>
/// <param name="Omitted">
/// How many further rows the window held that the answer could not carry. Zero is the ordinary
/// answer and is a real count, not a shrug: the cap is a backstop that a club-sized window does
/// not reach. It is counted over exactly the rows this caller may read, after their visibility
/// and every filter has been applied, so it says how much of <em>their</em> answer is missing and
/// never how much of the table is. A truncation nobody is told about reads exactly like a
/// complete answer, which is the one thing a record of a month must not do.
/// </param>
public sealed record CalendarResultDto(
    IReadOnlyList<CalendarEntryDto> Entries,
    int Omitted);
