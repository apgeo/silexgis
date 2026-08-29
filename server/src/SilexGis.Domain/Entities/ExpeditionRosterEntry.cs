// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One stretch of days somebody was at a camp, in one role: who, from when to when, and why the
/// row is there at all.
/// </summary>
/// <remarks>
/// <para>
/// Never derived from the trips gathered into the camp, and that is the whole reason it is a table
/// rather than a reading of the membership. A camp's people are not its cavers: the cook, the
/// driver and whoever kept the base camp were there for the fortnight and went underground on none
/// of it, so a presence list computed from who was on a trip would erase exactly the people the
/// list exists to record.
/// </para>
/// <para>
/// A row is a person <em>and</em> a role, so somebody who cooked and surveyed is two rows and one
/// person. Anywhere a reader is shown how many people were at a camp, that count is of people and
/// not of rows, or every camp where somebody wore two hats reports more people than were there.
/// </para>
/// <para>
/// Nothing constrains the intervals, deliberately. A person may leave and come back, so two rows
/// for one person on one camp is ordinary rather than an error, and overlapping rows are legal —
/// the alternative is a range type with an exclusion constraint, a schema shape this codebase does
/// not otherwise use, for a need nobody has shown.
/// </para>
/// </remarks>
public class ExpeditionRosterEntry : ITimestamped, IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid ExpeditionId { get; set; }

    public Guid CaverId { get; set; }

    /// <summary>What they were there as, from the club-extensible camp-roster vocabulary.</summary>
    public long RoleId { get; set; }

    /// <summary>The first day they were there.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>
    /// The day they were there until, present only when the stay actually ran on past its first
    /// day — the same rule the camp's own dates follow, so one day never reads as a range of
    /// itself. An absent end therefore says only that nothing ran on past the first day: for a
    /// stay recorded while the camp is still going it reads as still being there, and for one
    /// recorded against a camp that is over it reads as the single day it was.
    /// </summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Why the row reads as it does — arrived late, left early, came back.</summary>
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>
    /// Who was at the camp is a fact about the camp, so it surfaces on the camp's timeline rather
    /// than in a history of its own that nobody would think to open.
    /// </summary>
    public string RootEntityType => nameof(Expedition);

    public string RootEntityId => ExpeditionId.ToString();
}
