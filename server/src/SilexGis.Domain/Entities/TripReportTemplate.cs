// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A club's own layout for the document a trip is written up as.
/// </summary>
/// <remarks>
/// <para>
/// Only layouts somebody wrote are stored. An installation that has never edited one has no rows
/// here and its write-ups come out under the layout shipped with the product, which means such an
/// installation picks up improvements to it rather than a frozen copy of whatever it looked like
/// on the day somebody first pressed a button.
/// </para>
/// <para>
/// The body is checked when it is written, never when a document is produced: a club secretary
/// finds out that a line is wrong while they are editing the file, not six weeks later when the
/// bulletin is due.
/// </para>
/// </remarks>
public class TripReportTemplate : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>What the club calls this layout when choosing between them.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What this layout writes up. A layout is only ever offered for, and only ever read against,
    /// the kind it was written for — the two vocabularies differ, so a camp layout applied to a
    /// trip would name fields that trip has no answer for.
    /// </summary>
    public ReportTemplateKind Kind { get; set; } = ReportTemplateKind.Trip;

    /// <summary>The layout itself, in the small line-per-instruction language.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Whether a write-up asked for without naming a layout uses this one. At most one row may
    /// say yes, which the database enforces.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
