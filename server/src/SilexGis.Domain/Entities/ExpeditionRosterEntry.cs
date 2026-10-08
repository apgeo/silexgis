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
    /// The last day they were there, or nothing while they are still there. Somebody there for a
    /// single day has a last day equal to their first.
    /// </summary>
    /// <remarks>
    /// Deliberately not the rule the camp's own dates and a trip's follow, where an end equal to
    /// the start is stored as nothing. A camp and a trip are written down as finished things, so
    /// nothing there needs to say "not over yet"; a roster is kept while the camp is running, and
    /// "arrived on the 14th and has not left" is the ordinary state of most of its rows for most
    /// of a fortnight. One empty value cannot say that and "was there for the 14th only" at once,
    /// and a reader who cannot tell them apart can neither count who is at the camp today nor
    /// say how long anybody stayed. <see cref="DayRange.OpenEndForStorage"/> is the write rule and
    /// the database holds the matching constraint.
    /// </remarks>
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
